using System.Runtime.InteropServices;

namespace Kumiko.Platform;

/// <summary>Window placement in virtual-screen pixels and per-monitor DPI changes.</summary>
internal sealed unsafe partial class WindowsWindow
{
    /// <summary>Outer frame thickness around the client area at the window's current DPI.</summary>
    Win32.NativeRectangle Frame()
    {
        var frame = new Win32.NativeRectangle();
        AdjustWindowRectExForDpi(ref frame, style, 0, extended, Win32.GetDpiForWindow(handle));
        return frame;
    }

    /// <summary>
    /// The normal (restored) frame position and client size. GetWindowPlacement reports workspace coordinates,
    /// which are offset from screen coordinates by any taskbar on the top or left edge.
    /// </summary>
    public WindowState? State
    {
        get
        {
            var placement = new WindowPlacement
            {
                Length = (uint)sizeof(WindowPlacement)
            };
            if (GetWindowPlacement(handle, ref placement) == 0)
                return null;
            var (dx, dy) = WorkspaceOffset(placement.Normal);
            var frame = Frame();
            var normal = placement.Normal;
            return new(normal.Left + dx, normal.Top + dy, normal.Right - normal.Left - (frame.Right - frame.Left),
                normal.Bottom - normal.Top - (frame.Bottom - frame.Top), placement.ShowCommand == 3);
        }
    }

    public void Restore(WindowState state)
    {
        var frame = Frame();
        var outer = new Win32.NativeRectangle
        {
            Left = (int)state.X,
            Top = (int)state.Y,
            Right = (int)(state.X + state.Width) + frame.Right - frame.Left,
            Bottom = (int)(state.Y + state.Height) + frame.Bottom - frame.Top
        };
        var (dx, dy) = WorkspaceOffset(outer);
        outer.Left -= dx;
        outer.Right -= dx;
        outer.Top -= dy;
        outer.Bottom -= dy;
        var placement = new WindowPlacement
        {
            Length = (uint)sizeof(WindowPlacement),
            ShowCommand = IsVisible ? state.Maximized ? 3u : 1u : 0u,
            Normal = outer
        };
        SetWindowPlacement(handle, ref placement);
        ResizeFromNative();
    }

    (int X, int Y) WorkspaceOffset(Win32.NativeRectangle rect)
    {
        var monitor = MonitorFromRect(ref rect, 2);
        var info = new MonitorInfo
        {
            Size = (uint)sizeof(MonitorInfo)
        };
        if (monitor == 0 || GetMonitorInfoW(monitor, ref info) == 0)
            return (0, 0);
        return (info.Work.Left - info.Monitor.Left, info.Work.Top - info.Monitor.Top);
    }

    /// <summary>WM_DPICHANGED: adopt the suggested rectangle so the window keeps its logical size on the new monitor.</summary>
    void DpiChanged(nint suggested)
    {
        var r = *(Win32.NativeRectangle*)suggested;
        Win32.SetWindowPos(handle, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, 0x0004 | 0x0010);
        ResizeFromNative();
    }

    /// <summary>WM_DROPFILES fallback when RegisterDragDrop fails: files arrive at the drop point without hover.</summary>
    void DropFiles(nint drop)
    {
        try
        {
            var count = DragQueryFileW(drop, uint.MaxValue, null, 0);
            var files = new string[count];
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFileW(drop, i, null, 0);
                var buffer = new char[length + 1];
                fixed (char* text = buffer)
                    DragQueryFileW(drop, i, text, length + 1);
                files[i] = new string(buffer, 0, (int)length);
            }
            DragQueryPoint(drop, out var point);
            float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
            window.Input(input =>
            {
                input.Pointer = new(point.X / scale, point.Y / scale);
                input.DroppedFiles = files;
            });
        }
        finally
        {
            DragFinish(drop);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DropPoint
    {
        public int X, Y;
    }

    [DllImport("shell32.dll")]
    static extern uint DragQueryFileW(nint drop, uint index, char* file, uint length);
    [DllImport("shell32.dll")]
    static extern int DragQueryPoint(nint drop, out DropPoint point);
    [DllImport("shell32.dll")]
    static extern void DragFinish(nint drop);
    [DllImport("shell32.dll")]
    static extern void DragAcceptFiles(nint window, int accept);

    [StructLayout(LayoutKind.Sequential)]
    struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCommand;
        public int MinX, MinY, MaxX, MaxY;
        public Win32.NativeRectangle Normal;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public uint Size;
        public Win32.NativeRectangle Monitor;
        public Win32.NativeRectangle Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    static extern int GetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll")]
    static extern int SetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll")]
    static extern nint MonitorFromRect(ref Win32.NativeRectangle rect, uint flags);
    [DllImport("user32.dll")]
    static extern int GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    static extern int AdjustWindowRectExForDpi(ref Win32.NativeRectangle rect, uint style, int menu, uint extended, uint dpi);
}
