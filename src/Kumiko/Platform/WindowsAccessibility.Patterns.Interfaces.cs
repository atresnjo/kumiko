using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Kumiko.Platform;

[GeneratedComInterface, Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IUiaValue
{
    [PreserveSig] int SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int GetValue(out nint value);
    [PreserveSig] int GetReadOnly(out int readOnly);
}
[GeneratedComInterface, Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
internal partial interface IUiaRange
{
    [PreserveSig] int SetRangeValue(double value);
    [PreserveSig] int GetRangeValue(out double value);
    [PreserveSig] int GetRangeReadOnly(out int readOnly);
    [PreserveSig] int GetMaximum(out double value);
    [PreserveSig] int GetMinimum(out double value);
    [PreserveSig] int GetLargeChange(out double value);
    [PreserveSig] int GetSmallChange(out double value);
}
[GeneratedComInterface, Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IUiaToggle
{
    [PreserveSig] int Toggle();
    [PreserveSig] int GetToggleState(out int state);
}
[GeneratedComInterface, Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
internal partial interface IUiaExpand
{
    [PreserveSig] int Expand();
    [PreserveSig] int Collapse();
    [PreserveSig] int GetExpandState(out int state);
}
[GeneratedComInterface, Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface IUiaSelectionItem
{
    [PreserveSig] int Select();
    [PreserveSig] int AddToSelection();
    [PreserveSig] int RemoveFromSelection();
    [PreserveSig] int GetSelected(out int selected);
    [PreserveSig] int GetSelectionContainer(out nint container);
}
[GeneratedComInterface, Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface IUiaText
{
    [PreserveSig] int GetTextSelection(out nint ranges);
    [PreserveSig] int GetVisibleRanges(out nint ranges);
    [PreserveSig] int RangeFromChild(nint child, out nint range);
    [PreserveSig] int RangeFromPoint(UiaPoint point, out nint range);
    [PreserveSig] int GetDocumentRange(out nint range);
    [PreserveSig] int GetSupportedSelection(out int selection);
}
[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X, Y;
}
[GeneratedComInterface, Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface IUiaTextRange
{
    [PreserveSig] int Clone(out nint range);
    [PreserveSig] int Compare(nint range, out int same);
    [PreserveSig] int CompareEndpoints(int endpoint, nint range, int targetEndpoint, out int comparison);
    [PreserveSig] int ExpandToEnclosingUnit(int unit);
    [PreserveSig] int FindAttribute(int attribute, UiaValue value, int backward, out nint range);
    [PreserveSig] int FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out nint range);
    [PreserveSig] int GetAttributeValue(int attribute, out UiaValue value);
    [PreserveSig] int GetBoundingRectangles(out nint array);
    [PreserveSig] int GetEnclosingElement(out nint element);
    [PreserveSig] int GetText(int maximum, out nint text);
    [PreserveSig] int Move(int unit, int count, out int moved);
    [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
    [PreserveSig] int MoveEndpointByRange(int endpoint, nint range, int targetEndpoint);
    [PreserveSig] int Select();
    [PreserveSig] int AddToSelection();
    [PreserveSig] int RemoveFromSelection();
    [PreserveSig] int ScrollIntoView(int alignTop);
    [PreserveSig] int GetChildren(out nint array);
}

internal static partial class UiaNative
{
    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayPutElement")]
    internal static extern int PutPointer(nint array, ref int index, nint value);
    [DllImport("oleaut32.dll", EntryPoint = "SafeArrayPutElement")]
    internal static extern int PutDouble(nint array, ref int index, ref double value);
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaGetReservedNotSupportedValue(out nint value);
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaGetReservedMixedAttributeValue(out nint value);
}
