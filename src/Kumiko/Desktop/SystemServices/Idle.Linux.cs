using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

/// <summary>X11's screen-saver idle counter only; XWayland cannot report the entire Wayland session.</summary>
internal sealed class LinuxIdleClock : IDisposable
{
    nint display;
    nint info;
    internal static LinuxIdleClock? TryOpen()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland")
            return null;
        var clock = new LinuxIdleClock();
        try
        {
            clock.display = XOpenDisplay(0);
            if (clock.display == 0 || XScreenSaverQueryExtension(clock.display, out _, out _) == 0)
            {
                clock.Dispose();
                return null;
            }
            clock.info = XScreenSaverAllocInfo();
            if (clock.info != 0)
                return clock;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        clock.Dispose();
        return null;
    }
    internal TimeSpan? Read()
    {
        if (display == 0 || info == 0 || XScreenSaverQueryInfo(display, XDefaultRootWindow(display), info) == 0)
            return null;
        var value = Marshal.PtrToStructure<ScreenSaverInfo>(info);
        return value.Idle <= TimeSpan.MaxValue.TotalMilliseconds ? TimeSpan.FromMilliseconds(value.Idle) : null;
    }
    public void Dispose()
    {
        if (info != 0)
        {
            XFree(info);
            info = 0;
        }
        if (display != 0)
        {
            XCloseDisplay(display);
            display = 0;
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ScreenSaverInfo
    {
        public nuint Window;
        public int State;
        public int Kind;
        public nuint Since;
        public nuint Idle;
        public nuint Mask;
    }
    [DllImport("libX11.so.6")]
    static extern nint XOpenDisplay(nint name);
    [DllImport("libX11.so.6")]
    static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")]
    static extern int XFree(nint data);
    [DllImport("libX11.so.6")]
    static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libXss.so.1")]
    static extern int XScreenSaverQueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport("libXss.so.1")]
    static extern nint XScreenSaverAllocInfo();
    [DllImport("libXss.so.1")]
    static extern int XScreenSaverQueryInfo(nint display, nuint window, nint info);
}
