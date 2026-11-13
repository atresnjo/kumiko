using System.Runtime.InteropServices;
using Kumiko.Desktop;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed unsafe partial class LinuxWindow
{
    TransferSnapshot? dragSnapshot;
    string[] dragFormats = [];
    bool dragConnected;
    public void StartDrag(TransferData data, Rect bounds)
    {
        dragSnapshot?.Dispose();
        dragSnapshot = new(owner.App, data);
        dragFormats = data.Formats.ToArray();
        if (dragFormats.Length == 0)
            throw new ArgumentException("A native drag must offer at least one format.", nameof(data));
        if (!dragConnected)
        {
            Connect(view, "drag-data-get", (nint)(delegate* unmanaged<nint, nint, nint, uint, uint, nint, void>)&DragGet);
            Connect(view, "drag-end", (nint)(delegate* unmanaged<nint, nint, nint, void>)&DragEnd);
            dragConnected = true;
        }

        var entries = new DragTarget[dragFormats.Length];
        nint list = 0, ev = 0;
        try
        {
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new()
                {
                    Name = Marshal.StringToCoTaskMemUTF8(dragFormats[i]),
                    Info = (uint)i
                };
            fixed (DragTarget* p = entries)
                list = gtk_target_list_new(p, (uint)entries.Length);
            ev = gtk_get_current_event();
            if (ev == 0)
                throw new InvalidOperationException("A GTK drag needs a current pointer event.");
            if (gtk_drag_begin_with_coordinates(view, list, 2, 1, ev, -1, -1) == 0)
                throw new IOException("GTK could not begin a native drag.");
        }
        catch
        {
            dragSnapshot.Dispose();
            dragSnapshot = null;
            throw;
        }
        finally
        {
            foreach (var entry in entries)
                if (entry.Name != 0)
                    Marshal.FreeCoTaskMem(entry.Name);
            if (list != 0)
                gtk_target_list_unref(list);
            if (ev != 0)
                gdk_event_free(ev);
        }
    }

    [UnmanagedCallersOnly]
    static void DragGet(nint self, nint drag, nint selection, uint info, uint time, nint data) => Guard(self, w =>
    {
        if (w.dragSnapshot is null || info >= w.dragFormats.Length)
            return;
        var format = w.dragFormats[(int)info];
        var bytes = w.dragSnapshot.Read(format);
        fixed (byte* p = bytes)
            gtk_selection_data_set(selection, gdk_atom_intern(format, 0), 8, p, bytes.Length);
    });
    [UnmanagedCallersOnly]
    static void DragEnd(nint self, nint drag, nint data) => Guard(self, w =>
    {
        w.dragSnapshot?.Dispose();
        w.dragSnapshot = null;
        w.dragFormats = [];
    });
    [StructLayout(LayoutKind.Sequential)]
    struct DragTarget
    {
        public nint Name;
        public uint Flags, Info;
    }

    [DllImport(Gtk.Library)]
    static extern nint gtk_target_list_new(DragTarget* targets, uint count);
    [DllImport(Gtk.Library)]
    static extern void gtk_target_list_unref(nint list);
    [DllImport(Gtk.Library)]
    static extern nint gtk_get_current_event();
    [DllImport(Gtk.Library)]
    static extern nint gtk_drag_begin_with_coordinates(nint widget, nint targets, uint actions, int button, nint ev, int x, int y);
    [DllImport(Gtk.Gdk)]
    static extern void gdk_event_free(nint ev);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int existing);
    [DllImport(Gtk.Library)]
    static extern void gtk_selection_data_set(nint selection, nint type, int bits, byte* data, int length);
}
