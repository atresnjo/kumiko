namespace Kumiko.Desktop;

internal enum ToastEventKind
{
    Activated,
    Dismissed,
    Failed
}

/// <summary>Code is ToastDismissalReason for Dismissed and the HRESULT for Failed.</summary>
internal readonly record struct ToastEvent(ToastEventKind Kind, int Code = 0);
/// <summary>A shown toast: Dispose unsubscribes its events and releases it without removing it from history.</summary>
internal interface IToastHandle : IDisposable
{
    void Hide();
}

/// <summary>The native toast surface. Callbacks may run on any thread; every other member runs on the App thread.</summary>
internal interface IToastPlatform : IDisposable
{
    IToastHandle Show(string xml, string tag, string group, Action<ToastEvent> callback);
    void Remove(string tag, string group);
    void Clear();
}

/// <summary>
/// Maps Notification IDs/groups onto toasts. Per-toast state lives only on the App thread; native event
/// callbacks are dispatched there and ignored unless they belong to the current toast for that key.
/// </summary>
internal sealed class WindowsToastBackend : INotificationBackend
{
    // ToastDismissalReason.TimedOut: the popup expired into Action Center and can still be clicked.
    const int TimedOut = 2;
    readonly App app;
    readonly Action<NotificationClick> click;
    readonly IToastPlatform platform;
    readonly Dictionary<(string Id, string Group), Entry> entries = new();
    volatile bool disposed;
    sealed class Entry((string Id, string Group) key)
    {
        internal (string Id, string Group) Key { get; } = key;
        internal IToastHandle? Handle
        {
            get; set;
        }
    }

    internal WindowsToastBackend(App app, Action<NotificationClick> click, IToastPlatform platform)
    {
        this.app = app;
        this.click = click;
        this.platform = platform;
    }

    public Task Show(NotificationOptions options)
    {
        app.CheckThread();
        var key = (options.Id, options.Group);
        var (tag, group) = WindowsToastContent.Identity(options.Id, options.Group);
        var entry = new Entry(key);
        // The same Tag/Group replaces the previous toast in Action Center. Show first so a failed
        // replacement leaves the existing toast and its click routing intact.
        entry.Handle = platform.Show(WindowsToastContent.Xml(options), tag, group, value => Deliver(entry, value));
        if (entries.Remove(key, out var previous))
            previous.Handle?.Dispose();
        entries[key] = entry;
        return Task.CompletedTask;
    }

    void Deliver(Entry entry, ToastEvent value)
    {
        if (disposed)
            return;
        try
        {
            app.Dispatch(() => Handle(entry, value));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void Handle(Entry entry, ToastEvent value)
    {
        if (disposed || !entries.TryGetValue(entry.Key, out var current) || current != entry)
            return;
        if (value.Kind == ToastEventKind.Dismissed && value.Code == TimedOut)
            return;
        // Activation, user or application dismissal and failure all end this toast's routing.
        entries.Remove(entry.Key);
        entry.Handle?.Dispose();
        if (value.Kind == ToastEventKind.Activated)
            click(new(entry.Key.Id, entry.Key.Group));
        else if (value.Kind == ToastEventKind.Failed)
            app.Report(new IOException($"Windows toast notification failed (HRESULT 0x{value.Code:X8})."));
    }

    public void Close(string id, string group)
    {
        app.CheckThread();
        if (entries.Remove((id, group), out var entry))
            Release(entry, hide: true);
        var (tag, toastGroup) = WindowsToastContent.Identity(id, group);
        platform.Remove(tag, toastGroup);
    }

    public void Clear()
    {
        app.CheckThread();
        foreach (var entry in entries.Values)
            Release(entry, hide: true);
        entries.Clear();
        platform.Clear();
    }

    static void Release(Entry entry, bool hide)
    {
        if (entry.Handle is not { } handle)
            return;
        if (hide)
            handle.Hide();
        handle.Dispose();
    }

    /// <summary>Delivered toasts stay in Action Center; their clicks no longer reach this process.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var entry in entries.Values)
            Release(entry, hide: false);
        entries.Clear();
        platform.Dispose();
    }
}
