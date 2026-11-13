using Kumiko;
using Kumiko.Desktop;

namespace CRMLite;

/// <summary>
/// The desktop shell over Kumiko's <see cref="App"/> and <see cref="Notification"/>. Construct and dispose it on the
/// App thread; its other members may be called from command workers and run their native work on the App thread.
/// macOS notifications need a packaged .app with a bundle identifier. Unbundled, hidden and serviceless hosts report
/// <see cref="NoticeAccess.Unavailable"/> instead of failing, and hidden hosts are never activated.
/// </summary>
public sealed class NativeCrmDesktop : ICrmDesktop, IDisposable
{
    const string Group = "crm";
    readonly App app;
    readonly Notification? notifications;
    readonly DesktopSignal.Access access;
    readonly object gate = new();
    readonly List<Action<DesktopSignal>> listeners = [];
    bool disposed;

    public NativeCrmDesktop(App app)
    {
        this.app = app;
        try
        {
            notifications = new Notification(app);
            access = new(NoticeAccess.Ready);
        }
        catch (Exception error) when (error is PlatformNotSupportedException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            access = new(NoticeAccess.Unavailable, error.Message);
        }
        if (notifications is not null)
            notifications.Clicked += Clicked;
        app.DidBecomeActive += BecameActive;
        app.DidResignActive += ResignedActive;
    }

    public IDisposable Listen(Action<DesktopSignal> signal)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            listeners.Add(signal);
        }
        signal(access);
        return new Listener(this, signal);
    }

    public Task Post(CrmNotice notice, CancellationToken token) => OnMain(async () =>
    {
        if (notifications is null)
            throw new PlatformNotSupportedException(access.Detail);
        var shown = notifications.Show(new(notice.Id, notice.Title, notice.Body, Group: Group));
        try
        {
            await shown.ConfigureAwait(false);
        }
        catch (InvalidOperationException error)
        {
            // Kumiko reports a declined or failed authorization request this way once the request was accepted.
            throw new UnauthorizedAccessException(error.Message, error);
        }
    }, token);

    public Task Withdraw(string id, CancellationToken token) => OnMain(() =>
    {
        notifications?.Close(id, Group);
        return Task.CompletedTask;
    }, token);

    public Task Clear(CancellationToken token) => OnMain(() =>
    {
        notifications?.ClearNotifications();
        return Task.CompletedTask;
    }, token);

    public Task BringForward(CancellationToken token) => OnMain(() =>
    {
        if (!app.Options.Hidden)
            app.Activate();
        return Task.CompletedTask;
    }, token);

    /// <summary>Runs native work on the App thread and completes with its outcome, or cancels with the command.</summary>
    async Task OnMain(Func<Task> work, CancellationToken token)
    {
        var started = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            app.Dispatch(() =>
            {
                try
                {
                    started.TrySetResult(disposed ? Task.FromException(new ObjectDisposedException(nameof(NativeCrmDesktop))) : work());
                }
                catch (Exception error)
                {
                    started.TrySetException(error);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            throw new OperationCanceledException(token);
        }
        var running = await started.Task.WaitAsync(token).ConfigureAwait(false);
        await running.WaitAsync(token).ConfigureAwait(false);
    }

    void Clicked(NotificationClick click)
    {
        if (click.Group == Group)
            Emit(new DesktopSignal.Clicked(click.Id));
    }

    void BecameActive() => Emit(new DesktopSignal.Active(true));

    void ResignedActive() => Emit(new DesktopSignal.Active(false));

    void Emit(DesktopSignal signal)
    {
        Action<DesktopSignal>[] current;
        lock (gate)
            current = [.. listeners];
        foreach (var listener in current)
            listener(signal);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            listeners.Clear();
        }
        app.DidBecomeActive -= BecameActive;
        app.DidResignActive -= ResignedActive;
        if (notifications is null)
            return;
        notifications.Clicked -= Clicked;
        notifications.Dispose();
    }

    sealed class Listener(NativeCrmDesktop owner, Action<DesktopSignal> signal) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
                owner.listeners.Remove(signal);
        }
    }
}
