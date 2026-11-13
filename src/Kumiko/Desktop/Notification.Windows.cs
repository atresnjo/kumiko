namespace Kumiko.Desktop;
/// <summary>
/// WinRT toasts with Action Center history and in-process click activation; the Shell balloon is used
/// only when the toast runtime cannot be prepared. Cold-start COM activation is not implemented.
/// </summary>
internal sealed class WindowsNotificationBackend : INotificationBackend
{
    readonly INotificationBackend inner;
    /// <summary>Why toasts were unavailable, or null when WinRT toasts are in use.</summary>
    internal string? FallbackReason
    {
        get;
    }

    internal WindowsNotificationBackend(App app, Action<NotificationClick> click) : this(app, click, () => SystemToasts(app), callback => new WindowsBalloonNotifications(app, callback))
    {
    }

    internal WindowsNotificationBackend(App app, Action<NotificationClick> click, Func<IToastPlatform> toasts, Func<Action<NotificationClick>, INotificationBackend> balloon)
    {
        IToastPlatform platform;
        try
        {
            platform = toasts();
        }
        catch (PlatformNotSupportedException ex)
        {
            FallbackReason = ex.Message;
            Console.Error.WriteLine("Kumiko notifications: " + ex.Message + " Using the Shell_NotifyIcon balloon fallback.");
            inner = balloon(click);
            return;
        }

        inner = new WindowsToastBackend(app, click, platform);
    }

    static IToastPlatform SystemToasts(App app) => OperatingSystem.IsWindows() ? WindowsToastPlatform.Create(app) : throw new PlatformNotSupportedException("WinRT toasts require Windows.");
    public Task Show(NotificationOptions options) => inner.Show(options);
    public void Close(string id, string group) => inner.Close(id, group);
    public void Clear() => inner.Clear();
    public void Dispose() => inner.Dispose();
}
