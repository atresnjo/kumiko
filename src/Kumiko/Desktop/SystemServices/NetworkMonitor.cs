namespace Kumiko.Desktop;

public enum Connectivity
{
    Unknown, Offline, Local, CaptivePortal, Online
}
/// <summary>OS path/connectivity hints, never proof of reachability of an application server. Null means unknown.</summary>
public sealed record NetworkStatus(Connectivity Connectivity = Connectivity.Unknown, bool? Expensive = null, bool? Constrained = null);

internal interface INetworkStatusBackend : IDisposable
{
    void Start(Action<NetworkStatus> changed);
}

/// <summary>Native change subscription. Events and Current belong to the App thread; Dispose suppresses queued delivery.</summary>
public sealed class NetworkMonitor : IDisposable
{
    readonly App app;
    readonly INetworkStatusBackend backend;
    NetworkStatus current = new();
    bool disposed;
    public event Action<NetworkStatus>? Changed;
    public NetworkStatus Current
    {
        get
        {
            Check();
            return current;
        }
    }
    public NetworkMonitor(App app) : this(app, null)
    {
    }
    internal NetworkMonitor(App app, INetworkStatusBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.CheckThread();
        this.app = app;
        this.backend = backend ?? (OperatingSystem.IsMacOS() ? new MacNetworkBackend() : OperatingSystem.IsWindows() ? new WindowsNetworkBackend() : new LinuxNetworkBackend());
        try
        {
            this.backend.Start(NativeChanged);
            app.Disposing += Dispose;
        }
        catch
        {
            this.backend.Dispose();
            throw;
        }
    }
    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    void NativeChanged(NetworkStatus status)
    {
        try
        {
            app.Dispatch(() =>
            {
                if (disposed || current == status)
                    return;
                current = status;
                Changed?.Invoke(status);
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }
    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        app.Disposing -= Dispose;
        backend.Dispose();
        Changed = null;
    }
}
