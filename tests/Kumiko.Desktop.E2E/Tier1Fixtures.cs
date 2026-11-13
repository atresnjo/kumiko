using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.UI;
using Kumiko.Web;

static partial class Tier1Checks
{
    static void FixtureFlows(App app, Action<bool, string> check)
    {
        var notificationBackend = new NotificationFixture();
        using var notification = new Notification(app, callback =>
        {
            notificationBackend.Click = callback;
            return notificationBackend;
        });
        int clicked = 0;
        notification.Show(new("saved", "Saved", Group: "documents"), () => clicked++).GetAwaiter().GetResult();
        notification.Show(new("saved", "Saved again", Group: "documents"), () => clicked += 10).GetAwaiter().GetResult();
        notificationBackend.Click!(new("saved", "documents"));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(clicked == 10 && notificationBackend.Active.Count == 1, "stable notification IDs replace both content and click handlers");
        notification.Close("saved", "documents");
        notificationBackend.Click!(new("saved", "documents"));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(clicked == 10 && notificationBackend.Active.Count == 0, "closed notifications reject retained native click callbacks");
        var shortcutBackend = new ShortcutFixture();
        using var shortcuts = new GlobalShortcut(app, shortcutBackend);
        int invoked = 0;
        check(shortcuts.Register("CmdOrCtrl+Shift+K", () => invoked++), "global shortcut fixtures accept an available chord");
        check(!shortcuts.Register("Primary+Shift+K", () => invoked++), "accelerator aliases report duplicate shortcut conflicts");
        var stale = shortcutBackend.Last!;
        stale();
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(invoked == 1 && shortcuts.IsRegistered("Primary+Shift+K"), "shortcut callbacks wake and execute on the App thread");
        shortcuts.Unregister("Primary+Shift+K");
        stale();
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(invoked == 1, "unregistered shortcuts reject queued and retained callbacks");
        var trayBackend = new TrayFixture();
        Action<bool>? trayClick = null;
        using var menu = new Menu(app, new MenuItem("Example", Click: _ => invoked++));
        using var tray = new Tray(app, new("fixture", ToolTip: "Initial", Menu: menu), callback =>
        {
            trayClick = callback;
            return trayBackend;
        });
        int left = 0, right = 0;
        tray.Click += () => left++;
        tray.RightClick += () => right++;
        tray.SetToolTip("Updated");
        trayClick!(false);
        trayClick(true);
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(left == 1 && right == 1 && trayBackend.Options?.ToolTip == "Updated" && trayBackend.Popups == 1, "tray updates and left/right click routing use one stable owned item");
        tray.Dispose();
        trayClick(false);
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(left == 1 && trayBackend.Disposed, "disposed trays remove callbacks and backend resources");
        using var expiredMenu = new Menu(app, new MenuItem("Expired"));
        using var expiredTray = new Tray(app, new("expired-menu", Menu: expiredMenu), _ => new TrayFixture());
        expiredMenu.Dispose();
        bool rejectedMenu = false;
        try
        {
            expiredTray.PopUpMenu();
        }
        catch (ObjectDisposedException)
        {
            rejectedMenu = true;
        }
        check(rejectedMenu, "tray popup rejects disposed menus before dereferencing native resources");
        LifecycleFixture(check);
    }

    static void LifecycleFixture(Action<bool, string> check)
    {
        var backend = new LifecycleBackend();
        using var app = new App(new("Lifecycle fixture", Hidden: true), _ => backend);
        var events = new List<string>();
        Action<CancelableEvent> cancel = e =>
        {
            events.Add("cancel");
            e.PreventDefault();
        };
        app.BeforeQuit += cancel;
        app.WillQuit += _ => events.Add("will");
        app.DidQuit += () => events.Add("quit");
        app.Quit();
        check(!backend.Stopped && events.SequenceEqual(new[] { "cancel" }), "BeforeQuit cancellation prevents native shutdown");
        app.BeforeQuit -= cancel;
        app.BeforeQuit += _ => events.Add("before");
        app.Quit();
        app.Run();
        check(backend.Stopped && events.SequenceEqual(new[] { "cancel", "before", "will", "quit" }), "normal quit preserves before/will/did order");
        events.Clear();
        app.Exit(0);
        app.Run();
        check(events.Count == 0, "Exit bypasses lifecycle events");
    }

    sealed class NotificationFixture : INotificationBackend
    {
        internal Action<NotificationClick>? Click;
        internal readonly Dictionary<(string, string), NotificationOptions> Active = new();
        public Task Show(NotificationOptions options)
        {
            Active[(options.Id, options.Group)] = options;
            return Task.CompletedTask;
        }

        public void Close(string id, string group) => Active.Remove((id, group));
        public void Clear() => Active.Clear();
        public void Dispose() => Active.Clear();
    }

    sealed class ShortcutFixture : IGlobalShortcutBackend
    {
        internal Action? Last;
        public bool Register(ShortcutChord chord, Action callback)
        {
            Last = callback;
            return true;
        }

        public void Unregister(ShortcutChord chord)
        {
        }

        public void Dispose()
        {
        }
    }

    sealed class TrayFixture : ITrayBackend
    {
        internal TrayOptions? Options;
        internal int Popups;
        internal bool Disposed;
        public Rect Bounds => new(0, 0, 24, 24);

        public void Update(TrayOptions options) => Options = options;
        public void PopUpMenu(Menu menu) => Popups++;
        public void Dispose() => Disposed = true;
    }

    sealed class LifecycleBackend : IAppBackend
    {
        internal bool Stopped;
        public IWindowBackend CreateWindow(Window window, PageOptions? page) => throw new NotSupportedException();
        public void Dispatch(Action action) => action();
        public void Run()
        {
        }

        public void RunFor(TimeSpan duration)
        {
        }

        public void Quit() => Stopped = true;
        public void Activate()
        {
        }

        public void Dispose()
        {
        }
    }
}
