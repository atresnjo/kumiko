using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed partial class UiaElement : IUiaValue, IUiaRange, IUiaToggle, IUiaExpand, IUiaSelectionItem, IUiaText
{
    internal WindowsAccessibility Owner => owner;
    internal ulong Id => id;
    internal AccessibleNode? Snapshot => Node;
    internal bool IsAlive => Alive;
    internal UiaTextGeometry? TextGeometry => owner.Current.Text.GetValueOrDefault(id);
    const int Invalid = unchecked((int)0x80070057), NotSupported = unchecked((int)0x80040204);

    internal int Action(AccessibilityRequest request)
    {
        if (!Alive || Node is not { Enabled: true, Hidden: false } node || node.Offscreen && request.Action != AccessibilityAction.Reveal)
            return Unavailable;
        if (!node.Supports(request.Action))
            return NotSupported;
        try
        {
            owner.Window.App.Dispatch(() =>
            {
                if (!owner.Disposed && owner.Window.Context?.PerformAccessibilityAction(id, request) == true)
                    owner.Window.Invalidate();
            });
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return Unavailable;
        }
    }
    public int SetValue(string value) => Node?.Properties?.ReadOnly == true ? NotSupported : Action(new(AccessibilityAction.SetValue, Text: value));
    public int GetValue(out nint value)
    {
        value = 0;
        if (Node?.Properties?.Password == true)
            return unchecked((int)0x80070005);
        if (!Alive)
            return Unavailable;
        value = Marshal.StringToBSTR(Node?.Value ?? "");
        return 0;
    }
    public int GetReadOnly(out int readOnly)
    {
        readOnly = Node?.Properties?.ReadOnly == true ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int SetRangeValue(double value)
    {
        var p = Node?.Properties;
        if (!double.IsFinite(value) || value < p?.Minimum || value > p?.Maximum)
            return Invalid;
        return Action(new(AccessibilityAction.SetRange, Number: value));
    }
    public int GetRangeValue(out double value)
    {
        value = Node?.Properties?.Number ?? 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetRangeReadOnly(out int readOnly) => GetReadOnly(out readOnly);
    public int GetMaximum(out double value)
    {
        value = Node?.Properties?.Maximum ?? 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetMinimum(out double value)
    {
        value = Node?.Properties?.Minimum ?? 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetLargeChange(out double value)
    {
        value = (Node?.Properties?.Step ?? 1) * 10;
        return Alive ? 0 : Unavailable;
    }
    public int GetSmallChange(out double value)
    {
        value = Node?.Properties?.Step ?? 1;
        return Alive ? 0 : Unavailable;
    }
    public int Toggle() => Action(new(AccessibilityAction.Toggle));
    public int GetToggleState(out int state)
    {
        state = Node?.Checked == true ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int Expand() => Action(new(AccessibilityAction.Expand));
    public int Collapse() => Action(new(AccessibilityAction.Collapse));
    public int GetExpandState(out int state)
    {
        state = Node?.Properties?.Expanded == true ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int Select() => Action(new(AccessibilityAction.Select));
    public int AddToSelection() => Select();
    public int RemoveFromSelection() => NotSupported;
    public int GetSelected(out int selected)
    {
        selected = Node?.Properties?.Selected == true ? 1 : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetSelectionContainer(out nint container)
    {
        container = 0;
        var node = Node;
        while (node is not null && node.Role is not (Role.Grid or Role.List or Role.ComboBox or Role.Menu))
            node = owner.Current.Nodes.FirstOrDefault(n => n.Id == node.Parent);
        if (node is not null)
            container = Simple(owner.Element(node.Id));
        return Alive ? 0 : Unavailable;
    }
    internal unsafe nint TextRange(int start, int end) => (nint)ComInterfaceMarshaller<IUiaTextRange>.ConvertToUnmanaged(new UiaTextRange(this, start, end));
    internal static nint PointerArray(nint pointer)
    {
        var array = UiaNative.SafeArrayCreateVector(13, 0, 1);
        if (array == 0)
        {
            Marshal.Release(pointer);
            throw new OutOfMemoryException();
        }
        int index = 0;
        try
        {
            UiaNative.PutPointer(array, ref index, pointer);
        }
        finally { Marshal.Release(pointer); }
        return array;
    }
    public int GetTextSelection(out nint ranges)
    {
        var p = Node?.Properties;
        ranges = Alive ? PointerArray(TextRange(p?.SelectionStart ?? 0, p?.SelectionEnd ?? 0)) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetVisibleRanges(out nint ranges)
    {
        ranges = Alive ? PointerArray(TextRange(0, Node?.Value?.Length ?? 0)) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int RangeFromChild(nint child, out nint range)
    {
        range = 0;
        return Invalid;
    }
    public int RangeFromPoint(UiaPoint point, out nint range)
    {
        var snapshot = owner.Current;
        var local = new Point((float)(point.X - snapshot.X) / snapshot.Scale, (float)(point.Y - snapshot.Y) / snapshot.Scale);
        int position = TextGeometry?.HitTest(local) ?? 0;
        range = Alive ? TextRange(position, position) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetDocumentRange(out nint range)
    {
        range = Alive ? TextRange(0, Node?.Value?.Length ?? 0) : 0;
        return Alive ? 0 : Unavailable;
    }
    public int GetSupportedSelection(out int selection)
    {
        selection = 1;
        return Alive ? 0 : Unavailable;
    }
}
