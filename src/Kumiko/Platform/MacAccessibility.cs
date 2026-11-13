using System.Runtime.InteropServices;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;
// NSAccessibilityElement owns no managed context. The map is removed before native
// elements are released; stale assistive-client references safely return defaults.
internal sealed unsafe partial class MacAccessibility(Window window, nint view) : IDisposable
{
    readonly Window ownerWindow = window;
    nint OwnerView => view;

    static readonly Dictionary<nint, (MacAccessibility Owner, ulong Id)> callbacks = new();
    readonly Dictionary<ulong, nint> elements = new();
    Dictionary<ulong, AccessibleNode> nodes = new();
    bool disposed;
    static nint Define() => O.Define(
        "NanoCSharpAccessibleElement",
        "NSAccessibilityElement",
        ("accessibilityPerformPress", (nint)(delegate* unmanaged<nint, nint, byte>)&Press, "B@:"),
        ("accessibilityFrame", (nint)(delegate* unmanaged<nint, nint, NativeRect>)&Frame, "{CGRect={CGPoint=dd}{CGSize=dd}}@:"),
        ("accessibilityValue", (nint)(delegate* unmanaged<nint, nint, nint>)&Value, "@@:"),
        ("setAccessibilityValue:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&SetValue, "v@:@"),
        ("accessibilityMinValue", (nint)(delegate* unmanaged<nint, nint, nint>)&Minimum, "@@:"),
        ("accessibilityMaxValue", (nint)(delegate* unmanaged<nint, nint, nint>)&Maximum, "@@:"),
        ("accessibilityPerformIncrement", (nint)(delegate* unmanaged<nint, nint, byte>)&Increment, "B@:"),
        ("accessibilityPerformDecrement", (nint)(delegate* unmanaged<nint, nint, byte>)&Decrement, "B@:"),
        ("accessibilitySelectedText", (nint)(delegate* unmanaged<nint, nint, nint>)&SelectedText, "@@:"),
        ("accessibilityNumberOfCharacters", (nint)(delegate* unmanaged<nint, nint, nint>)&CharacterCount, "q@:"),
        ("accessibilitySelectedTextRange", (nint)(delegate* unmanaged<nint, nint, NativeRange>)&TextSelection, "{_NSRange=QQ}@:"),
        ("setAccessibilitySelectedTextRange:", (nint)(delegate* unmanaged<nint, nint, NativeRange, void>)&SetTextSelection, "v@:{_NSRange=QQ}"),
        ("accessibilityAttributedStringForRange:", (nint)(delegate* unmanaged<nint, nint, NativeRange, nint>)&AttributedString, "@@:{_NSRange=QQ}"),
        ("accessibilityStringForRange:", (nint)(delegate* unmanaged<nint, nint, NativeRange, nint>)&StringForRange, "@@:{_NSRange=QQ}"),
        ("isAccessibilitySelected", (nint)(delegate* unmanaged<nint, nint, byte>)&Selected, "B@:"),
        ("setAccessibilitySelected:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&SetSelected, "v@:B"),
        ("isAccessibilityExpanded", (nint)(delegate* unmanaged<nint, nint, byte>)&Expanded, "B@:"),
        ("setAccessibilityExpanded:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&SetExpanded, "v@:B"),
        ("isAccessibilitySelectorAllowed:", (nint)(delegate* unmanaged<nint, nint, nint, byte>)&SelectorAllowed, "B@::"),
        ("isAccessibilityEnabled", (nint)(delegate* unmanaged<nint, nint, byte>)&Enabled, "B@:"),
        ("isAccessibilityFocused", (nint)(delegate* unmanaged<nint, nint, byte>)&Focused, "B@:"),
        ("setAccessibilityFocused:", (nint)(delegate* unmanaged<nint, nint, byte, void>)&SetFocused, "v@:B")
    );
    /// <summary>
    /// Mirrors the semantic tree into NSAccessibilityElements. Values, frames, focus and enabled state are read
    /// through callbacks, so a frame pushes only what changed: properties of changed records, parents that moved
    /// and children lists that differ. Scrolling a long list no longer re-sends every element every frame.
    /// </summary>
    public void Update(Context context)
    {
        if (disposed)
            return;
        using var pool = new AutoreleasePool();
        var previous = nodes;
        (nodes, spareNodes) = (spareNodes, nodes);
        nodes.Clear();
        foreach (var node in context.Accessibility)
            nodes.TryAdd(node.Id, node);
        foreach (var id in elements.Keys)
        {
            if (nodes.ContainsKey(id))
                continue;
            callbacks.Remove(elements[id]);
            O.Release(elements[id]);
            elements.Remove(id);
            children.Remove(id);
        }
        created.Clear();
        foreach (var node in nodes.Values)
        {
            if (!elements.TryGetValue(node.Id, out var element))
            {
                element = O.O(Define(), "new");
                elements.Add(node.Id, element);
                callbacks[element] = (this, node.Id);
                created.Add(node.Id);
                O.V(element, "setAccessibilityElement:", (byte)1);
                O.V(element, "setAccessibilityIdentifier:", O.TempString(node.Id.ToString("x16")));
            }
            else if (previous.TryGetValue(node.Id, out var old) && (ReferenceEquals(old, node) ||
                old.Role == node.Role && old.Name == node.Name && Equals(old.Properties, node.Properties)))
                continue;
            O.V(element, "setAccessibilityRole:", O.TempString(node.Role switch
            {
                Role.Button => "AXButton",
                Role.Checkbox => "AXCheckBox",
                Role.Switch => "AXCheckBox",
                Role.Slider => "AXSlider",
                Role.TextField => "AXTextField",
                Role.Text or Role.Heading => "AXStaticText",
                Role.Image => "AXImage",
                Role.List => "AXList",
                Role.ListItem => "AXRow",
                Role.Grid => "AXTable",
                Role.Row => "AXRow",
                Role.Cell => "AXCell",
                Role.ColumnHeader => "AXColumn",
                Role.ComboBox => "AXComboBox",
                Role.Option => "AXRow",
                Role.Menu => "AXMenu",
                Role.MenuItem => "AXMenuItem",
                Role.Tree => "AXOutline",
                Role.TreeItem => "AXRow",
                Role.Tooltip => "AXHelpTag",
                Role.Separator => "AXSplitter",
                Role.ScrollArea => "AXScrollArea",
                Role.Dialog => "AXGroup",
                _ => "AXGroup"
            }));
            O.V(element, "setAccessibilityLabel:", O.TempString(node.Name));
            O.V(element, "setAccessibilityIdentifier:", O.TempString(node.Id.ToString("x16")));
            O.V(element, "setAccessibilitySubrole:", node.Properties?.Password == true ? O.TempString("AXSecureTextField") : node.Role == Role.Dialog ? O.TempString("AXDialog") : 0);
            O.V(element, "setAccessibilityHelp:", node.Properties?.Help is { } help ? O.TempString(help) : 0);
            if (node.Role == Role.TreeItem)
            {
                O.V(element, "setAccessibilityDisclosed:", node.Properties?.Expanded == true ? (byte)1 : (byte)0);
                O.V(element, "setAccessibilityDisclosureLevel:", (nint)Math.Max(0, (node.Properties?.Level ?? 1) - 1));
            }

        }

        foreach (var list in pending.Values)
            list.Clear();
        foreach (var node in nodes.Values)
        {
            var parent = node.Parent;
            if (created.Contains(node.Id) || created.Contains(parent) || !previous.TryGetValue(node.Id, out var old) || old.Parent != parent)
                O.V(elements[node.Id], "setAccessibilityParent:", elements.GetValueOrDefault(parent, view));
            if (node.Hidden)
                continue;
            if (!pending.TryGetValue(parent, out var list))
                pending[parent] = list = [];
            list.Add(node.Id);
        }
        Publish(0, view);
        foreach (var id in nodes.Keys)
            Publish(id, elements[id]);

        if (!previous.Keys.SequenceEqual(nodes.Keys))
            Post(view, "AXLayoutChanged");
        foreach (var node in nodes.Values)
        {
            previous.TryGetValue(node.Id, out var old);
            if (node.Focused && old?.Focused != true)
                Post(elements[node.Id], "AXFocusedUIElementChanged");
            if (old is not null && (old.Value != node.Value || old.Checked != node.Checked))
                Post(elements[node.Id], "AXValueChanged");
        }
    }

    Dictionary<ulong, AccessibleNode> spareNodes = new();
    readonly HashSet<ulong> created = new();
    /// <summary>Visible children per parent id: being built this frame, and as last sent to AppKit.</summary>
    readonly Dictionary<ulong, List<ulong>> pending = new(), children = new();

    /// <summary>Sends a parent's children when they differ from what AppKit already holds.</summary>
    void Publish(ulong id, nint element)
    {
        var next = pending.GetValueOrDefault(id);
        var sent = children.GetValueOrDefault(id);
        if (sent is not null && !created.Contains(id) && CollectionsMarshal.AsSpan(sent).SequenceEqual(CollectionsMarshal.AsSpan(next)))
            return;
        var array = O.O(O.Class("NSMutableArray"), "array");
        foreach (var child in next ?? [])
            O.V(array, "addObject:", elements[child]);
        if (id == 0)
            O.V(element, "setAccessibilityElement:", (byte)0);
        O.V(element, "setAccessibilityChildren:", array);
        if (sent is null)
            children[id] = sent = [];
        sent.Clear();
        if (next is not null)
            sent.AddRange(next);
    }

    // Resolve desktop-only exports lazily: untrimmed iOS hot reload builds must not link AppKit.
    static readonly delegate* unmanaged<nint, nint, void> notify = (delegate* unmanaged<nint, nint, void>)
        NativeLibrary.GetExport(NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"), "NSAccessibilityPostNotification");
    static void Notify(nint element, nint notification) => notify(element, notification);
    static void Post(nint element, string notification) => Notify(element, O.TempString(notification));
    [UnmanagedCallersOnly]
    static NativeRect Frame(nint self, nint cmd)
    {
        if (!callbacks.TryGetValue(self, out var entry) || !entry.Owner.nodes.TryGetValue(entry.Id, out var node))
            return default;
        try
        {
            var r = node.Rect;
            var local = O.R(entry.Owner.OwnerView, "convertRect:toView:", new NativeRect(r.X, r.Y, r.Width, r.Height), 0);
            return O.R(O.O(entry.Owner.OwnerView, "window"), "convertRectToScreen:", local);
        }
        catch (Exception ex)
        {
            entry.Owner.ownerWindow.App.Report(ex);
            return default;
        }
    }

    [UnmanagedCallersOnly]
    static byte Enabled(nint self, nint cmd) => callbacks.TryGetValue(self, out var entry) && entry.Owner.nodes.TryGetValue(entry.Id, out var node) && node.Enabled ? (byte)1 : (byte)0;
    [UnmanagedCallersOnly]
    static byte Focused(nint self, nint cmd) => callbacks.TryGetValue(self, out var entry) && entry.Owner.nodes.TryGetValue(entry.Id, out var node) && node.Focused ? (byte)1 : (byte)0;
    [UnmanagedCallersOnly]
    static void SetFocused(nint self, nint cmd, byte focused)
    {
        if (focused == 0 || !callbacks.TryGetValue(self, out var entry))
            return;
        try
        {
            Act(self, new(AccessibilityAction.Focus));
        }
        catch (Exception ex)
        {
            entry.Owner.ownerWindow.App.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static byte Press(nint self, nint cmd)
    {
        if (!callbacks.TryGetValue(self, out var entry))
            return 0;
        try
        {
            if (entry.Owner.ownerWindow.Context?.PerformAccessibilityAction(entry.Id, new(AccessibilityAction.Invoke)) != true)
                return 0;
            // Defer rebuilding so elements remain alive until this callback returns.
            entry.Owner.ownerWindow.Invalidate();
            return 1;
        }
        catch (Exception ex)
        {
            entry.Owner.ownerWindow.App.Report(ex);
            return 0;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        O.V(view, "setAccessibilityChildren:", (nint)0);
        foreach (var element in elements.Values)
        {
            callbacks.Remove(element);
            O.Release(element);
        }

        elements.Clear();
        nodes.Clear();
    }
}
