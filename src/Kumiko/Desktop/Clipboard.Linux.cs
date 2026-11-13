using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed unsafe class LinuxClipboardBackend : IClipboardBackend
{
    readonly App app;
    readonly nint clipboard;
    readonly List<nint> targets = new();
    GCHandle context;
    TransferSnapshot? snapshot;
    internal LinuxClipboardBackend(App app, string selection = "CLIPBOARD")
    {
        this.app = app;
        clipboard = gtk_clipboard_get(Atom(selection));
        context = GCHandle.Alloc(this);
    }

    static nint Atom(string name) => gdk_atom_intern(name, 0);
    static string Portable(string type) => type is "UTF8_STRING" or "text/plain" ? TransferFormats.Text : type;
    public IReadOnlyList<string> Formats
    {
        get
        {
            app.CheckThread();
            if (gtk_clipboard_wait_for_targets(clipboard, out var atoms, out int count) == 0)
                return Array.Empty<string>();
            try
            {
                var result = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    var name = gdk_atom_name(((nint*)atoms)[i]);
                    var type = Marshal.PtrToStringUTF8(name) ?? "";
                    Gtk.g_free(name);
                    if (type.Contains('/') || type == "UTF8_STRING")
                        result.Add(Portable(type));
                }

                return Array.AsReadOnly(result.Distinct().ToArray());
            }
            finally
            {
                Gtk.g_free(atoms);
            }
        }
    }

    public TransferData Read(string[] formats)
    {
        app.CheckThread();
        if (snapshot is { } own)
            return own.Materialize(formats);
        var selected = formats.Length == 0 ? Formats : formats;
        var result = new List<TransferRepresentation>();
        foreach (var format in selected.Distinct())
        {
            var selection = gtk_clipboard_wait_for_contents(clipboard, Atom(format));
            if (selection == 0 && format == TransferFormats.Text)
                selection = gtk_clipboard_wait_for_contents(clipboard, Atom("UTF8_STRING"));
            if (selection == 0)
                continue;
            try
            {
                int length = gtk_selection_data_get_length(selection);
                if (length < 0)
                    continue;
                if (length > TransferSnapshot.MaximumForeignBytes)
                    throw new IOException("Foreign clipboard representation exceeds 64 MiB.");
                var bytes = new byte[length];
                if (length > 0)
                    Marshal.Copy(gtk_selection_data_get_data(selection), bytes, 0, length);
                result.Add(new(format, bytes));
            }
            finally
            {
                gtk_selection_data_free(selection);
            }
        }

        if (formats.Length > 0 && result.Count == 0)
            throw new KeyNotFoundException("None of the requested clipboard formats are available.");
        return result.Count == 0 ? new() : new(new TransferItem(result.ToArray()));
    }

    public void Write(TransferSnapshot next)
    {
        app.CheckThread();
        Clear();
        snapshot = next;
        var formats = next.Data.Formats.ToList();
        if (formats.Contains(TransferFormats.Text))
            formats.Add("UTF8_STRING");
        var entries = new Target[formats.Count];
        for (int i = 0; i < formats.Count; i++)
        {
            var name = Marshal.StringToCoTaskMemUTF8(formats[i]);
            targets.Add(name);
            entries[i] = new()
            {
                Name = name,
                Info = (uint)i
            };
        }

        fixed (Target* p = entries)
            if (entries.Length > 0 && gtk_clipboard_set_with_data(clipboard, p, (uint)entries.Length, &Get, &Lost, GCHandle.ToIntPtr(context)) == 0)
                throw new IOException("GTK clipboard ownership failed.");
    }

    [UnmanagedCallersOnly]
    static void Get(nint clipboard, nint selection, uint info, nint data)
    {
        var owner = (LinuxClipboardBackend)GCHandle.FromIntPtr(data).Target!;
        try
        {
            if (owner.snapshot is not { } snapshot || info >= owner.targets.Count)
                return;
            var type = Marshal.PtrToStringUTF8(owner.targets[(int)info])!;
            var bytes = snapshot.Read(Portable(type));
            fixed (byte* p = bytes)
                gtk_selection_data_set(selection, Atom(type), 8, p, bytes.Length);
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static void Lost(nint clipboard, nint data)
    {
        var owner = (LinuxClipboardBackend)GCHandle.FromIntPtr(data).Target!;
        try
        {
            owner.Release();
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    void Release()
    {
        var previous = snapshot;
        snapshot = null;
        foreach (var target in targets)
            Marshal.FreeCoTaskMem(target);
        targets.Clear();
        previous?.Dispose();
    }

    public void Clear()
    {
        app.CheckThread();
        gtk_clipboard_clear(clipboard);
        Release();
    }

    public void Flush()
    {
        app.CheckThread();
        if (gdk_selection_owner_get_for_display(gdk_display_get_default(), Atom("CLIPBOARD_MANAGER")) == 0)
            throw new PlatformNotSupportedException("No Linux clipboard manager owns CLIPBOARD_MANAGER; clipboard persistence is unavailable.");
        gtk_clipboard_set_can_store(clipboard, 0, 0);
        gtk_clipboard_store(clipboard);
    }

    public void Dispose()
    {
        Clear();
        if (context.IsAllocated)
            context.Free();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Target
    {
        public nint Name;
        public uint Flags, Info;
    }

    [DllImport(Gtk.Library)]
    static extern nint gtk_clipboard_get(nint selection);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_atom_intern([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_atom_name(nint atom);
    [DllImport(Gtk.Library)]
    static extern int gtk_clipboard_wait_for_targets(nint clipboard, out nint targets, out int count);
    [DllImport(Gtk.Library)]
    static extern nint gtk_clipboard_wait_for_contents(nint clipboard, nint target);
    [DllImport(Gtk.Library)]
    static extern int gtk_selection_data_get_length(nint selection);
    [DllImport(Gtk.Library)]
    static extern nint gtk_selection_data_get_data(nint selection);
    [DllImport(Gtk.Library)]
    static extern void gtk_selection_data_free(nint selection);
    [DllImport(Gtk.Library)]
    static extern int gtk_clipboard_set_with_data(nint clipboard, Target* targets, uint count, delegate* unmanaged<nint, nint, uint, nint, void> get, delegate* unmanaged<nint, nint, void> clear, nint data);
    [DllImport(Gtk.Library)]
    static extern void gtk_selection_data_set(nint selection, nint type, int format, byte* data, int length);
    [DllImport(Gtk.Library)]
    static extern void gtk_clipboard_clear(nint clipboard);
    [DllImport(Gtk.Library)]
    static extern void gtk_clipboard_set_can_store(nint clipboard, nint targets, int count);
    [DllImport(Gtk.Library)]
    static extern void gtk_clipboard_store(nint clipboard);
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_display_get_default();
    [DllImport(Gtk.Gdk)]
    static extern nint gdk_selection_owner_get_for_display(nint display, nint selection);
}
