using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;
/// <summary>Shell_NotifyIcon balloon fallback used when WinRT toasts are unavailable.</summary>
internal sealed unsafe class WindowsBalloonNotifications : INotificationBackend
{
    readonly WindowsMessageWindow host;
    readonly Dictionary<(string, string), uint> identities = new();
    readonly Dictionary<uint, NotificationClick> clicks = new();
    readonly Action<NotificationClick> click;
    uint nextId;
    internal WindowsBalloonNotifications(App app, Action<NotificationClick> click)
    {
        this.click = click;
        host = new(app, Message);
    }

    nint? Message(uint message, nuint w, nint l)
    {
        if (message != 0x8003)
            return null;
        if (l == 0x405 && clicks.TryGetValue((uint)w, out var notification))
            click(notification);
        return 0;
    }

    /// <summary>NOTIFYICONDATAW.szInfoTitle and szInfo hold 64 and 256 UTF-16 units including the terminator.</summary>
    internal static string Title(NotificationOptions options)
    {
        string title = options.Subtitle.Length == 0 ? options.Title : options.Title + " — " + options.Subtitle;
        if (title.Length > 63 || options.Body.Length > 255)
            throw new ArgumentException("Windows balloon fallback permits at most 63 title characters and 255 body characters.");
        return title;
    }

    public Task Show(NotificationOptions options)
    {
        string title = Title(options);
        var key = (options.Id, options.Group);
        bool existing = identities.TryGetValue(key, out uint id);
        if (!existing)
            id = ++nextId;
        var data = new WindowsTrayBackend.NotifyData
        {
            Size = (uint)sizeof(WindowsTrayBackend.NotifyData),
            Window = host.Handle,
            Id = id,
            Flags = 1 | 2 | 0x10,
            Callback = 0x8003,
            Icon = LoadIconW(0, 32512),
            InfoFlags = 1,
            Timeout = 10000
        };
        title.AsSpan().CopyTo(new Span<char>(data.InfoTitle, 64));
        options.Body.AsSpan().CopyTo(new Span<char>(data.Info, 256));
        if (WindowsTrayBackend.Shell_NotifyIconW(existing ? 1u : 0u, ref data) == 0)
            throw new IOException("Windows notification balloon failed; Shell_NotifyIcon is unavailable.");
        identities[key] = id;
        clicks[id] = new(options.Id, options.Group);
        return Task.CompletedTask;
    }

    public void Close(string id, string group)
    {
        if (!identities.Remove((id, group), out uint native))
            return;
        var data = new WindowsTrayBackend.NotifyData
        {
            Size = (uint)sizeof(WindowsTrayBackend.NotifyData),
            Window = host.Handle,
            Id = native
        };
        WindowsTrayBackend.Shell_NotifyIconW(2, ref data);
        clicks.Remove(native);
    }

    public void Clear()
    {
        foreach (var key in identities.Keys.ToArray())
            Close(key.Item1, key.Item2);
    }

    public void Dispose()
    {
        Clear();
        host.Dispose();
    }

    [DllImport("user32.dll")]
    static extern nint LoadIconW(nint instance, nint name);
}
