using System.Runtime.InteropServices;
using S = Kumiko.Desktop.SecretVariant;

namespace Kumiko.Desktop;

internal sealed unsafe class LinuxPowerBus : IDisposable
{
    readonly nint connection;
    readonly List<uint> subscriptions = new();
    readonly Action invalidated;
    readonly Action<SessionChange> session;
    readonly CancellationToken cancellation;
    volatile string? sessionPath;
    bool upower;
    bool login;
    int disposed;
    GioSignalContext? signalContext;
    static int activeContexts;
    internal static int ActiveCallbackContexts => Volatile.Read(ref activeContexts);
    internal PowerCapabilities Capabilities => (login ? PowerCapabilities.SystemKeepAwake | PowerCapabilities.SuspendResume : 0) |
        (sessionPath is not null ? PowerCapabilities.LockUnlock : 0) | (upower ? PowerCapabilities.Battery : 0);

    internal LinuxPowerBus(Action invalidated, Action<SessionChange> session, CancellationToken cancellation, string? address = null)
    {
        this.invalidated = invalidated;
        this.session = session;
        this.cancellation = cancellation;
        using var native = new GioCancellation(cancellation);
        connection = address is null ? g_bus_get_sync(1, native.Handle, out var error) :
            g_dbus_connection_new_for_address_sync(address, 1 | 8, 0, native.Handle, out error);
        SecretBusException.Throw(error);
        if (connection == 0)
            throw new IOException("The native power service bus is unavailable.");
    }
    public PowerStatus Read()
    {
        try
        {
            using var args = S.Tuple();
            using var device = Call("org.freedesktop.UPower", "/org/freedesktop/UPower", "org.freedesktop.UPower", "GetDisplayDevice", args);
            using var path = device.Child(0);
            using var properties = GetAll("org.freedesktop.UPower", path.Text, "org.freedesktop.UPower.Device");
            using var present = Property(properties, "IsPresent");
            using var type = Property(properties, "Type");
            using var percent = Property(properties, "Percentage");
            using var state = Property(properties, "State");
            using var root = GetAll("org.freedesktop.UPower", "/org/freedesktop/UPower", "org.freedesktop.UPower");
            using var battery = Property(root, "OnBattery");
            upower = true;
            bool? exists = present is null || type is null ? null : present.Boolean && type.UInt32 == 2;
            return new(exists, battery?.Boolean, exists == true && percent is not null && double.IsFinite(percent.Double) ? Math.Clamp(percent.Double, 0, 100) : null,
                exists == true && state is not null && state.UInt32 != 0 ? state.UInt32 is 1 or 5 : null);
        }
        catch (SecretBusException)
        {
            upower = false;
            return new();
        }
    }
    S GetAll(string bus, string path, string iface)
    {
        using var name = S.String(iface);
        using var args = S.Tuple(name);
        return Call(bus, path, "org.freedesktop.DBus.Properties", "GetAll", args);
    }
    static S? Property(S result, string key)
    {
        using var dictionary = result.Child(0);
        for (nuint i = 0; i < dictionary.Count; i++)
        {
            using var entry = dictionary.Child(i);
            using var name = entry.Child(0);
            if (name.Text != key)
                continue;
            using var box = entry.Child(1);
            return box.Unbox();
        }
        return null;
    }
    internal void Start() => signalContext = new(SubscribeOnContext, Unsubscribe);
    void SubscribeOnContext()
    {
        try
        {
            using var name = S.String("org.freedesktop.login1");
            using var args = S.Tuple(name);
            using var owner = Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", args);
            using var present = owner.Child(0);
            login = present.Boolean;
            if (login)
            {
                Subscribe("org.freedesktop.login1", "org.freedesktop.login1.Manager", "PrepareForSleep", "/org/freedesktop/login1");
                using var pid = UInt32((uint)Environment.ProcessId);
                using var sessionArgs = S.Tuple(pid);
                using var result = Call("org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "GetSessionByPID", sessionArgs);
                using var path = result.Child(0);
                sessionPath = path.Text;
                Subscribe("org.freedesktop.login1", "org.freedesktop.DBus.Properties", "PropertiesChanged", sessionPath);
            }
        }
        catch (SecretBusException)
        {
            sessionPath = null; // A system service process need not belong to a graphical login session.
        }
        if (upower)
            Subscribe("org.freedesktop.UPower", "org.freedesktop.DBus.Properties", "PropertiesChanged", null);
    }
    void Subscribe(string bus, string iface, string member, string? path)
    {
        var context = GCHandle.Alloc(this);
        Interlocked.Increment(ref activeContexts);
        uint id = g_dbus_connection_signal_subscribe(connection, bus, iface, member, path, null, 0, &Signal, GCHandle.ToIntPtr(context), &Destroyed);
        if (id == 0)
        {
            context.Free();
            Interlocked.Decrement(ref activeContexts);
            throw new PlatformNotSupportedException("D-Bus power subscription failed.");
        }
        subscriptions.Add(id);
    }
    [UnmanagedCallersOnly]
    static void Signal(nint connection, nint sender, nint path, nint iface, nint signal, nint parameters, nint data)
    {
        try
        {
            var owner = (LinuxPowerBus)GCHandle.FromIntPtr(data).Target!;
            if (Volatile.Read(ref owner.disposed) != 0)
                return;
            var name = Marshal.PtrToStringUTF8(signal);
            if (name == "PrepareForSleep")
            {
                // Borrowed callback parameters: take our own reference.
                using var args = new S(parameters);
                using var sleeping = args.Child(0);
                owner.session.Invoke(sleeping.Boolean ? SessionChange.Suspending : SessionChange.Resumed);
            }
            else if (Marshal.PtrToStringUTF8(path) == owner.sessionPath)
            {
                using var args = new S(parameters);
                using var fields = args.Child(1);
                for (nuint i = 0; i < fields.Count; i++)
                {
                    using var entry = fields.Child(i);
                    using var key = entry.Child(0);
                    if (key.Text != "LockedHint")
                        continue;
                    using var box = entry.Child(1);
                    using var locked = box.Unbox();
                    owner.session.Invoke(locked.Boolean ? SessionChange.Locked : SessionChange.Unlocked);
                }
            }
            else
                owner.invalidated();
        }
        catch
        {
        }
    }
    [UnmanagedCallersOnly]
    static void Destroyed(nint data)
    {
        GCHandle.FromIntPtr(data).Free();
        Interlocked.Decrement(ref activeContexts);
    }
    internal IDisposable KeepAwake(KeepAwakeKind kind, string reason, CancellationToken token)
    {
        if (kind != KeepAwakeKind.System || !login)
            throw new PlatformNotSupportedException("Linux requires login1 for system inhibition; display inhibition is unsupported.");
        using var what = S.String("idle");
        using var who = S.String("Kumiko");
        using var why = S.String(reason);
        using var mode = S.String("block");
        using var args = S.Tuple(what, who, why, mode);
        using var native = new GioCancellation(token);
        var result = g_dbus_connection_call_with_unix_fd_list_sync(connection, "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "Inhibit", args.Handle, 0, 0, 5000, 0, out var descriptors, native.Handle, out var error);
        try
        {
            using var response = result == 0 ? null : S.Adopt(result);
            SecretBusException.Throw(error);
            using var index = response!.Child(0);
            int fd = g_unix_fd_list_get(descriptors, g_variant_get_handle(index.Handle), out error);
            SecretBusException.Throw(error);
            if (fd < 0)
                throw new IOException("The native inhibitor returned no descriptor.");
            return new Inhibitor(fd);
        }
        finally
        {
            if (descriptors != 0)
                g_object_unref(descriptors);
        }
    }
    sealed class Inhibitor(int descriptor) : IDisposable
    {
        int fd = descriptor;
        public void Dispose()
        {
            int owned = Interlocked.Exchange(ref fd, -1);
            if (owned >= 0)
                close(owned);
        }
    }
    S Call(string bus, string path, string iface, string method, S args)
    {
        using var native = new GioCancellation(cancellation);
        var result = g_dbus_connection_call_sync(connection, bus, path, iface, method, args.Handle, 0, 0, 3000, native.Handle, out var error);
        SecretBusException.Throw(error);
        return S.Adopt(result);
    }
    static S UInt32(uint value) => new(g_variant_new_uint32(value));
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        signalContext?.Dispose();
        g_object_unref(connection);
    }
    void Unsubscribe()
    {
        foreach (uint id in subscriptions)
            g_dbus_connection_signal_unsubscribe(connection, id);
        subscriptions.Clear();
    }
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_new_for_address_sync([MarshalAs(UnmanagedType.LPUTF8Str)] string address, int flags, nint observer, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_bus_get_sync(int type, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_call_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, int flags, int timeout, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern uint g_dbus_connection_signal_subscribe(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string sender, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string member, [MarshalAs(UnmanagedType.LPUTF8Str)] string? path, [MarshalAs(UnmanagedType.LPUTF8Str)] string? arg0, int flags, delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, void> callback, nint data, delegate* unmanaged<nint, void> destroy);
    [DllImport(S.Gio)]
    static extern void g_dbus_connection_signal_unsubscribe(nint connection, uint id);
    [DllImport(S.Gio)]
    static extern nint g_dbus_connection_call_with_unix_fd_list_sync(nint connection, [MarshalAs(UnmanagedType.LPUTF8Str)] string bus, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string iface, [MarshalAs(UnmanagedType.LPUTF8Str)] string method, nint parameters, nint replyType, int flags, int timeout, nint fdList, out nint outFdList, nint cancellation, out nint error);
    [DllImport(S.Gio)]
    static extern int g_unix_fd_list_get(nint list, int index, out nint error);
    [DllImport(S.GObject)]
    static extern void g_object_unref(nint value);
    [DllImport(S.GLib)]
    static extern nint g_variant_new_uint32(uint value);
    [DllImport(S.GLib)]
    static extern int g_variant_get_handle(nint value);
    [DllImport("libc")]
    static extern int close(int descriptor);
}
