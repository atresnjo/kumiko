namespace Kumiko.Desktop;

[Flags]
public enum PowerCapabilities
{
    None = 0,
    SystemKeepAwake = 1,
    DisplayKeepAwake = 2,
    SuspendResume = 4,
    LockUnlock = 8,
    Battery = 16,
    IdleTime = 32,
    SessionActivity = 64
}
public enum KeepAwakeKind
{
    System, Display
}
public enum SessionChange
{
    Suspending, Resumed, Locked, Unlocked, Activated, Deactivated
}
/// <summary>Nullable values represent information the native backend cannot supply; percentage is 0..100.</summary>
public sealed record PowerStatus(bool? BatteryPresent = null, bool? OnBattery = null, double? BatteryPercent = null, bool? Charging = null);

internal interface IPowerSessionBackend : IDisposable
{
    PowerCapabilities Capabilities
    {
        get;
    }
    PowerStatus Read();
    TimeSpan? IdleTime
    {
        get;
    }
    void Start(Action<PowerStatus> changed, Action<SessionChange> session);
    IDisposable KeepAwake(KeepAwakeKind kind, string reason);
}

/// <summary>Native power snapshots, session notifications and independent idle-sleep inhibition leases.</summary>
public sealed class PowerSession : IDisposable
{
    readonly App app;
    readonly IPowerSessionBackend backend;
    readonly HashSet<Lease> leases = new();
    readonly object leaseGate = new();
    PowerCapabilities capabilities;
    PowerStatus current;
    bool disposed;
    public event Action<PowerStatus>? Changed;
    public event Action<PowerCapabilities>? CapabilitiesChanged;
    public event Action<SessionChange>? SessionChanged;
    public PowerCapabilities Capabilities
    {
        get
        {
            Check();
            return capabilities;
        }
    }
    public PowerStatus Current
    {
        get
        {
            Check();
            return current;
        }
    }
    public TimeSpan? IdleTime
    {
        get
        {
            Check();
            return backend.IdleTime;
        }
    }
    public PowerSession(App app) : this(app, null)
    {
    }
    internal PowerSession(App app, IPowerSessionBackend? backend)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.CheckThread();
        this.app = app;
        injected = backend is not null;
        this.backend = backend ?? (OperatingSystem.IsMacOS() ? new MacPowerBackend() : OperatingSystem.IsWindows() ? new WindowsPowerBackend(app) : new LinuxPowerBackend());
        try
        {
            current = this.backend.Read();
            capabilities = this.backend.Capabilities;
            this.backend.Start(status => Dispatch(() =>
            {
                var available = this.backend.Capabilities;
                if (available != capabilities)
                {
                    capabilities = available;
                    CapabilitiesChanged?.Invoke(available);
                    if (disposed)
                        return;
                }
                if (current == status)
                    return;
                current = status;
                Changed?.Invoke(status);
            }), value => Dispatch(() => SessionChanged?.Invoke(value)));
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
    void Dispatch(Action action)
    {
        try
        {
            app.Dispatch(() =>
            {
                if (!disposed)
                    action();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }
    /// <summary>Prevents idle sleep as permitted by policy; cannot override explicit sleep, lid closure or shutdown.</summary>
    /// <exception cref="NotSupportedException">Linux requires KeepAwakeAsync to avoid blocking the App event loop.</exception>
    public IDisposable KeepAwake(KeepAwakeKind kind, string reason)
    {
        Validate(kind, reason);
        if (backend is IAsyncPowerSessionBackend)
            throw new NotSupportedException("This platform requires awaiting KeepAwakeAsync.");
        CheckCapability(kind);
        return Own(backend.KeepAwake(kind, reason), CancellationToken.None);
    }
    /// <summary>
    /// Acquires an idle-sleep lease without blocking the App event loop on desktop service replies.
    /// Starts on the App thread; await completion before relying on inhibition. Dispose the returned lease on the App thread.
    /// </summary>
    public Task<IDisposable> KeepAwakeAsync(KeepAwakeKind kind, string reason, CancellationToken cancellation = default)
    {
        Validate(kind, reason);
        if (cancellation.IsCancellationRequested)
            return Task.FromCanceled<IDisposable>(cancellation);
        if (backend is IAsyncPowerSessionBackend asynchronous)
            return Acquire(asynchronous.KeepAwakeAsync(kind, reason, cancellation), cancellation);
        CheckCapability(kind);
        return Task.FromResult(Own(backend.KeepAwake(kind, reason), cancellation));
    }
    async Task<IDisposable> Acquire(Task<IDisposable> pending, CancellationToken cancellation)
    {
        var native = await pending.ConfigureAwait(false);
        // Async backends return thread-independent native leases (Linux owned FDs).
        return Own(native, cancellation);
    }
    IDisposable Own(IDisposable native, CancellationToken cancellation)
    {
        lock (leaseGate)
        {
            if (disposed || cancellation.IsCancellationRequested)
            {
                native.Dispose();
                throw new OperationCanceledException(new CancellationToken(true));
            }
            var lease = new Lease(this, native);
            leases.Add(lease);
            return lease;
        }
    }
    void Validate(KeepAwakeKind kind, string reason)
    {
        Check();
        if (kind is not KeepAwakeKind.System and not KeepAwakeKind.Display)
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 256 || reason.Contains('\0'))
            throw new ArgumentException("Use a short non-secret reason without NUL characters.", nameof(reason));
        if (app.Options.Hidden && !injected)
            throw new InvalidOperationException("Hidden apps require an isolated keep-awake backend.");
    }
    void CheckCapability(KeepAwakeKind kind)
    {
        var capability = kind == KeepAwakeKind.System ? PowerCapabilities.SystemKeepAwake : PowerCapabilities.DisplayKeepAwake;
        if ((capabilities & capability) == 0)
            throw new PlatformNotSupportedException("This backend cannot inhibit the requested sleep kind.");
    }
    readonly bool injected;
    public void Dispose()
    {
        app.CheckThread();
        Lease[] owned;
        lock (leaseGate)
        {
            if (disposed)
                return;
            disposed = true;
            owned = leases.ToArray();
        }
        app.Disposing -= Dispose;
        foreach (var lease in owned)
            lease.Dispose();
        backend.Dispose();
        Changed = null;
        CapabilitiesChanged = null;
        SessionChanged = null;
    }
    sealed class Lease(PowerSession owner, IDisposable native) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            owner.app.CheckThread();
            if (disposed)
                return;
            disposed = true;
            lock (owner.leaseGate)
                owner.leases.Remove(this);
            native.Dispose();
        }
    }
}
