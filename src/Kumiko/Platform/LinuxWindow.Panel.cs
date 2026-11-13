using System.Runtime.InteropServices;

namespace Kumiko.Platform;

internal sealed partial class LinuxWindow
{
    [DllImport(Gtk.Library)]
    static extern void gtk_window_set_type_hint(nint window, int hint);
}
