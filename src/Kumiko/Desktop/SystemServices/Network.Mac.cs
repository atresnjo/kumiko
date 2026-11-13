using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

internal sealed class MacNetworkBackend : INetworkStatusBackend
{
    const string Network = "/System/Library/Frameworks/Network.framework/Network";
    nint monitor;
    NativeBlock? update;
    public void Start(Action<NetworkStatus> changed)
    {
        monitor = nw_path_monitor_create();
        if (monitor == 0)
            throw new PlatformNotSupportedException("Network path monitoring is unavailable.");
        update = NativeBlock.Pointer(path => changed(new(nw_path_get_status(path) switch
        {
            1 => Connectivity.Online,
            2 => Connectivity.Offline,
            _ => Connectivity.Unknown
        }, nw_path_is_expensive(path) != 0, nw_path_is_constrained(path) != 0)));
        nw_path_monitor_set_update_handler(monitor, update.Handle);
        nw_path_monitor_set_queue(monitor, mainQueue);
        nw_path_monitor_start(monitor);
    }
    public void Dispose()
    {
        if (monitor != 0)
        {
            // All delivery uses the owner main queue. The monitor retains its copied block until cancellation drains.
            nw_path_monitor_cancel(monitor);
            nw_release(monitor);
            monitor = 0;
        }
        update?.Dispose();
        update = null;
    }
    [DllImport(Network)]
    static extern nint nw_path_monitor_create();
    [DllImport(Network)]
    static extern void nw_path_monitor_set_update_handler(nint monitor, nint block);
    [DllImport(Network)]
    static extern void nw_path_monitor_set_queue(nint monitor, nint queue);
    [DllImport(Network)]
    static extern void nw_path_monitor_start(nint monitor);
    [DllImport(Network)]
    static extern void nw_path_monitor_cancel(nint monitor);
    [DllImport(Network)]
    static extern int nw_path_get_status(nint path);
    [DllImport(Network)]
    static extern byte nw_path_is_expensive(nint path);
    [DllImport(Network)]
    static extern byte nw_path_is_constrained(nint path);
    [DllImport(Network)]
    static extern void nw_release(nint value);
    static readonly nint mainQueue = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/system/libdispatch.dylib"), "_dispatch_main_q");
}
