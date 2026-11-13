using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kumiko.Desktop;
using S = Kumiko.Desktop.SecretVariant;

sealed unsafe partial class PrivateServiceBus : IDisposable
{
    const string DevicePath = "/fixture/battery";
    const string SecretPath = "/fixture/item";
    const string SessionPath = "/fixture/session";
    readonly Process daemon;
    readonly Thread thread;
    readonly Thread? clientThread;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly List<uint> registrations = new();
    readonly ConcurrentQueue<nint> reads = new();
    readonly ConcurrentQueue<nint> inhibits = new();
    readonly ConcurrentQueue<Task> pipeClosures = new();
    readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    readonly string directory;
    nint connection;
    nint loop;
    nint clientLoop;
    GCHandle context;
    internal string Address
    {
        get;
    }
    internal readonly ManualResetEventSlim ReadEntered = new();
    internal readonly ManualResetEventSlim InhibitEntered = new();
    internal volatile bool DelayReads;
    internal volatile bool DelayInhibits;
    internal volatile bool DenyInhibits;
    internal volatile bool OnBattery;
    int secretState;
    internal volatile int SecretRace; // 1 locks, 2 disappears after SearchItems.
    internal string[] LastInhibit = [];
    internal int InhibitRequests;
    internal readonly ConcurrentQueue<Exception> Errors = new();

    internal PrivateServiceBus(string output)
    {
        directory = Path.Combine(Path.GetFullPath(output), "private-bus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "bus.conf");
        File.WriteAllText(config, "<busconfig><type>session</type><listen>unix:tmpdir=/tmp</listen><auth>EXTERNAL</auth><policy context='default'><allow send_destination='*'/><allow receive_sender='*'/><allow own='*'/></policy></busconfig>");
        var start = new ProcessStartInfo("dbus-daemon") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--nofork");
        start.ArgumentList.Add("--print-address=1");
        start.ArgumentList.Add("--config-file=" + config);
        daemon = Process.Start(start) ?? throw new IOException("Starting private D-Bus failed.");
        Address = daemon.StandardOutput.ReadLine() ?? throw new IOException("Private bus returned no address.");
        if (OperatingSystem.IsMacOS())
        {
            clientLoop = g_main_loop_new(0, 0);
            clientThread = new(() => g_main_loop_run(clientLoop))
            {
                IsBackground = true
            };
            clientThread.Start();
        }
        thread = new(Serve)
        {
            IsBackground = true
        };
        thread.Start();
        ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }
    void Serve()
    {
        nint nativeContext = g_main_context_new();
        g_main_context_push_thread_default(nativeContext);
        nint node = 0;
        try
        {
            connection = Connect(Address);
            foreach (var bus in new[] { "org.freedesktop.UPower", "org.freedesktop.login1", "org.freedesktop.secrets" })
            {
                using var name = S.String(bus);
                using var flags = UInt32(0);
                using var args = S.Tuple(name, flags);
                nint reply = g_dbus_connection_call_sync(connection, "org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "RequestName", args.Handle, 0, 0, 3000, 0, out var error);
                SecretBusException.Throw(error);
                using var result = S.Adopt(reply);
            }
            node = g_dbus_node_info_new_for_xml(Xml, out var xmlError);
            SecretBusException.Throw(xmlError);
            context = GCHandle.Alloc(this);
            Register(node, "/org/freedesktop/UPower", "org.freedesktop.UPower");
            Register(node, "/org/freedesktop/UPower", "org.freedesktop.DBus.Properties");
            Register(node, DevicePath, "org.freedesktop.DBus.Properties");
            Register(node, "/org/freedesktop/login1", "org.freedesktop.login1.Manager");
            Register(node, SessionPath, "org.freedesktop.DBus.Properties");
            Register(node, "/org/freedesktop/secrets", "org.freedesktop.Secret.Service");
            Register(node, SecretPath, "org.freedesktop.Secret.Item");
            loop = g_main_loop_new(nativeContext, 0);
            ready.TrySetResult();
            g_main_loop_run(loop);
        }
        catch (Exception error)
        {
            Errors.Enqueue(error);
            ready.TrySetException(error);
        }
        finally
        {
            while (reads.TryDequeue(out var pending))
                Error(pending, "org.freedesktop.DBus.Error.Disconnected");
            while (inhibits.TryDequeue(out var pending))
                Error(pending, "org.freedesktop.DBus.Error.Disconnected");
            foreach (uint id in registrations)
                g_dbus_connection_unregister_object(connection, id);
            if (context.IsAllocated)
                context.Free();
            if (node != 0)
                g_dbus_node_info_unref(node);
            if (connection != 0)
            {
                g_dbus_connection_close_sync(connection, 0, out var error);
                if (error != 0)
                    g_error_free(error);
                g_object_unref(connection);
            }
            if (loop != 0)
                g_main_loop_unref(loop);
            g_main_context_pop_thread_default(nativeContext);
            g_main_context_unref(nativeContext);
        }
    }
    void Register(nint node, string path, string iface)
    {
        var table = new VTable { Method = &Method };
        uint id = g_dbus_connection_register_object(connection, path, g_dbus_node_info_lookup_interface(node, iface), ref table, GCHandle.ToIntPtr(context), 0, out var error);
        SecretBusException.Throw(error);
        if (id == 0)
            throw new IOException("Exporting a private fixture interface failed.");
        registrations.Add(id);
    }
    [UnmanagedCallersOnly]
    static void Method(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data)
    {
        var fixture = (PrivateServiceBus)GCHandle.FromIntPtr(data).Target!;
        try
        {
            using var args = new S(parameters);
            fixture.Respond(Marshal.PtrToStringUTF8(path)!, Marshal.PtrToStringUTF8(method)!, args, invocation);
        }
        catch (Exception error)
        {
            fixture.Errors.Enqueue(error);
            Error(invocation, "org.freedesktop.DBus.Error.Failed");
        }
    }
    void Respond(string path, string method, S args, nint invocation)
    {
        if (method == "GetDisplayDevice" || method == "GetSessionByPID")
        {
            using var value = S.Path(method == "GetDisplayDevice" ? DevicePath : SessionPath);
            using var result = S.Tuple(value);
            Reply(invocation, result);
        }
        else if (method == "GetAll")
        {
            if (path == DevicePath && DelayReads)
            {
                reads.Enqueue(invocation); // Own invocation until reply, including after a client cancels.
                ReadEntered.Set();
            }
            else
                ReplyProperties(invocation, path);
        }
        else if (method == "Inhibit")
        {
            Interlocked.Increment(ref InhibitRequests);
            LastInhibit = Enumerable.Range(0, 4).Select(i =>
            {
                using var field = args.Child((nuint)i);
                return field.Text;
            }).ToArray();
            if (DelayInhibits)
                inhibits.Enqueue(invocation);
            else
                ReplyInhibit(invocation);
            InhibitEntered.Set();
        }
        else if (method == "SearchItems")
        {
            using var item = S.Path(SecretPath);
            using var unlocked = Paths(item);
            using var locked = Paths();
            using var result = S.Tuple(unlocked, locked);
            secretState = SecretRace;
            Reply(invocation, result);
        }
        else if (method == "GetSecret")
        {
            if (secretState != 0)
            {
                Error(invocation, secretState == 1 ? "org.freedesktop.Secret.Error.IsLocked" : "org.freedesktop.Secret.Error.NoSuchObject");
                return;
            }
            using var session = S.Path(SessionPath);
            using var parameters = Bytes([]);
            using var value = Bytes(secret);
            using var type = S.String("application/octet-stream");
            using var encoded = S.Tuple(session, parameters, value, type);
            using var result = S.Tuple(encoded);
            Reply(invocation, result);
        }
        else
            Error(invocation, "org.freedesktop.DBus.Error.UnknownMethod");
    }
    void ReplyProperties(nint invocation, string path)
    {
        using var present = S.Bool(true);
        using var type = UInt32(2);
        using var percentage = new S(g_variant_new_double(75));
        using var state = UInt32(2);
        using var battery = S.Bool(OnBattery);
        using var presentBox = S.Box(present);
        using var typeBox = S.Box(type);
        using var percentageBox = S.Box(percentage);
        using var stateBox = S.Box(state);
        using var batteryBox = S.Box(battery);
        using var dictionary = path == DevicePath ? S.Dictionary("{sv}", ("IsPresent", presentBox), ("Type", typeBox), ("Percentage", percentageBox), ("State", stateBox)) :
            S.Dictionary("{sv}", ("OnBattery", batteryBox));
        using var result = S.Tuple(dictionary);
        Reply(invocation, result);
    }
    internal void ReleaseReads()
    {
        DelayReads = false;
        while (reads.TryDequeue(out nint pending))
            ReplyProperties(pending, DevicePath);
    }
    internal void ReleaseInhibits()
    {
        DelayInhibits = false;
        while (inhibits.TryDequeue(out nint pending))
            ReplyInhibit(pending);
    }
    void ReplyInhibit(nint invocation)
    {
        if (DenyInhibits)
        {
            Error(invocation, "org.freedesktop.DBus.Error.AccessDenied");
            return;
        }
        int* fds = stackalloc int[2];
        if (pipe(fds) != 0)
            throw new IOException("Creating private inhibitor witness failed.");
        int reader = fds[0];
        pipeClosures.Enqueue(Task.Run(() =>
        {
            try
            {
                byte value;
                if (read(reader, &value, 1) != 0)
                    throw new IOException("Unexpected private pipe payload.");
            }
            finally
            {
                close(reader);
            }
        }));
        nint list = g_unix_fd_list_new();
        try
        {
            int index = g_unix_fd_list_append(list, fds[1], out var error);
            SecretBusException.Throw(error);
            using var handle = Handle(index);
            using var result = S.Tuple(handle);
            g_dbus_method_invocation_return_value_with_unix_fd_list(invocation, result.Handle, list);
        }
        finally
        {
            close(fds[1]);
            g_object_unref(list);
        }
    }
    internal Task NextClosure() => pipeClosures.TryDequeue(out var witness) ? witness : throw new InvalidOperationException("No private inhibitor witness.");
    internal void EmitPowerChange()
    {
        using var iface = S.String("org.freedesktop.UPower.Device");
        using var changed = S.Dictionary("{sv}");
        using var invalidated = new S(g_variant_new_strv(0, 0));
        using var args = S.Tuple(iface, changed, invalidated);
        g_dbus_connection_emit_signal(connection, 0, DevicePath, "org.freedesktop.DBus.Properties", "PropertiesChanged", args.Handle, out var error);
        SecretBusException.Throw(error);
    }
    internal void StopDefaultCallbacks()
    {
        if (clientThread is not null)
        {
            g_main_loop_quit(clientLoop);
            if (!clientThread.Join(TimeSpan.FromSeconds(3)))
                throw new TimeoutException("Private default callback loop did not stop.");
        }
    }
    public void Dispose()
    {
        g_main_loop_quit(loop);
        if (!thread.Join(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Private service loop did not stop.");
        if (clientThread is not null)
        {
            g_main_loop_quit(clientLoop);
            clientThread.Join(TimeSpan.FromSeconds(5));
            g_main_loop_unref(clientLoop);
        }
        daemon.Kill();
        daemon.WaitForExit();
        daemon.Dispose();
        ReadEntered.Dispose();
        InhibitEntered.Dispose();
        CryptographicOperations.ZeroMemory(secret);
        Directory.Delete(directory, true);
    }
    [DllImport(S.GLib)]
    static extern nint g_variant_new_strv(nint strings, nint count);
    const string Xml = """
        <node>
          <interface name='org.freedesktop.UPower'><method name='GetDisplayDevice'><arg type='o' direction='out'/></method></interface>
          <interface name='org.freedesktop.DBus.Properties'><method name='GetAll'><arg type='s' direction='in'/><arg type='a{sv}' direction='out'/></method></interface>
          <interface name='org.freedesktop.login1.Manager'>
            <method name='GetSessionByPID'><arg type='u' direction='in'/><arg type='o' direction='out'/></method>
            <method name='Inhibit'><arg type='s' direction='in'/><arg type='s' direction='in'/><arg type='s' direction='in'/><arg type='s' direction='in'/><arg type='h' direction='out'/></method>
          </interface>
          <interface name='org.freedesktop.Secret.Service'><method name='SearchItems'><arg type='a{ss}' direction='in'/><arg type='ao' direction='out'/><arg type='ao' direction='out'/></method></interface>
          <interface name='org.freedesktop.Secret.Item'><method name='GetSecret'><arg type='o' direction='in'/><arg type='(oayays)' direction='out'/></method></interface>
        </node>
        """;
}
