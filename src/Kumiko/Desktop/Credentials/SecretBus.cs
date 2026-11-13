using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

/// <summary>Small owned GVariant/GDBus boundary shared by the Linux native services.</summary>
internal sealed unsafe class SecretVariant : IDisposable
{
    internal const string GLib = "libglib-2.0.so.0";
    internal const string Gio = "libgio-2.0.so.0";
    internal const string GObject = "libgobject-2.0.so.0";
    internal nint Handle
    {
        get;
    }
    internal SecretVariant(nint handle)
    {
        if (handle == 0)
            throw new InvalidOperationException("The native service returned no value.");
        Handle = g_variant_ref_sink(handle);
    }
    internal static SecretVariant Adopt(nint handle)
    {
        var value = new SecretVariant(handle);
        g_variant_unref(handle);
        return value;
    }
    internal SecretVariant Child(nuint index) => Adopt(g_variant_get_child_value(Handle, index));
    internal string Text => Marshal.PtrToStringUTF8(g_variant_get_string(Handle, 0)) ?? "";
    internal bool Boolean => g_variant_get_boolean(Handle) != 0;
    internal uint UInt32 => g_variant_get_uint32(Handle);
    internal double Double => g_variant_get_double(Handle);
    internal nuint Count => g_variant_n_children(Handle);
    internal SecretVariant Unbox() => Adopt(g_variant_get_variant(Handle));
    internal static SecretVariant String(string value) => new(g_variant_new_string(value));
    internal static SecretVariant Path(string value) => new(g_variant_new_object_path(value));
    internal static SecretVariant Bool(bool value) => new(g_variant_new_boolean(value ? 1 : 0));
    internal static SecretVariant Box(SecretVariant value) => new(g_variant_new_variant(value.Handle));
    internal static SecretVariant Tuple(params SecretVariant[] children)
    {
        var handles = children.Select(c => c.Handle).ToArray();
        fixed (nint* values = handles)
            return new(g_variant_new_tuple(values, (nuint)handles.Length));
    }
    internal static SecretVariant Dictionary(string type, params (string Key, SecretVariant Value)[] entries)
    {
        nint element = g_variant_type_new(type);
        var values = new nint[entries.Length];
        try
        {
            for (int i = 0; i < entries.Length; i++)
            {
                using var key = String(entries[i].Key);
                values[i] = g_variant_ref_sink(g_variant_new_dict_entry(key.Handle, entries[i].Value.Handle));
            }
            fixed (nint* items = values)
                return new(g_variant_new_array(element, items, (nuint)values.Length));
        }
        finally
        {
            foreach (var value in values)
                if (value != 0)
                    g_variant_unref(value);
            g_variant_type_free(element);
        }
    }
    public void Dispose() => g_variant_unref(Handle);

    [DllImport(GLib)]
    static extern nint g_variant_ref_sink(nint value);
    [DllImport(GLib)]
    static extern void g_variant_unref(nint value);
    [DllImport(GLib)]
    static extern nint g_variant_get_child_value(nint value, nuint index);
    [DllImport(GLib)]
    static extern nint g_variant_get_string(nint value, nint length);
    [DllImport(GLib)]
    static extern int g_variant_get_boolean(nint value);
    [DllImport(GLib)]
    static extern uint g_variant_get_uint32(nint value);
    [DllImport(GLib)]
    static extern double g_variant_get_double(nint value);
    [DllImport(GLib)]
    static extern nuint g_variant_n_children(nint value);
    [DllImport(GLib)]
    static extern nint g_variant_get_variant(nint value);
    [DllImport(GLib)]
    static extern nint g_variant_new_string([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(GLib)]
    static extern nint g_variant_new_object_path([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(GLib)]
    static extern nint g_variant_new_boolean(int value);
    [DllImport(GLib)]
    static extern nint g_variant_new_variant(nint value);
    [DllImport(GLib)]
    static extern nint g_variant_new_tuple(nint* values, nuint count);
    [DllImport(GLib)]
    static extern nint g_variant_new_array(nint type, nint* values, nuint count);
    [DllImport(GLib)]
    static extern nint g_variant_new_dict_entry(nint key, nint value);
    [DllImport(GLib)]
    static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [DllImport(GLib)]
    static extern void g_variant_type_free(nint type);
}

internal sealed unsafe class SecretBusException(CredentialStatus status) : Exception("Native credential operation failed.")
{
    internal CredentialStatus Status => status;
    internal static void Throw(nint error)
    {
        if (error == 0)
            return;
        var remote = g_dbus_error_get_remote_error(error);
        try
        {
            var name = Marshal.PtrToStringUTF8(remote) ?? "";
            var native = (Error*)error;
            var status = name switch
            {
                "org.freedesktop.Secret.Error.IsLocked" => CredentialStatus.Locked,
                "org.freedesktop.Secret.Error.NoSuchObject" => CredentialStatus.Missing,
                "org.freedesktop.DBus.Error.AccessDenied" or "org.freedesktop.DBus.Error.AuthFailed" => CredentialStatus.Denied,
                "org.freedesktop.DBus.Error.ServiceUnknown" or "org.freedesktop.DBus.Error.NameHasNoOwner" or
                "org.freedesktop.DBus.Error.NoReply" or "org.freedesktop.DBus.Error.Disconnected" => CredentialStatus.Unavailable,
                _ when native->Domain == g_io_error_quark() && native->Code == 19 => CredentialStatus.Cancelled,
                _ => CredentialStatus.Failed
            };
            throw new SecretBusException(status);
        }
        finally
        {
            g_free(remote);
            g_error_free(error);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Error
    {
        internal uint Domain;
        internal int Code;
        internal nint Message;
    }
    [DllImport(SecretVariant.Gio)]
    static extern uint g_io_error_quark();
    [DllImport(SecretVariant.Gio)]
    static extern nint g_dbus_error_get_remote_error(nint error);
    [DllImport(SecretVariant.GLib)]
    static extern void g_error_free(nint error);
    [DllImport(SecretVariant.GLib)]
    internal static extern void g_free(nint value);
}
