using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

internal sealed unsafe class LinuxNetworkBackend : INetworkStatusBackend
{
    nint monitor;
    readonly List<nuint> signals = new();
    Action<NetworkStatus>? changed;
    public void Start(Action<NetworkStatus> callback)
    {
        changed = callback;
        monitor = g_network_monitor_get_default();
        if (monitor == 0)
            throw new PlatformNotSupportedException("GIO network monitoring is unavailable.");
        g_object_ref(monitor);
        var name = Marshal.PtrToStringUTF8(g_type_name_from_instance(monitor)) ?? "";
        if (name == "GNetworkMonitorBase")
            throw new PlatformNotSupportedException("No native GIO network monitor backend is available.");
        Connect("network-changed");
        Connect("notify::network-metered");
        Connect("notify::connectivity");
        Publish();
    }
    void Connect(string signal)
    {
        var context = GCHandle.Alloc(this);
        nuint id = g_signal_connect_data(monitor, signal, &Changed, GCHandle.ToIntPtr(context), &Destroyed, 0);
        if (id == 0)
        {
            context.Free();
            throw new PlatformNotSupportedException("GIO network signal registration failed.");
        }
        signals.Add(id);
    }
    void Publish()
    {
        var type = Marshal.PtrToStringUTF8(g_type_name_from_instance(monitor)) ?? "";
        bool cost = type.Contains("NM", StringComparison.Ordinal) || type.Contains("Portal", StringComparison.Ordinal);
        changed?.Invoke(new(g_network_monitor_get_network_available(monitor) == 0 ? Connectivity.Offline : g_network_monitor_get_connectivity(monitor) switch
        {
            1 or 2 => Connectivity.Local,
            3 => Connectivity.CaptivePortal,
            4 => Connectivity.Online,
            _ => Connectivity.Unknown
        }, cost ? g_network_monitor_get_network_metered(monitor) != 0 : null));
    }
    [UnmanagedCallersOnly]
    static void Changed(nint obj, nint value, nint data)
    {
        try
        {
            ((LinuxNetworkBackend)GCHandle.FromIntPtr(data).Target!).Publish();
        }
        catch
        {
        }
    }
    [UnmanagedCallersOnly]
    static void Destroyed(nint data, nint closure) => GCHandle.FromIntPtr(data).Free();
    public void Dispose()
    {
        if (monitor == 0)
            return;
        foreach (var signal in signals)
            g_signal_handler_disconnect(monitor, signal);
        signals.Clear();
        changed = null;
        g_object_unref(monitor);
        monitor = 0;
    }
    [DllImport(SecretVariant.Gio)]
    static extern nint g_network_monitor_get_default();
    [DllImport(SecretVariant.Gio)]
    static extern int g_network_monitor_get_network_available(nint monitor);
    [DllImport(SecretVariant.Gio)]
    static extern int g_network_monitor_get_network_metered(nint monitor);
    [DllImport(SecretVariant.Gio)]
    static extern int g_network_monitor_get_connectivity(nint monitor);
    [DllImport(SecretVariant.GObject)]
    static extern nint g_object_ref(nint value);
    [DllImport(SecretVariant.GObject)]
    static extern void g_object_unref(nint value);
    [DllImport(SecretVariant.GObject)]
    static extern nint g_type_name_from_instance(nint value);
    [DllImport(SecretVariant.GObject)]
    static extern nuint g_signal_connect_data(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, delegate* unmanaged<nint, nint, nint, void> callback, nint data, delegate* unmanaged<nint, nint, void> destroy, int flags);
    [DllImport(SecretVariant.GObject)]
    static extern void g_signal_handler_disconnect(nint obj, nuint id);
}
