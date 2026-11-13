using System.Runtime.InteropServices;
using S = Kumiko.Desktop.SecretVariant;

namespace Kumiko.Desktop;

internal interface ILinuxSecretSession : IDisposable
{
    nint Connection
    {
        get;
    }
    string? Algorithms
    {
        get;
    }
    string Path
    {
        get;
    }
    S Encode(ReadOnlySpan<byte> secret);
    SecretBuffer? Decode(S encoded);
}

/// <summary>Per-operation encrypted libsecret session. Never selects SecretBackend's optional file/portal implementation.</summary>
internal sealed unsafe class LinuxSecretSession : ILinuxSecretSession
{
    const string Secret = "libsecret-1.so.0";
    readonly nint service;
    public nint Connection => g_dbus_proxy_get_connection(service);
    public string? Algorithms => Marshal.PtrToStringUTF8(secret_service_get_session_algorithms(service));
    public string Path => Marshal.PtrToStringUTF8(secret_service_get_session_dbus_path(service)) ?? throw new InvalidOperationException("Missing secret session.");
    internal LinuxSecretSession(nint cancellation)
    {
        service = secret_service_open_sync(secret_service_get_type(), "org.freedesktop.secrets", 2, cancellation, out var error);
        if (error != 0)
        {
            if (service != 0)
                g_object_unref(service);
            SecretBusException.Throw(error);
        }
        if (service == 0)
            throw new SecretBusException(CredentialStatus.Unavailable);
    }
    public S Encode(ReadOnlySpan<byte> secret)
    {
        byte empty = 0;
        nint value;
        fixed (byte* bytes = secret)
            value = secret_value_new(secret.IsEmpty ? &empty : bytes, secret.Length, "application/octet-stream");
        if (value == 0)
            throw new OutOfMemoryException();
        try
        {
            return new S(secret_service_encode_dbus_secret(service, value));
        }
        finally
        {
            secret_value_unref(value);
        }
    }
    public SecretBuffer? Decode(S encoded)
    {
        var value = secret_service_decode_dbus_secret(service, encoded.Handle);
        if (value == 0)
            return null;
        try
        {
            var bytes = secret_value_get(value, out var length);
            return length > CredentialStore.MaximumSecretBytes || (bytes == null && length != 0) ? null :
                new(new ReadOnlySpan<byte>(bytes, (int)length));
        }
        finally
        {
            secret_value_unref(value);
        }
    }
    public void Dispose()
    {
        try
        {
            using var args = S.Tuple();
            var reply = g_dbus_connection_call_sync(Connection, "org.freedesktop.secrets", Path,
                "org.freedesktop.Secret.Session", "Close", args.Handle, 0, 1, 1000, 0, out var error);
            if (reply != 0)
            {
                using var result = S.Adopt(reply);
            }
            if (error != 0)
                g_error_free(error);
        }
        finally
        {
            g_object_unref(service);
        }
    }
    [DllImport(Secret)]
    static extern nint secret_service_get_session_dbus_path(nint service);
    [DllImport(Secret)]
    static extern nint secret_service_get_session_algorithms(nint service);
    [DllImport(Secret)]
    static extern nuint secret_service_get_type();
    [DllImport(Secret)]
    static extern nint secret_service_open_sync(nuint type, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, int flags, nint cancellation, out nint error);
    [DllImport(Secret)]
    static extern nint secret_service_encode_dbus_secret(nint service, nint value);
    [DllImport(Secret)]
    static extern nint secret_service_decode_dbus_secret(nint service, nint value);
    [DllImport(Secret)]
    static extern nint secret_value_new(byte* bytes, nint length, [MarshalAs(UnmanagedType.LPUTF8Str)] string type);
    [DllImport(Secret)]
    static extern byte* secret_value_get(nint value, out nuint length);
    [DllImport(Secret)]
    static extern void secret_value_unref(nint value);
    [DllImport(S.Gio)]
    static extern nint g_dbus_proxy_get_connection(nint proxy);
    [DllImport(S.GObject)]
    static extern void g_object_unref(nint value);
    [DllImport(S.GLib)]
    static extern void g_error_free(nint error);
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint args, nint replyType, int flags, int timeout, nint cancellation, out nint error);
}
