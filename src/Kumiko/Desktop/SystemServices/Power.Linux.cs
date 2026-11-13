namespace Kumiko.Desktop;

internal interface IAsyncPowerSessionBackend
{
    Task<IDisposable> KeepAwakeAsync(KeepAwakeKind kind, string reason, CancellationToken cancellation);
}

/// <summary>Serial worker owns D-Bus operations and cleanup. Native GTK/X11 idle queries remain on the App thread.</summary>
internal sealed class LinuxPowerBackend : IPowerSessionBackend, IAsyncPowerSessionBackend
{
    readonly object gate = new();
    readonly CancellationTokenSource lifetime = new();
    readonly CancellationToken stopped;
    readonly string? address;
    readonly LinuxIdleClock? idle;
    Task tail = Task.CompletedTask;
    LinuxPowerBus? bus;
    Action<PowerStatus>? changed;
    Action<SessionChange>? session;
    PowerStatus current = new();
    int capabilities;
    bool disposed;
    bool refreshPending;
    long refreshVersion;
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal LinuxPowerBackend(string? fixtureAddress = null)
    {
        address = fixtureAddress;
        stopped = lifetime.Token;
        idle = fixtureAddress is null ? LinuxIdleClock.TryOpen() : null;
        capabilities = idle is null ? 0 : (int)PowerCapabilities.IdleTime;
    }
    public PowerCapabilities Capabilities => (PowerCapabilities)Volatile.Read(ref capabilities);
    public TimeSpan? IdleTime => idle?.Read();
    public PowerStatus Read() => Volatile.Read(ref current);
    public void Start(Action<PowerStatus> callback, Action<SessionChange> sessionCallback)
    {
        changed = callback;
        session = sessionCallback;
        _ = Enqueue(token =>
        {
            try
            {
                // The bus outlives this queue operation; retain the backend lifetime token, not its temporary linked token.
                bus = new(Invalidate, value => session?.Invoke(value), stopped, address);
                Publish(bus.Read());
                bus.Start();
                Publish(bus.Read()); // Reconcile a change during discovery, without a polling loop.
            }
            catch (Exception ex) when (ex is SecretBusException or IOException or DllNotFoundException or EntryPointNotFoundException)
            {
                Publish(new());
            }
            return true;
        }, default);
    }
    void Publish(PowerStatus status)
    {
        if (stopped.IsCancellationRequested)
            return;
        Volatile.Write(ref current, status);
        Volatile.Write(ref capabilities, (int)((idle is null ? 0 : PowerCapabilities.IdleTime) | (bus?.Capabilities ?? 0)));
        changed?.Invoke(status); // PowerSession marshals both status and capability changes with App.Dispatch.
    }
    void Invalidate()
    {
        lock (gate)
        {
            if (disposed)
                return;
            refreshVersion++;
            if (!refreshPending)
                QueueRefresh();
        }
    }
    void QueueRefresh()
    {
        refreshPending = true;
        _ = Enqueue(token =>
        {
            long observed;
            lock (gate)
                observed = refreshVersion;
            try
            {
                if (bus is not null)
                    Publish(bus.Read());
            }
            finally
            {
                lock (gate)
                {
                    refreshPending = false;
                    if (!disposed && observed != refreshVersion)
                        QueueRefresh(); // At most one trailing refresh for changes received during a roundtrip.
                }
            }
            return true;
        }, default);
    }
    Task<T> Enqueue<T>(Func<CancellationToken, T> operation, CancellationToken cancellation)
    {
        lock (gate)
        {
            if (disposed)
                return Task.FromCanceled<T>(new CancellationToken(true));
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestCancellation = cancellation.Register(() => completion.TrySetCanceled(cancellation));
            var disposalCancellation = stopped.Register(() => completion.TrySetCanceled(stopped));
            tail = tail.ContinueWith(_ =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopped, cancellation);
                try
                {
                    linked.Token.ThrowIfCancellationRequested();
                    var result = operation(linked.Token);
                    if (linked.IsCancellationRequested)
                    {
                        if (result is IDisposable owned)
                            owned.Dispose();
                        completion.TrySetCanceled(linked.Token);
                    }
                    else if (!completion.TrySetResult(result) && result is IDisposable unclaimed)
                        unclaimed.Dispose();
                }
                catch (Exception error)
                {
                    if (linked.IsCancellationRequested || error is OperationCanceledException)
                        completion.TrySetCanceled(new CancellationToken(true));
                    else
                        completion.TrySetException(error);
                }
                finally
                {
                    requestCancellation.Dispose();
                    disposalCancellation.Dispose();
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return completion.Task;
        }
    }
    public IDisposable KeepAwake(KeepAwakeKind kind, string reason) =>
        throw new NotSupportedException("Linux idle inhibition requires awaiting PowerSession.KeepAwakeAsync.");
    public Task<IDisposable> KeepAwakeAsync(KeepAwakeKind kind, string reason, CancellationToken cancellation) =>
        Enqueue(token => bus is null ? throw new PlatformNotSupportedException("The login1 idle inhibitor is unavailable.") : bus.KeepAwake(kind, reason, token), cancellation);
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            lifetime.Cancel();
            idle?.Dispose();
            Completion = tail.ContinueWith(_ =>
            {
                bus?.Dispose();
                bus = null;
                changed = null;
                session = null;
                lifetime.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
