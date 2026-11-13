using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Kumiko.Platform;

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct UiaValue
{
    [FieldOffset(0)]
    public ushort Type;
    [FieldOffset(8)]
    public nint Pointer;
    [FieldOffset(8)]
    public int Integer;
    [FieldOffset(8)]
    public short Boolean;
    public static UiaValue Text(string value) => new()
    {
        Type = 8,
        Pointer = Marshal.StringToBSTR(value)
    };
    public static UiaValue Number(int value) => new()
    {
        Type = 3,
        Integer = value
    };
    public static UiaValue Bool(bool value) => new()
    {
        Type = 11,
        Boolean = (short)(value ? -1 : 0)
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left, Top, Width, Height;
}

[GeneratedComInterface, Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IUiaSimple
{
    [PreserveSig]
    int GetOptions(out int options);
    [PreserveSig]
    int GetPattern(int pattern, out nint provider);
    [PreserveSig]
    int GetProperty(int property, out UiaValue value);
    [PreserveSig]
    int GetHost(out nint provider);
}

[GeneratedComInterface, Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IUiaFragment
{
    [PreserveSig]
    int Navigate(int direction, out nint provider);
    [PreserveSig]
    int RuntimeId(out nint array);
    [PreserveSig]
    int Bounds(out UiaRect rect);
    [PreserveSig]
    int EmbeddedRoots(out nint array);
    [PreserveSig]
    int SetFocus();
    [PreserveSig]
    int FragmentRoot(out nint provider);
}

[GeneratedComInterface, Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IUiaRoot
{
    [PreserveSig]
    int FromPoint(double x, double y, out nint provider);
    [PreserveSig]
    int GetFocus(out nint provider);
}

[GeneratedComInterface, Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IUiaInvoke
{
    [PreserveSig]
    int Invoke();
}

internal static partial class UiaNative
{
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaClientsAreListening();
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaRaiseStructureChangedEvent(nint provider, int change, nint runtimeId, int count);
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaRaiseAutomationEvent(nint provider, int eventId);
    [DllImport("UIAutomationCore.dll")]
    internal static extern nint UiaReturnRawElementProvider(nint hwnd, nuint w, nint l, nint provider);
    [DllImport("UIAutomationCore.dll")]
    internal static extern int UiaHostProviderFromHwnd(nint hwnd, out nint provider);
    [DllImport("oleaut32.dll")]
    internal static extern nint SafeArrayCreateVector(ushort type, int lower, uint count);
    [DllImport("oleaut32.dll")]
    internal static extern int SafeArrayPutElement(nint array, ref int index, ref int value);
    [DllImport("user32.dll")]
    internal static extern int ClientToScreen(nint hwnd, ref ScreenPoint point);
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScreenPoint
    {
        public int X, Y;
    }
}
