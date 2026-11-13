using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed unsafe class LinuxNotificationBackend : INotificationBackend
{
    const string Gio = "libgio-2.0.so.0";
    readonly App app;
    readonly Action<NotificationClick> click;
    readonly nint connection;
    readonly uint subscription;
    readonly Dictionary<(string, string), uint> ids = new();
    readonly Dictionary<uint, NotificationClick> clicks = new();
    GCHandle context;
    internal LinuxNotificationBackend(App app, Action<NotificationClick> click)
    {
        this.app = app;
        this.click = click;
        connection = g_bus_get_sync(2, 0, out var error);
        ThrowError(error);
        if (connection == 0)
            throw new PlatformNotSupportedException("The Linux notification service requires a session D-Bus.");
        context = GCHandle.Alloc(this);
        subscription = g_dbus_connection_signal_subscribe(connection, "org.freedesktop.Notifications", "org.freedesktop.Notifications", "ActionInvoked", "/org/freedesktop/Notifications", null, 0, &Signal, GCHandle.ToIntPtr(context), 0);
    }

    public Task Show(NotificationOptions options)
    {
        using var name = new Utf8(app.Options.Name);
        using var icon = new Utf8("");
        using var title = new Utf8(options.Subtitle.Length == 0 ? options.Title : options.Title + " — " + options.Subtitle);
        using var body = new Utf8(options.Body);
        using var action = new Utf8("default");
        using var actionLabel = new Utf8("Open");
        nint* actions = stackalloc nint[2]
        {
            action.Handle,
            actionLabel.Handle
        };
        var dictType = g_variant_type_new("{sv}");
        nint* values = stackalloc nint[8]
        {
            g_variant_new_string(name.Handle),
            g_variant_new_uint32(ids.GetValueOrDefault((options.Id, options.Group))),
            g_variant_new_string(icon.Handle),
            g_variant_new_string(title.Handle),
            g_variant_new_string(body.Handle),
            g_variant_new_strv(actions, 2),
            g_variant_new_array(dictType, null, 0),
            g_variant_new_int32(-1)
        };
        g_variant_type_free(dictType);
        var result = Call("Notify", g_variant_new_tuple(values, 8));
        try
        {
            var value = g_variant_get_child_value(result, 0);
            uint id = g_variant_get_uint32(value);
            g_variant_unref(value);
            ids[(options.Id, options.Group)] = id;
            clicks[id] = new(options.Id, options.Group);
        }
        finally
        {
            g_variant_unref(result);
        }

        return Task.CompletedTask;
    }

    nint Call(string method, nint parameters)
    {
        var result = g_dbus_connection_call_sync(connection, "org.freedesktop.Notifications", "/org/freedesktop/Notifications", "org.freedesktop.Notifications", method, parameters, 0, 0, 5000, 0, out var error);
        ThrowError(error);
        if (result == 0)
            throw new IOException("The notification service returned no result.");
        return result;
    }

    static void ThrowError(nint error)
    {
        if (error == 0)
            return;
        var value = (GError*)error;
        var message = Marshal.PtrToStringUTF8(value->Message) ?? "D-Bus request failed.";
        g_error_free(error);
        throw new PlatformNotSupportedException("org.freedesktop.Notifications: " + message);
    }

    [UnmanagedCallersOnly]
    static void Signal(nint connection, nint sender, nint path, nint iface, nint signal, nint parameters, nint data)
    {
        var owner = (LinuxNotificationBackend)GCHandle.FromIntPtr(data).Target!;
        try
        {
            var value = g_variant_get_child_value(parameters, 0);
            uint id = g_variant_get_uint32(value);
            g_variant_unref(value);
            if (owner.clicks.TryGetValue(id, out var click))
                owner.click(click);
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    public void Close(string id, string group)
    {
        if (!ids.TryGetValue((id, group), out uint native))
            return;
        nint value = g_variant_new_uint32(native);
        var result = Call("CloseNotification", g_variant_new_tuple(&value, 1));
        g_variant_unref(result);
        ids.Remove((id, group));
        clicks.Remove(native);
    }

    public void Clear()
    {
        foreach (var key in ids.Keys.ToArray())
            Close(key.Item1, key.Item2);
    }

    public void Dispose()
    {
        g_dbus_connection_signal_unsubscribe(connection, subscription);
        if (context.IsAllocated)
            context.Free();
        Gtk.g_object_unref(connection);
    }

    readonly struct Utf8 : IDisposable
    {
        internal nint Handle
        {
            get;
        }

        internal Utf8(string text) => Handle = Marshal.StringToCoTaskMemUTF8(text);
        public void Dispose() => Marshal.FreeCoTaskMem(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct GError
    {
        public uint Domain;
        public int Code;
        public nint Message;
    }

    [DllImport(Gio)]
    static extern nint g_bus_get_sync(int type, nint cancellation, out nint error);
    [DllImport(Gio)]
    static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, int flags, int timeout, nint cancellation, out nint error);
    [DllImport(Gio)]
    static extern uint g_dbus_connection_signal_subscribe(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string member, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? arg0, int flags, delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint data, nint destroy);
    [DllImport(Gio)]
    static extern void g_dbus_connection_signal_unsubscribe(nint connection, uint subscription);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_string(nint value);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_uint32(uint value);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_int32(int value);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_strv(nint* values, nint count);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_array(nint type, nint* children, nuint count);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_new_tuple(nint* children, nuint count);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [DllImport(Gtk.GLib)]
    static extern void g_variant_type_free(nint type);
    [DllImport(Gtk.GLib)]
    static extern nint g_variant_get_child_value(nint value, nuint index);
    [DllImport(Gtk.GLib)]
    static extern uint g_variant_get_uint32(nint value);
    [DllImport(Gtk.GLib)]
    static extern void g_variant_unref(nint value);
    [DllImport(Gtk.GLib)]
    static extern void g_error_free(nint error);
}
