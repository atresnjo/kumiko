using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public static unsafe partial class Screens
{
    /// <summary>AppKit screens, flipped from bottom-left to top-left coordinates of the primary display.</summary>
    static List<Screen> Mac()
    {
        using var pool = new AutoreleasePool();
        var list = O.O(O.Class("NSScreen"), "screens");
        var count = O.N(list, "count");
        var result = new List<Screen>((int)count);
        double top = 0;
        for (nuint i = 0; i < count; i++)
        {
            var screen = O.O(list, "objectAtIndex:", i);
            var frame = O.R(screen, "frame");
            var visible = O.R(screen, "visibleFrame");
            if (i == 0)
                top = frame.Size.Height;
            Rect Flip(NativeRect r) => new((float)r.Origin.X, (float)(top - r.Origin.Y - r.Size.Height), (float)r.Size.Width, (float)r.Size.Height);
            result.Add(new(Flip(frame), Flip(visible), (float)O.D(screen, "backingScaleFactor"), i == 0));
        }
        return result;
    }

    /// <summary>Win32 monitors in virtual-screen pixels, the coordinates GetWindowPlacement uses.</summary>
    static List<Screen> Windows()
    {
        var handles = new List<nint>();
        var gate = GCHandle.Alloc(handles);
        try
        {
            EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(gate));
        }
        finally
        {
            gate.Free();
        }
        var result = new List<Screen>(handles.Count);
        foreach (var handle in handles)
        {
            var info = new MonitorInfo
            {
                Size = (uint)sizeof(MonitorInfo)
            };
            if (GetMonitorInfoW(handle, ref info) == 0)
                continue;
            var scale = GetDpiForMonitor(handle, 0, out var dpi, out _) == 0 ? dpi / 96f : 1;
            result.Add(new(info.Monitor.ToRect(), info.Work.ToRect(), scale, (info.Flags & 1) != 0));
        }
        return result;
    }

    [UnmanagedCallersOnly]
    static int Collect(nint monitor, nint context, nint rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> handles)
            handles.Add(monitor);
        return 1;
    }

    /// <summary>GDK monitors in application pixels (logical points).</summary>
    static List<Screen> Linux()
    {
        var display = gdk_display_get_default();
        var result = new List<Screen>();
        if (display == 0)
            return result;
        var count = gdk_display_get_n_monitors(display);
        for (var i = 0; i < count; i++)
        {
            var monitor = gdk_display_get_monitor(display, i);
            if (monitor == 0)
                continue;
            gdk_monitor_get_geometry(monitor, out var geometry);
            gdk_monitor_get_workarea(monitor, out var work);
            result.Add(new(geometry.ToRect(), work.ToRect(), gdk_monitor_get_scale_factor(monitor), gdk_monitor_is_primary(monitor) != 0 || count == 1));
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Win32Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly Rect ToRect() => new(Left, Top, Right - Left, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MonitorInfo
    {
        public uint Size;
        public Win32Rect Monitor;
        public Win32Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GdkRectangle
    {
        public int X, Y, Width, Height;
        public readonly Rect ToRect() => new(X, Y, Width, Height);
    }

    [DllImport("user32.dll")]
    static extern int EnumDisplayMonitors(nint hdc, nint clip, delegate* unmanaged<nint, nint, nint, nint, int> callback, nint data);
    [DllImport("user32.dll")]
    static extern int GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_display_get_default();
    [DllImport(Gtk.Gdk)]
    static extern int gdk_display_get_n_monitors(nint display);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_display_get_monitor(nint display, int index);
    [DllImport(Gtk.Gdk)]
    static extern void gdk_monitor_get_geometry(nint monitor, out GdkRectangle geometry);
    [DllImport(Gtk.Gdk)]
    static extern void gdk_monitor_get_workarea(nint monitor, out GdkRectangle workarea);
    [DllImport(Gtk.Gdk)]
    static extern int gdk_monitor_get_scale_factor(nint monitor);
    [DllImport(Gtk.Gdk)]
    static extern int gdk_monitor_is_primary(nint monitor);
}
