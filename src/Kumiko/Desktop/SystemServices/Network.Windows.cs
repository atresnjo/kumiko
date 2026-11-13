using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

internal sealed unsafe class WindowsNetworkBackend : INetworkStatusBackend
{
    GCHandle context;
    nint registration;
    Action<NetworkStatus>? changed;
    WindowsLegacyNetworkBackend? fallback;
    public void Start(Action<NetworkStatus> callback)
    {
        // Connectivity hints arrived in Windows 10 2004; earlier supported versions re-query Network List Manager on events.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            fallback = new();
            fallback.Start(callback);
            return;
        }
        changed = callback;
        context = GCHandle.Alloc(this);
        if (NotifyNetworkConnectivityHintChange(&Changed, GCHandle.ToIntPtr(context), 1, out registration) != 0)
            throw new PlatformNotSupportedException("Native network hints are unavailable.");
    }
    [UnmanagedCallersOnly]
    static void Changed(nint data, Hint hint)
    {
        try
        {
            var owner = (WindowsNetworkBackend)GCHandle.FromIntPtr(data).Target!;
            owner.changed?.Invoke(new(hint.Level switch
            {
                1 => Connectivity.Offline,
                2 => Connectivity.Local,
                3 => Connectivity.Online,
                4 => Connectivity.CaptivePortal,
                _ => Connectivity.Unknown
            }, hint.Cost == 0 ? null : hint.Cost != 1 || hint.Roaming != 0 || hint.Over != 0, hint.Near != 0 || hint.Over != 0));
        }
        catch
        {
            // No managed exception may cross the IP Helper callback boundary.
        }
    }
    public void Dispose()
    {
        fallback?.Dispose();
        fallback = null;
        if (registration != 0)
        {
            CancelMibChangeNotify2(registration); // Waits for in-flight callbacks; never called inside a native callback.
            registration = 0;
        }
        changed = null;
        if (context.IsAllocated)
            context.Free();
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Hint
    {
        public int Level;
        public int Cost;
        public byte Near;
        public byte Over;
        public byte Roaming;
    }
    [DllImport("iphlpapi.dll")]
    static extern uint NotifyNetworkConnectivityHintChange(delegate* unmanaged<nint, Hint, void> callback, nint context, byte initial, out nint handle);
    [DllImport("iphlpapi.dll")]
    static extern uint CancelMibChangeNotify2(nint handle);
}
