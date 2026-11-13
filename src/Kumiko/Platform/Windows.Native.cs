using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal static unsafe class Win32
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Style;
        public delegate* unmanaged<nint, uint, nuint, nint, nint> Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PaintStruct
    {
        public nint DC;
        public int Erase;
        public NativeRectangle Rect;
        public int Restore, Update;
        public fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int Xppm, Yppm;
        public uint Used, Important;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassW(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(
        uint ex,
        string cls,
        string title,
        uint style,
        int x,
        int y,
        int w,
        int h,
        nint parent,
        nint menu,
        nint instance,
        nint data
    );
    [DllImport("user32.dll")]
    internal static extern nint DefWindowProcW(nint h, uint message, nuint w, nint l);
    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out Message message, nint h, uint min, uint max);
    [DllImport("user32.dll")]
    internal static extern int PeekMessageW(out Message message, nint h, uint min, uint max, uint remove);
    [DllImport("user32.dll")]
    internal static extern int TranslateMessage(ref Message message);
    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")]
    internal static extern int PostThreadMessageW(uint thread, uint message, nuint w, nint l);
    [DllImport("user32.dll")]
    internal static extern int PostMessageW(nint h, uint message, nuint w, nint l);
    [DllImport("user32.dll")]
    internal static extern uint MsgWaitForMultipleObjects(uint count, nint handles, int waitAll, uint ms, uint mask);
    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")]
    internal static extern int DestroyWindow(nint h);
    [DllImport("user32.dll")]
    internal static extern int ShowWindow(nint h, int command);
    [DllImport("user32.dll")]
    internal static extern int IsWindowVisible(nint h);
    [DllImport("user32.dll")]
    internal static extern int IsZoomed(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTextW(nint h, string text);
    [DllImport("user32.dll")]
    internal static extern int SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")]
    internal static extern int GetClientRect(nint h, out NativeRectangle rect);
    [DllImport("user32.dll")]
    internal static extern int AdjustWindowRectEx(ref NativeRectangle r, uint style, int menu, uint ex);
    [DllImport("user32.dll")]
    internal static extern int InvalidateRect(nint h, nint rect, int erase);
    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint h, out PaintStruct paint);
    [DllImport("user32.dll")]
    internal static extern int EndPaint(nint h, ref PaintStruct paint);
    [DllImport("gdi32.dll")]
    internal static extern int StretchDIBits(
        nint dc,
        int x,
        int y,
        int w,
        int h,
        int sx,
        int sy,
        int sw,
        int sh,
        void* bits,
        ref BitmapInfo info,
        uint usage,
        uint op
    );
    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    internal static extern int GetCursorPos(out UiaNative.ScreenPoint point);
    [DllImport("user32.dll")]
    internal static extern nint SetCapture(nint h);
    [DllImport("user32.dll")]
    internal static extern int ReleaseCapture();
    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")]
    internal static extern int SetProcessDpiAwarenessContext(nint value);
}
