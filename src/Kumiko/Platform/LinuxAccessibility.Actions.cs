using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class LinuxAccessibility
{
    static void Slot(nint iface, int index, nint callback) => ((nint*)iface)[index + 2] = callback;
    [UnmanagedCallersOnly]
    static void ComponentInit(nint iface, nint data)
    {
        Slot(iface, 3, (nint)(delegate* unmanaged<nint, int*, int*, int*, int*, int, void>)&Extents);
        Slot(iface, 6, (nint)(delegate* unmanaged<nint, int>)&GrabFocus);
    }
    [UnmanagedCallersOnly]
    static void Extents(nint self, int* x, int* y, int* width, int* height, int coordinates)
    {
        var node = Node(self);
        int originX = 0, originY = 0;
        if (coordinates == 0 && callbacks.TryGetValue(self, out var entry))
        {
            var native = Atk.gtk_widget_get_window(entry.Owner.view);
            if (native != 0)
                Atk.gdk_window_get_origin(native, out originX, out originY);
        }
        if (x != null)
            *x = originX + (int)(node?.Rect.X ?? 0);
        if (y != null)
            *y = originY + (int)(node?.Rect.Y ?? 0);
        if (width != null)
            *width = (int)(node?.Rect.Width ?? 0);
        if (height != null)
            *height = (int)(node?.Rect.Height ?? 0);
    }
    [UnmanagedCallersOnly]
    static int GrabFocus(nint self) => Act(self, new(AccessibilityAction.Focus)) ? 1 : 0;
    [UnmanagedCallersOnly]
    static void ActionInit(nint iface, nint data)
    {
        Slot(iface, 0, (nint)(delegate* unmanaged<nint, int, int>)&DoAction);
        Slot(iface, 1, (nint)(delegate* unmanaged<nint, int>)&ActionCount);
        Slot(iface, 3, (nint)(delegate* unmanaged<nint, int, nint>)&ActionName);
    }
    static readonly (AccessibilityAction Action, nint Name)[] actionNames =
    [
        (AccessibilityAction.Invoke, Marshal.StringToCoTaskMemUTF8("activate")),
        (AccessibilityAction.Toggle, Marshal.StringToCoTaskMemUTF8("toggle")),
        (AccessibilityAction.Select, Marshal.StringToCoTaskMemUTF8("select")),
        (AccessibilityAction.Expand, Marshal.StringToCoTaskMemUTF8("expand")),
        (AccessibilityAction.Collapse, Marshal.StringToCoTaskMemUTF8("collapse")),
        (AccessibilityAction.Reveal, Marshal.StringToCoTaskMemUTF8("show"))
    ];
    static (AccessibilityAction Action, nint Name)[] Actions(nint self) =>
        actionNames.Where(a => Node(self)?.Supports(a.Action) == true).ToArray();
    [UnmanagedCallersOnly]
    static nint ActionName(nint self, int index) => Actions(self) is var actions && index >= 0 && index < actions.Length ? actions[index].Name : 0;
    [UnmanagedCallersOnly]
    static int ActionCount(nint self) => Actions(self).Length;
    [UnmanagedCallersOnly]
    static int DoAction(nint self, int index)
    {
        var actions = Actions(self);
        return index >= 0 && index < actions.Length && Act(self, new(actions[index].Action)) ? 1 : 0;
    }
    [UnmanagedCallersOnly]
    static void ValueInit(nint iface, nint data)
    {
        Slot(iface, 0, (nint)(delegate* unmanaged<nint, nint, void>)&CurrentValue);
        Slot(iface, 1, (nint)(delegate* unmanaged<nint, nint, void>)&MaximumValue);
        Slot(iface, 2, (nint)(delegate* unmanaged<nint, nint, void>)&MinimumValue);
        Slot(iface, 3, (nint)(delegate* unmanaged<nint, nint, int>)&SetCurrentValue);
        Slot(iface, 4, (nint)(delegate* unmanaged<nint, nint, void>)&MinimumIncrement);
        Slot(iface, 5, (nint)(delegate* unmanaged<nint, double*, nint*, void>)&ValueAndText);
        Slot(iface, 6, (nint)(delegate* unmanaged<nint, nint>)&Range);
        Slot(iface, 7, (nint)(delegate* unmanaged<nint, double>)&Increment);
        Slot(iface, 9, (nint)(delegate* unmanaged<nint, double, void>)&SetValue);
    }
    [DllImport(Atk.GObject)] static extern nint g_value_init(nint value, nuint type);
    [DllImport(Atk.GObject)] static extern void g_value_set_double(nint value, double number);
    [DllImport(Atk.GObject)] static extern double g_value_get_double(nint value);
    static void DoubleValue(nint value, double number)
    {
        if (value == 0)
            return;
        g_value_init(value, 60);
        g_value_set_double(value, number);
    }
    [UnmanagedCallersOnly] static void CurrentValue(nint self, nint value) => DoubleValue(value, Node(self)?.Properties?.Number ?? 0);
    [UnmanagedCallersOnly] static void MaximumValue(nint self, nint value) => DoubleValue(value, Node(self)?.Properties?.Maximum ?? 0);
    [UnmanagedCallersOnly] static void MinimumValue(nint self, nint value) => DoubleValue(value, Node(self)?.Properties?.Minimum ?? 0);
    [UnmanagedCallersOnly] static void MinimumIncrement(nint self, nint value) => DoubleValue(value, Node(self)?.Properties?.Step ?? 1);
    [UnmanagedCallersOnly] static int SetCurrentValue(nint self, nint value) => Act(self, new(AccessibilityAction.SetRange, Number: g_value_get_double(value))) ? 1 : 0;
    [UnmanagedCallersOnly]
    static void ValueAndText(nint self, double* value, nint* text)
    {
        if (value != null)
            *value = Node(self)?.Properties?.Number ?? 0;
        if (text != null)
            *text = Atk.g_strdup(Node(self)?.Value ?? "");
    }
    [UnmanagedCallersOnly] static nint Range(nint self) => Atk.atk_range_new(Node(self)?.Properties?.Minimum ?? 0, Node(self)?.Properties?.Maximum ?? 0, "");
    [UnmanagedCallersOnly] static double Increment(nint self) => Node(self)?.Properties?.Step ?? 1;
    [UnmanagedCallersOnly] static void SetValue(nint self, double value) => Act(self, new(AccessibilityAction.SetRange, Number: value));
}
