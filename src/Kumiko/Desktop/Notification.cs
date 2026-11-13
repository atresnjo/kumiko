namespace Kumiko.Desktop;

public sealed record NotificationOptions(string Id, string Title, string Body = "", string Subtitle = "", string Group = "");
public sealed record NotificationClick(string Id, string Group);
internal interface INotificationBackend : IDisposable
{
    Task Show(NotificationOptions options);
    void Close(string id, string group);
    void Clear();
}

/// <summary>Owns notifications with stable IDs and guarded click handlers.</summary>
public sealed class Notification : IDisposable
{
    readonly App app;
    readonly INotificationBackend backend;
    readonly Dictionary<(string Id, string Group), Action?> handlers = new();
    readonly HashSet<(string Id, string Group)> dismissed = new();
    long clearGeneration;
    bool disposed;
    public event Action<NotificationClick>? Clicked;
    public Notification(App app) : this(app, null)
    {
    }

    internal Notification(App app, Func<Action<NotificationClick>, INotificationBackend>? factory)
    {
        app.CheckThread();
        this.app = app;
        if (factory is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden apps require an injected notification backend.");
        backend = factory is not null ? factory(NativeClick) : OperatingSystem.IsMacOS() ? new MacNotificationBackend(app, NativeClick) : OperatingSystem.IsWindows() ? new WindowsNotificationBackend(app, NativeClick) : new LinuxNotificationBackend(app, NativeClick);
        app.Disposing += Dispose;
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public Task Show(NotificationOptions options, Action? click = null)
    {
        Check();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);
        handlers[(options.Id, options.Group)] = click;
        dismissed.Remove((options.Id, options.Group));
        try
        {
            return backend.Show(options);
        }
        catch
        {
            handlers.Remove((options.Id, options.Group));
            throw;
        }
    }

    void NativeClick(NotificationClick notification)
    {
        long generation = Volatile.Read(ref clearGeneration);
        try
        {
            app.Dispatch(() =>
            {
                if (disposed || generation != Volatile.Read(ref clearGeneration) || dismissed.Contains((notification.Id, notification.Group)))
                    return;
                handlers.TryGetValue((notification.Id, notification.Group), out var handler);
                handler?.Invoke();
                if (!disposed && generation == Volatile.Read(ref clearGeneration) && !dismissed.Contains((notification.Id, notification.Group)))
                    Clicked?.Invoke(notification);
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Close(string id, string group = "")
    {
        Check();
        backend.Close(id, group);
        handlers.Remove((id, group));
        dismissed.Add((id, group));
    }

    public void ClearNotifications()
    {
        Check();
        Interlocked.Increment(ref clearGeneration);
        dismissed.UnionWith(handlers.Keys);
        handlers.Clear();
        backend.Clear();
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        app.Disposing -= Dispose;
        handlers.Clear();
        backend.Dispose();
    }
}
