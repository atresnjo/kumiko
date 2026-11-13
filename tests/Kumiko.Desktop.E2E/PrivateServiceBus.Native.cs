using System.Reflection;
using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using S = Kumiko.Desktop.SecretVariant;

sealed unsafe partial class PrivateServiceBus
{
    internal static void ResolveLibraries()
    {
        if (!OperatingSystem.IsMacOS())
            return;
        nint Resolve(string name, Assembly assembly, DllImportSearchPath? path) => name switch
        {
            S.Gio => NativeLibrary.Load("/opt/homebrew/lib/libgio-2.0.dylib"),
            S.GLib => NativeLibrary.Load("/opt/homebrew/lib/libglib-2.0.dylib"),
            S.GObject => NativeLibrary.Load("/opt/homebrew/lib/libgobject-2.0.dylib"),
            _ => 0
        };
        NativeLibrary.SetDllImportResolver(typeof(App).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(PrivateServiceBus).Assembly, Resolve);
    }
    internal static nint Connect(string address, nint cancellation = 0)
    {
        nint result = g_dbus_connection_new_for_address_sync(address, 1 | 8, 0, cancellation, out var error);
        SecretBusException.Throw(error);
        if (result == 0)
            throw new IOException("Private fixture bus connection failed.");
        return result;
    }
    internal static S UInt32(uint value) => new(g_variant_new_uint32(value));
    static S Handle(int value) => new(g_variant_new_handle(value));
    internal static S Bytes(ReadOnlySpan<byte> bytes)
    {
        nint type = g_variant_type_new("y");
        try
        {
            fixed (byte* pointer = bytes)
                return new(g_variant_new_fixed_array(type, pointer, (nuint)bytes.Length, 1));
        }
        finally
        {
            g_variant_type_free(type);
        }
    }
    internal static SecretBuffer CopyBytes(S value)
    {
        var data = g_variant_get_fixed_array(value.Handle, out var length, 1);
        if (length > CredentialStore.MaximumSecretBytes)
            throw new IOException("Invalid private secret length.");
        return new(new ReadOnlySpan<byte>(data, (int)length));
    }
    static S Paths(params S[] paths)
    {
        nint type = g_variant_type_new("o");
        try
        {
            var children = paths.Select(p => p.Handle).ToArray();
            fixed (nint* pointer = children)
                return new(g_variant_new_array(type, pointer, (nuint)children.Length));
        }
        finally
        {
            g_variant_type_free(type);
        }
    }
    static void Reply(nint invocation, S value) => g_dbus_method_invocation_return_value(invocation, value.Handle);
    static void Error(nint invocation, string name) => g_dbus_method_invocation_return_dbus_error(invocation, name, "Private fixture outcome.");
    [StructLayout(LayoutKind.Sequential)]
    struct VTable
    {
        public delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint, void> Method;
        public nint Get;
        public nint Set;
        public fixed long Padding[8];
    }
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_new_for_address_sync([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int flags, nint observer, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint args, nint type, int flags, int timeout, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_dbus_node_info_new_for_xml([MarshalAs(UnmanagedType.LPUTF8Str)] string xml, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_dbus_node_info_lookup_interface(nint node, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(S.Gio)]
    static extern uint g_dbus_connection_register_object(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint iface, ref VTable table, nint context, nint destroy, out nint error);
    [DllImport(S.Gio)]
    static extern int g_dbus_connection_unregister_object(nint connection, uint registration);
    [DllImport(S.Gio)]
    static extern void g_dbus_node_info_unref(nint node);
    [DllImport(S.Gio)]
    static extern void g_dbus_method_invocation_return_value(nint invocation, nint value);
    [DllImport(S.Gio)]
    static extern void g_dbus_method_invocation_return_dbus_error(nint invocation, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
    [DllImport(S.Gio)]
    static extern void g_dbus_method_invocation_return_value_with_unix_fd_list(nint invocation, nint value, nint descriptors);
    [DllImport(S.Gio)]
    static extern int g_dbus_connection_emit_signal(nint connection, nint destination, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint value, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_unix_fd_list_new();
    [DllImport(S.Gio)]
    static extern int g_unix_fd_list_append(nint list, int fd, out nint error);
    [DllImport(S.Gio)]
    static extern int g_dbus_connection_close_sync(nint connection, nint cancellation, out nint error);
    [DllImport(S.GObject)]
    internal static extern void g_object_unref(nint value);
    [DllImport(S.GLib)]
    static extern nint g_main_context_new();
    [DllImport(S.GLib)]
    static extern void g_main_context_push_thread_default(nint context);
    [DllImport(S.GLib)]
    static extern void g_main_context_pop_thread_default(nint context);
    [DllImport(S.GLib)]
    static extern void g_main_context_unref(nint context);
    [DllImport(S.GLib)]
    static extern nint g_main_loop_new(nint context, int running);
    [DllImport(S.GLib)]
    static extern void g_main_loop_run(nint loop);
    [DllImport(S.GLib)]
    static extern void g_main_loop_quit(nint loop);
    [DllImport(S.GLib)]
    static extern void g_main_loop_unref(nint loop);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_uint32(uint value);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_handle(int value);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_fixed_array(nint type, byte* data, nuint count, nuint size);
    [DllImport(S.GLib)]
    static extern byte* g_variant_get_fixed_array(nint value, out nuint count, nuint size);
    [DllImport(S.GLib)]
    static extern nint g_variant_type_new([MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(S.GLib)]
    static extern void g_variant_type_free(nint type);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_array(nint type, nint* children, nuint count);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_double(double value);
    [DllImport(S.GLib)]
    static extern void g_error_free(nint error);
    [DllImport("libc")]
    static extern int pipe(int* descriptors);
    [DllImport("libc")]
    static extern nint read(int fd, byte* value, nuint length);
    [DllImport("libc")]
    static extern int close(int fd);
}
