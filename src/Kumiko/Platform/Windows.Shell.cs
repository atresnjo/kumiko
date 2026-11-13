using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class WindowsWindow
{
    bool fullScreen;
    Rect restoreBounds;
    uint restoreStyle;
    public nint NativeHandle => handle;
    public bool IsFocused => GetForegroundWindow() == handle;
    public bool IsMaximized => Win32.IsZoomed(handle) != 0;
    public bool IsMinimized => IsIconic(handle) != 0;
    public bool IsFullScreen => fullScreen;

    public Rect Bounds
    {
        get
        {
            if (GetWindowRect(handle, out var r) == 0)
                throw new InvalidOperationException("GetWindowRect failed.");
            return new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    public Size ContentSize
    {
        get
        {
            Win32.GetClientRect(handle, out var r);
            float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
            return new(r.Right / scale, r.Bottom / scale);
        }
    }

    public void SetBounds(Rect r) => Win32.SetWindowPos(handle, 0, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, 0x14);
    public void Configure(WindowOptions o)
    {
        if (o.Material != WindowMaterial.None && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            throw new PlatformNotSupportedException("Window backdrops require Windows 11 22H2 or later.");
        extended = (o.AlwaysOnTop ? 8u : 0) | (o.SkipTaskbar || o.Panel ? 0x80u : 0x40000u) | (o.IgnoreMouseEvents ? 0x20u : 0) | (o.Opacity < 1 || o.Transparent || o.IgnoreMouseEvents ? 0x80000u : 0);
        SetWindowLongPtrW(handle, -20, (nint)extended);
        if ((extended & 0x80000) != 0 && SetLayeredWindowAttributes(handle, 0, (byte)Math.Round(o.Opacity * 255), 2) == 0)
            throw new InvalidOperationException("SetLayeredWindowAttributes failed.");
        style = o.Frameless ? 0x80000000u : 0x00c80000u;
        if (o.Resizable)
            style |= 0x40000;
        if (o.Minimizable)
            style |= 0x20000;
        if (o.Maximizable)
            style |= 0x10000;
        if (!fullScreen)
            SetWindowLongPtrW(handle, -16, (nint)style);
        var menu = GetSystemMenu(handle, 0);
        EnableMenuItem(menu, 0xf060, (uint)(o.Closable ? 0 : 1));
        EnableMenuItem(menu, 0xf010, (uint)(o.Movable ? 0 : 1));
        if (SetWindowDisplayAffinity(handle, o.ContentProtection ? 0x11u : 0) == 0)
            throw new InvalidOperationException("SetWindowDisplayAffinity failed.");
        int backdrop = o.Material == WindowMaterial.Mica ? 2 : o.Material == WindowMaterial.Acrylic ? 3 : 1;
        if (o.Material != WindowMaterial.None && DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int)) < 0)
            throw new InvalidOperationException("Windows rejected the requested backdrop.");
        int dark = window.App.Options.Hidden ? 0 : Desktop.Appearance.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (o.HiddenTitleBar)
        {
            var margins = new Margins
            {
                Top = 1
            };
            DwmExtendFrameIntoClientArea(handle, ref margins);
        }

        int policy = o.HasShadow ? 2 : 1;
        DwmSetWindowAttribute(handle, 2, ref policy, sizeof(int));
        if (o.Parent is { } parent)
        {
            SetWindowLongPtrW(handle, -8, parent.NativeHandle);
            if (o.Modal && IsVisible)
                EnableWindow(parent.NativeHandle, 0);
        }

        Win32.SetWindowPos(handle, o.AlwaysOnTop ? -1 : -2, 0, 0, 0, 0, 0x33);
    }

    public void Perform(WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Focus:
                if (SetForegroundWindow(handle) == 0)
                    throw new InvalidOperationException("Windows refused foreground activation.");
                break;
            case WindowAction.Blur:
                if (IsFocused)
                    SetForegroundWindow(GetDesktopWindow());
                break;
            case WindowAction.Maximize:
                if (!IsMaximized)
                    Win32.ShowWindow(handle, 3);
                break;
            case WindowAction.Unmaximize:
                if (IsMaximized)
                    Win32.ShowWindow(handle, 9);
                break;
            case WindowAction.Restore:
                Win32.ShowWindow(handle, 9);
                break;
            case WindowAction.EnterFullScreen:
                if (fullScreen)
                    break;
                restoreBounds = Bounds;
                restoreStyle = style;
                var rect = new Win32.NativeRectangle
                {
                    Left = (int)Bounds.X,
                    Top = (int)Bounds.Y,
                    Right = (int)Bounds.Right,
                    Bottom = (int)Bounds.Bottom
                };
                var info = new MonitorInfo
                {
                    Size = (uint)sizeof(MonitorInfo)
                };
                GetMonitorInfoW(MonitorFromRect(ref rect, 2), ref info);
                SetWindowLongPtrW(handle, -16, (nint)(style & ~0x00cf0000u));
                fullScreen = true;
                SetBounds(new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top));
                break;
            case WindowAction.LeaveFullScreen:
                if (!fullScreen)
                    break;
                fullScreen = false;
                SetWindowLongPtrW(handle, -16, (nint)restoreStyle);
                SetBounds(restoreBounds);
                break;
        }

        window.NativeShellChanged();
    }

    public void Progress(double? value) => TaskbarProgress.Set(handle, value);
    public void Attention(bool enabled)
    {
        var info = new FlashInfo
        {
            Size = (uint)sizeof(FlashInfo),
            Window = handle,
            Flags = enabled ? 3u | 12u : 0,
            Count = uint.MaxValue
        };
        FlashWindowEx(ref info);
    }

    bool ShellMessage(uint message, nuint w, nint l, out nint result)
    {
        result = 0;
        if (message == 0x83 && w != 0 && window.Options.HiddenTitleBar)
            return true;
        if (message == 0x84 && window.Options.HiddenTitleBar)
        {
            if (DwmDefWindowProc(handle, message, w, l, out result) != 0)
                return true;
        }

        if (message == 0x104 && (w == 18 || w == 121) && window.Options.AutoHideMenuBar)
        {
            var bounds = Bounds;
            (window.Menu ?? window.App.Menu)?.PopupWindows(handle, (int)bounds.X, (int)bounds.Y + 24);
            return true;
        }

        if (message == 0x111 && l == 0)
            return (window.Menu ?? window.App.Menu)?.InvokeCommand((uint)(w & 65535), window) == true;
        if (message == 0x24)
        {
            var limits = (int*)l;
            var o = window.Options;
            var frame = Frame();
            float scale = Math.Max(1, Win32.GetDpiForWindow(handle) / 96f);
            limits[6] = (int)(o.MinWidth * scale) + frame.Right - frame.Left;
            limits[7] = (int)(o.MinHeight * scale) + frame.Bottom - frame.Top;
            if (o.MaxWidth > 0)
                limits[8] = (int)(o.MaxWidth * scale) + frame.Right - frame.Left;
            if (o.MaxHeight > 0)
                limits[9] = (int)(o.MaxHeight * scale) + frame.Bottom - frame.Top;
            return true;
        }

        if (message == 0x112 && (((w & 0xfff0) == 0xf010 && !window.Options.Movable) || ((w & 0xfff0) == 0xf020 && !window.Options.Minimizable) || ((w & 0xfff0) == 0xf030 && !window.Options.Maximizable)))
            return true;
        if (message is 3 or 5 or 7 or 8 or 0x18)
            window.App.Dispatch(window.NativeShellChanged);
        if (message == 0x1c)
            window.App.NativeActiveChanged(w != 0);
        return false;
    }

    void DisposeShell()
    {
        if (window.Options is { Modal: true, Parent: { } parent })
            EnableWindow(parent.NativeHandle, 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);
    [DllImport("dwmapi.dll")]
    static extern int DwmDefWindowProc(nint window, uint message, nuint w, nint l, out nint result);
    [StructLayout(LayoutKind.Sequential)]
    struct FlashInfo
    {
        public uint Size;
        public nint Window;
        public uint Flags, Count, Timeout;
    }

    [DllImport("user32.dll")]
    static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern nint GetDesktopWindow();
    [DllImport("user32.dll")]
    static extern int SetForegroundWindow(nint window);
    [DllImport("user32.dll")]
    static extern int IsIconic(nint window);
    [DllImport("user32.dll")]
    static extern int GetWindowRect(nint window, out Win32.NativeRectangle rectangle);
    [DllImport("user32.dll")]
    static extern nint SetWindowLongPtrW(nint window, int index, nint value);
    [DllImport("user32.dll")]
    static extern int SetLayeredWindowAttributes(nint window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")]
    static extern int SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")]
    static extern nint GetSystemMenu(nint window, int revert);
    [DllImport("user32.dll")]
    static extern int EnableMenuItem(nint menu, uint item, uint enabled);
    [DllImport("user32.dll")]
    static extern int EnableWindow(nint window, int enabled);
    [DllImport("user32.dll")]
    static extern int FlashWindowEx(ref FlashInfo info);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window, uint attribute, ref int value, int size);
}

internal static unsafe class TaskbarProgress
{
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    internal static void Set(nint window, double? progress)
    {
        var clsid = new Guid("56fdf344-fd6d-11d0-958a-006097c9a090");
        var iid = new Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");
        Marshal.ThrowExceptionForHR(CoCreateInstance(clsid, 0, 1, iid, out var taskbar));
        try
        {
            var v = *(nint**)taskbar;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int>)v[3])(taskbar));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, uint, int>)v[10])(taskbar, window, progress is null ? 0u : 2u));
            if (progress is { } value)
                Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint, ulong, ulong, int>)v[9])(taskbar, window, (ulong)(value * 1000), 1000));
        }
        finally
        {
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)taskbar)[2])(taskbar);
        }
    }
}
