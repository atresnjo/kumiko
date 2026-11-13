using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal static class LauncherPointer
{
    internal static Point Position()
    {
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var point = O.P(O.Class("NSEvent"), "mouseLocation");
            var primary = O.O(O.O(O.Class("NSScreen"), "screens"), "objectAtIndex:", (nuint)0);
            return new((float)point.X, (float)(O.R(primary, "frame").Size.Height - point.Y));
        }
        if (OperatingSystem.IsWindows())
        {
            if (!GetCursorPos(out var point))
                throw new InvalidOperationException("Cannot read the cursor position.");
            return new(point.X, point.Y);
        }
        var display = gdk_display_get_default();
        var seat = gdk_display_get_default_seat(display);
        var device = gdk_seat_get_pointer(seat);
        gdk_device_get_position(device, 0, out var x, out var y);
        return new(x, y);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CursorPoint
    {
        public int X, Y;
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out CursorPoint point);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_display_get_default();
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_display_get_default_seat(nint display);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_seat_get_pointer(nint seat);
    [DllImport(Gtk.Gdk)]
    static extern void gdk_device_get_position(nint device, nint screen, out int x, out int y);
}
