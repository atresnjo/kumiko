using System.Runtime.InteropServices;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacAccessibility
{
    static AccessibleNode? Node(nint self) => callbacks.TryGetValue(self, out var entry) ? entry.Owner.nodes.GetValueOrDefault(entry.Id) : null;
    static T Safe<T>(nint self, Func<T> action, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            if (callbacks.TryGetValue(self, out var entry))
                entry.Owner.ownerWindow.App.Report(ex);
            return fallback;
        }
    }
    static bool Act(nint self, AccessibilityRequest request) => Safe(self, () =>
    {
        if (!callbacks.TryGetValue(self, out var entry) || entry.Owner.disposed || entry.Owner.ownerWindow.Context?.PerformAccessibilityAction(entry.Id, request) != true)
            return false;
        entry.Owner.ownerWindow.Invalidate();
        return true;
    }, false);
    [UnmanagedCallersOnly]
    static nint Value(nint self, nint cmd) => Safe(self, () =>
    {
        var node = Node(self);
        if (node?.Properties?.Password == true)
            return 0;
        if (node?.Role is Role.Checkbox or Role.Switch)
            return O.O(O.Class("NSNumber"), "numberWithBool:", node.Checked ? (nint)1 : 0);
        if (node?.Properties?.Number is { } number)
            return O.O(O.Class("NSNumber"), "numberWithDouble:", number);
        return node?.Value is { } value ? O.TempString(value) : 0;
    }, (nint)0);
    [UnmanagedCallersOnly]
    static void SetValue(nint self, nint cmd, nint value)
    {
        Safe(self, () =>
        {
            if (Node(self) is not { Enabled: true } node || node.Properties?.ReadOnly == true)
                return false;
            if (node.Supports(AccessibilityAction.SetRange))
                return Act(self, new(AccessibilityAction.SetRange, Number: O.D(value, "doubleValue")));
            if (node.Role is Role.Checkbox or Role.Switch)
                return node.Checked == (O.N(value, "boolValue") != 0) || Act(self, new(AccessibilityAction.Toggle));
            return node.Role == Role.TextField && Act(self, new(AccessibilityAction.SetValue, Text: O.Text(value)));
        }, false);
    }
    [UnmanagedCallersOnly] static nint Minimum(nint self, nint cmd) => Safe(self, () => O.O(O.Class("NSNumber"), "numberWithDouble:", Node(self)?.Properties?.Minimum ?? 0), (nint)0);
    [UnmanagedCallersOnly] static nint Maximum(nint self, nint cmd) => Safe(self, () => O.O(O.Class("NSNumber"), "numberWithDouble:", Node(self)?.Properties?.Maximum ?? 0), (nint)0);
    static byte Step(nint self, int direction)
    {
        var p = Node(self)?.Properties;
        return p?.Number is { } number && Act(self, new(AccessibilityAction.SetRange,
            Number: Math.Clamp(number + direction * p.Step, p.Minimum ?? double.MinValue, p.Maximum ?? double.MaxValue))) ? (byte)1 : (byte)0;
    }
    [UnmanagedCallersOnly] static byte Increment(nint self, nint cmd) => Safe(self, () => Step(self, 1), (byte)0);
    [UnmanagedCallersOnly] static byte Decrement(nint self, nint cmd) => Safe(self, () => Step(self, -1), (byte)0);
    [UnmanagedCallersOnly]
    static nint SelectedText(nint self, nint cmd) => Safe(self, () =>
    {
        var node = Node(self);
        if (node?.Properties is not { Password: false } p)
            return 0;
        var text = node.Value ?? "";
        int from = Math.Clamp(p.SelectionStart, 0, text.Length), to = Math.Clamp(p.SelectionEnd, from, text.Length);
        return O.TempString(text[from..to]);
    }, (nint)0);
    [UnmanagedCallersOnly] static nint CharacterCount(nint self, nint cmd) => Node(self)?.Properties?.Password == true ? 0 : Node(self)?.Value?.Length ?? 0;
    [UnmanagedCallersOnly]
    static NativeRange TextSelection(nint self, nint cmd)
    {
        var p = Node(self)?.Properties;
        return p is { Password: false } ? new((nuint)p.SelectionStart, (nuint)Math.Max(0, p.SelectionEnd - p.SelectionStart)) : default;
    }
    [UnmanagedCallersOnly]
    static void SetTextSelection(nint self, nint cmd, NativeRange range)
    {
        if (range.Location <= int.MaxValue && range.Length <= int.MaxValue && range.Location + range.Length <= int.MaxValue)
            Act(self, new(AccessibilityAction.SetSelection, Start: (int)range.Location, End: (int)(range.Location + range.Length)));
    }
    [UnmanagedCallersOnly]
    static nint StringForRange(nint self, nint cmd, NativeRange range) => Safe(self, () =>
    {
        var node = Node(self);
        var text = node?.Properties?.Password == true ? "" : node?.Value ?? "";
        if (range.Location > (nuint)text.Length || range.Length > (nuint)text.Length - range.Location)
            return 0;
        return O.TempString(text.Substring((int)range.Location, (int)range.Length));
    }, (nint)0);
    [UnmanagedCallersOnly] static byte Selected(nint self, nint cmd) => Node(self)?.Properties?.Selected == true ? (byte)1 : (byte)0;
    [UnmanagedCallersOnly]
    static void SetSelected(nint self, nint cmd, byte selected)
    {
        if (selected != 0)
            Act(self, new(AccessibilityAction.Select));
    }
    [UnmanagedCallersOnly] static byte Expanded(nint self, nint cmd) => Node(self)?.Properties?.Expanded == true ? (byte)1 : (byte)0;
    [UnmanagedCallersOnly] static void SetExpanded(nint self, nint cmd, byte expanded) => Act(self, new(expanded != 0 ? AccessibilityAction.Expand : AccessibilityAction.Collapse));
    [UnmanagedCallersOnly]
    static byte SelectorAllowed(nint self, nint cmd, nint selector)
    {
        var node = Node(self);
        if (node is null)
            return 0;
        AccessibilityAction? action = selector == O.Sel("accessibilityPerformPress") ? AccessibilityAction.Invoke :
            selector == O.Sel("setAccessibilityFocused:") ? AccessibilityAction.Focus :
            selector == O.Sel("setAccessibilitySelected:") ? AccessibilityAction.Select :
            selector == O.Sel("setAccessibilityExpanded:") ? AccessibilityAction.Expand :
            selector == O.Sel("accessibilityPerformIncrement") || selector == O.Sel("accessibilityPerformDecrement") ? AccessibilityAction.SetRange :
            selector == O.Sel("setAccessibilityValue:") ? node.Role == Role.TextField ? AccessibilityAction.SetValue :
                node.Role is Role.Checkbox or Role.Switch ? AccessibilityAction.Toggle : AccessibilityAction.SetRange :
            selector == O.Sel("setAccessibilitySelectedTextRange:") ? AccessibilityAction.SetSelection : null;
        if (action is { } requested && (!node.Enabled || node.Offscreen || !node.Supports(requested)))
            return 0;
        if (selector == O.Sel("setAccessibilityValue:") && (!node.Enabled || node.Properties?.ReadOnly == true))
            return 0;
        if ((selector == O.Sel("accessibilitySelectedText") || selector == O.Sel("accessibilityStringForRange:") || selector == O.Sel("setAccessibilitySelectedTextRange:")) && node.Properties?.Password == true)
            return 0;
        return 1;
    }
}
