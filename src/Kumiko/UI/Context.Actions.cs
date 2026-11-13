using Kumiko.Runtime;

namespace Kumiko.UI;

[Flags]
public enum AccessibilityActions
{
    None = 0,
    Invoke = 1 << (int)AccessibilityAction.Invoke,
    Focus = 1 << (int)AccessibilityAction.Focus,
    SetValue = 1 << (int)AccessibilityAction.SetValue,
    SetRange = 1 << (int)AccessibilityAction.SetRange,
    Toggle = 1 << (int)AccessibilityAction.Toggle,
    SetSelection = 1 << (int)AccessibilityAction.SetSelection,
    Select = 1 << (int)AccessibilityAction.Select,
    Expand = 1 << (int)AccessibilityAction.Expand,
    Collapse = 1 << (int)AccessibilityAction.Collapse,
    Scroll = 1 << (int)AccessibilityAction.Scroll,
    Reveal = 1 << (int)AccessibilityAction.Reveal
}

public enum ActionStatus
{
    Applied, Queued, NotFound, Disabled, Inert, Offscreen, Unsupported, StaleRevision, InvalidValue
}
public sealed record ActionResult(ActionStatus Status, long Revision, string? Detail = null)
{
    public bool Succeeded => Status is ActionStatus.Applied or ActionStatus.Queued;
}

public sealed partial class Context
{
    readonly Queue<(ulong Id, MessageSource Source, AccessibilityAction Action, bool Native)> accessibleInvocations = new();
    ulong accessibleActivation;
    MessageSource? accessibilitySource;
    internal MessageSource? AccessibilitySource => accessibilitySource;

    internal bool Within(ulong id, ulong ancestor)
    {
        for (var index = Array.FindIndex(Tree, 0, Count, n => n.Id == id); index >= 0; index = Tree[index].Parent)
            if (Tree[index].Id == ancestor)
                return true;
        return false;
    }

    AccessibilityActions SupportedActions(int index)
    {
        ref readonly var node = ref Tree[index];
        var actions = node.Interactive ? AccessibilityActions.Focus : AccessibilityActions.None;
        if (node.Interactive && node.Role != Role.TextField)
            actions |= AccessibilityActions.Invoke;
        if (node.Role is Role.Checkbox or Role.Switch)
            actions |= AccessibilityActions.Toggle;
        if (node.Accessible?.Expanded is not null)
            actions |= AccessibilityActions.Expand | AccessibilityActions.Collapse;
        if (node.Role is Role.Cell or Role.Option or Role.TreeItem or Role.Row && node.Interactive)
            actions |= AccessibilityActions.Select;
        if (node.Role == Role.TextField && node.AccessibilityHandler is not null)
        {
            if (node.Accessible?.ReadOnly != true)
                actions |= AccessibilityActions.SetValue;
            if (node.Accessible?.Password != true)
                actions |= AccessibilityActions.SetSelection;
        }
        if (node.Accessible?.Number is not null && node.AccessibilityHandler is not null && node.Accessible.ReadOnly != true)
            actions |= AccessibilityActions.SetRange;
        if (node.Style.Scroll)
            actions |= AccessibilityActions.Scroll | AccessibilityActions.SetRange;
        for (var parent = node.Parent; parent >= 0; parent = Tree[parent].Parent)
            if (Tree[parent].Style.Scroll)
            {
                actions |= AccessibilityActions.Reveal;
                break;
            }
        return actions;
    }

    /// <summary>Validates a semantic action against the current frame and active modal scope.</summary>
    public ActionResult PerformAction(ulong id, AccessibilityRequest request, long? expectedRevision = null)
        => PerformActionCore(id, request, expectedRevision, native: false);

    // UIKit already owns clipping, scrolling and transformed hit testing. Its live
    // control actions still require current identity, enabled state and modal scope,
    // but cannot use the desktop tree's unscrolled geometry as a visibility gate.
    internal ActionResult PerformNativeAction(ulong id) =>
        PerformActionCore(id, new(AccessibilityAction.Invoke), null, native: true);

    ActionResult PerformActionCore(ulong id, AccessibilityRequest request, long? expectedRevision, bool native)
    {
        CheckThread();
        ActionResult Result(ActionStatus status, string? detail = null) => new(status, Revision, detail);
        if (expectedRevision is { } revision && revision != Revision)
            return Result(ActionStatus.StaleRevision, "Inspect the current revision before acting.");
        var index = Array.FindIndex(Tree, 0, Count, n => n.Id == id);
        var node = accessibility.FirstOrDefault(n => n.Id == id);
        if (disposed || index < 0 || node is null)
            return Result(ActionStatus.NotFound);
        if (node.Inert)
            return Result(ActionStatus.Inert, "The target is outside the active modal scope.");
        if (!node.Enabled)
            return Result(ActionStatus.Disabled);
        if (node.Hidden || !native && node.Offscreen && request.Action != AccessibilityAction.Reveal)
            return Result(ActionStatus.Offscreen, "Reveal the target before acting.");
        if (!Enum.IsDefined(request.Action) || !node.Supports(request.Action))
            return Result(ActionStatus.Unsupported);
        var properties = node.Properties;
        if (request.Action is AccessibilityAction.SetRange or AccessibilityAction.Scroll && !double.IsFinite(request.Number) ||
            request.Action == AccessibilityAction.SetRange && (request.Number < properties?.Minimum || request.Number > properties?.Maximum))
            return Result(ActionStatus.InvalidValue);
        if (request.Action == AccessibilityAction.SetValue && request.Text is null)
            return Result(ActionStatus.InvalidValue);
        var previous = accessibilitySource;
        accessibilitySource = new(node.Role, node.Name, Tree[index].Key, PathOf(index));
        try
        {
            if (request.Action == AccessibilityAction.Focus)
                FocusNode(id);
            else if (request.Action == AccessibilityAction.Reveal)
                RevealNode(index);
            else if (Tree[index].Style.Scroll && request.Action is AccessibilityAction.Scroll or AccessibilityAction.SetRange)
            {
                var scroll = Scrolls[id];
                scroll.Offset = (float)Math.Clamp(request.Number + (request.Action == AccessibilityAction.Scroll ? scroll.Offset : 0), 0, scroll.Max);
                scroll.Follow = false;
            }
            else if (request.Action is AccessibilityAction.Expand or AccessibilityAction.Collapse &&
                properties?.Expanded == (request.Action == AccessibilityAction.Expand))
                return Result(ActionStatus.Applied);
            else if (request.Action == AccessibilityAction.Select && properties?.Selected == true)
                return Result(ActionStatus.Applied);
            else if (Tree[index].AccessibilityHandler?.Invoke(request) != true)
            {
                if (request.Action is not (AccessibilityAction.Invoke or AccessibilityAction.Toggle or AccessibilityAction.Select or AccessibilityAction.Expand or AccessibilityAction.Collapse))
                    return Result(ActionStatus.InvalidValue);
                accessibleInvocations.Enqueue((id, accessibilitySource, request.Action, native));
                return Result(ActionStatus.Queued);
            }
            Changed = true;
            return Result(ActionStatus.Applied);
        }
        finally
        {
            accessibilitySource = previous;
        }
    }

    void BeginAccessibilityFrame()
    {
        accessibleActivation = 0;
        accessibilitySource = null;
        while (accessibleInvocations.TryDequeue(out var invocation))
        {
            var node = accessibility.FirstOrDefault(n => n.Id == invocation.Id && n.Enabled &&
                (invocation.Native || !n.Offscreen) && !n.Hidden && !n.Inert);
            if (node is null || !node.Supports(invocation.Action))
                continue;
            if (invocation.Action is AccessibilityAction.Expand or AccessibilityAction.Collapse &&
                node.Properties?.Expanded == (invocation.Action == AccessibilityAction.Expand) ||
                invocation.Action == AccessibilityAction.Select && node.Properties?.Selected == true)
                continue;
            accessibleActivation = invocation.Id;
            accessibilitySource = invocation.Source;
            break;
        }
    }

    void RevealNode(int index)
    {
        var rect = Tree[index].Rect;
        for (var parent = Tree[index].Parent; parent >= 0; parent = Tree[parent].Parent)
        {
            if (!Scrolls.TryGetValue(Tree[parent].Id, out var scroll))
                continue;
            var viewport = Tree[parent].Rect.Intersect(Tree[parent].ClipRect);
            float start = scroll.Horizontal ? rect.X : rect.Y;
            float end = scroll.Horizontal ? rect.Right : rect.Bottom;
            float low = scroll.Horizontal ? viewport.X : viewport.Y;
            float high = scroll.Horizontal ? viewport.Right : viewport.Bottom;
            float delta = start < low ? start - low : end > high ? end - high : 0;
            var offset = Math.Clamp(scroll.Offset + delta, 0, scroll.Max);
            delta = offset - scroll.Offset;
            scroll.Offset = offset;
            scroll.Follow = false;
            rect = new(rect.X - (scroll.Horizontal ? delta : 0), rect.Y - (scroll.Horizontal ? 0 : delta), rect.Width, rect.Height);
        }
    }
}
