using System.Diagnostics;
using System.Xml.Linq;
using Kumiko;
using Kumiko.Desktop;

/// <summary>
/// Windows toast behavior without showing a toast: payload and identity rules, fallback selection, and
/// the toast lifecycle through Notification with a fixture platform that raises events from workers.
/// </summary>
static class WindowsNotificationChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;
        Verify(check);
    }

    internal static void Verify(Action<bool, string> check)
    {
        PayloadRules(check);
        using var app = new App(new("Windows notification checks", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        FallbackSelection(app, check);
        Lifecycle(app, errors, check);
    }

    static void PayloadRules(Action<bool, string> check)
    {
        string hostile = "Build <b>&\"' \u0001\uD800 \U0001F514";
        var texts = Texts(WindowsToastContent.Xml(new("id", hostile, "Body </toast>", "Sub & title")));
        check(texts.SequenceEqual([
            "Build <b>&\"' \uFFFD\uFFFD \U0001F514",
            "Sub & title",
            "Body </toast>"
        ]), "toast XML escapes markup, keeps surrogate pairs and replaces characters XML cannot carry");
        check(Texts(WindowsToastContent.Xml(new("id", "Only title"))).SequenceEqual(["Only title"]), "empty toast subtitle and body are omitted");
        string longTitle = new('t', 2000);
        string longBody = new('b', 5000);
        check(Texts(WindowsToastContent.Xml(new("id", longTitle, longBody))).SequenceEqual([longTitle, longBody]), "toast content has no balloon length limits");
        check(WindowsBalloonNotifications.Title(new("id", new string('t', 63), new string('b', 255))).Length == 63, "balloon fallback accepts 63 title and 255 body characters");
        check(Throws(() => WindowsBalloonNotifications.Title(new("id", new string('t', 64)))), "balloon fallback rejects a 64 character title");
        check(Throws(() => WindowsBalloonNotifications.Title(new("id", new string('t', 40), Subtitle: new string('s', 30)))), "balloon fallback counts the combined title and subtitle");
        check(Throws(() => WindowsBalloonNotifications.Title(new("id", "t", new string('b', 256)))), "balloon fallback rejects a 256 character body");
        var plain = WindowsToastContent.Identity("ab", "");
        var grouped = WindowsToastContent.Identity("b", "a");
        var huge = WindowsToastContent.Identity(new string('x', 300), new string('g', 300));
        check(new[] { plain.Tag, plain.Group, grouped.Tag, grouped.Group, huge.Tag, huge.Group }.All(Hex), "toast tags and groups are 16 hex digits for every ID and group length");
        check(plain.Tag != grouped.Tag && plain.Group != grouped.Group && WindowsToastContent.Identity("ab", "") == plain, "toast identity is deterministic and keeps ID/group boundaries distinct");
        check(WindowsToastContent.Identity("other", "a").Group == grouped.Group && WindowsToastContent.Identity("other", "a").Tag != grouped.Tag, "notifications in one group share a toast group with distinct tags");
        string id = WindowsToastContent.ApplicationId("My App!", @"C:\Apps\My App.exe");
        check(id.StartsWith("Kumiko.MyApp.", StringComparison.Ordinal) && Hex(id["Kumiko.MyApp.".Length..]) && !id.Contains(' '), "unpackaged AppUserModelID is Kumiko.<name>.<hash> without spaces");
        check(id == WindowsToastContent.ApplicationId("My App!", @"c:\apps\my app.exe") && id != WindowsToastContent.ApplicationId("My App!", @"C:\Other\My App.exe"), "AppUserModelID is stable across path case and distinct per executable");
        check(WindowsToastContent.ApplicationId(new string('n', 500), null).Length <= 128 && WindowsToastContent.ApplicationId("!!!", null).StartsWith("Kumiko.App.", StringComparison.Ordinal), "AppUserModelID stays within 128 characters and names punctuation-only apps");
    }

    static void FallbackSelection(App app, Action<bool, string> check)
    {
        var balloon = new BalloonFixture();
        WindowsNotificationBackend? backend = null;
        using (var service = new Notification(app, callback => backend = new WindowsNotificationBackend(app, callback, () => throw new PlatformNotSupportedException("fixture: toast factory missing"), _ => balloon)))
        {
            service.Show(new("fallback", "Balloon")).GetAwaiter().GetResult();
            check(backend!.FallbackReason == "fixture: toast factory missing" && balloon.Shown == 1, "an unavailable toast factory selects the balloon fallback and reports why");
        }

        check(balloon.Disposed, "the balloon fallback is disposed with its Notification owner");
        var toasts = new ToastFixture();
        using (var service = new Notification(app, callback => backend = new WindowsNotificationBackend(app, callback, () => toasts, _ => throw new InvalidOperationException("balloon must stay unused"))))
            check(backend!.FallbackReason is null, "an available toast platform is used without creating the balloon fallback");
    }

    static void Lifecycle(App app, List<Exception> errors, Action<bool, string> check)
    {
        var toasts = new ToastFixture();
        using var service = new Notification(app, callback => new WindowsNotificationBackend(app, callback, () => toasts, _ => throw new InvalidOperationException("balloon must stay unused")));
        var received = new List<NotificationClick>();
        service.Clicked += received.Add;
        int clicks = 0, clickThread = 0;
        service.Show(new("build", "Build & test", "Passed", "main", "ci"), () => clicks++).GetAwaiter().GetResult();
        var first = toasts.Last;
        check((first.Tag, first.Group) == WindowsToastContent.Identity("build", "ci") && Texts(first.Xml).SequenceEqual(["Build & test", "main", "Passed"]), "toasts carry the mapped tag/group and title, subtitle and body lines");
        service.Show(new("build", "Replacement", Group: "ci"), () =>
        {
            clicks += 10;
            clickThread = Environment.CurrentManagedThreadId;
        }).GetAwaiter().GetResult();
        var second = toasts.Last;
        check(first.Disposed && first.Hidden == 0 && !second.Disposed && second.Tag == first.Tag, "replacement releases the previous toast's handlers without hiding the replacement");
        first.Raise(new(ToastEventKind.Activated));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(clicks == 0 && received.Count == 0, "late activation of a replaced toast is ignored");
        second.Raise(new(ToastEventKind.Activated));
        Pump(app, () => clicks == 10);
        check(clickThread == Environment.CurrentManagedThreadId && received.SequenceEqual([new NotificationClick("build", "ci")]) && second.Disposed, "worker-thread activation runs the click on the App thread once and releases the toast");
        service.Show(new("expired", "Timed out")).GetAwaiter().GetResult();
        var expired = toasts.Last;
        expired.Raise(new(ToastEventKind.Dismissed, 2));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        expired.Raise(new(ToastEventKind.Activated));
        Pump(app, () => received.Count == 2);
        check(received[^1] == new NotificationClick("expired", ""), "a toast that timed out into Action Center still routes its click");
        service.Show(new("dismissed", "User dismissed")).GetAwaiter().GetResult();
        var dismissed = toasts.Last;
        dismissed.Raise(new(ToastEventKind.Dismissed, 0));
        Pump(app, () => dismissed.Disposed);
        dismissed.Raise(new(ToastEventKind.Activated));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(received.Count == 2, "user dismissal releases the toast and ignores later activation");
        service.Show(new("failed", "Never displayed")).GetAwaiter().GetResult();
        var failed = toasts.Last;
        failed.Raise(new(ToastEventKind.Failed, unchecked((int)0x80070490)));
        Pump(app, () => failed.Disposed);
        check(errors.Count == 1 && errors[0].Message.Contains("80070490", StringComparison.Ordinal), "a failed toast is released and its HRESULT reaches UnhandledException");
        service.Show(new("closed", "Closed", Group: "g")).GetAwaiter().GetResult();
        var closed = toasts.Last;
        service.Close("closed", "g");
        closed.Raise(new(ToastEventKind.Activated));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(closed.Hidden == 1 && closed.Disposed && toasts.Removed.SequenceEqual([WindowsToastContent.Identity("closed", "g")]) && received.Count == 2, "Close hides the toast, removes its history entry and ignores later activation");
        service.Show(new("kept", "Original")).GetAwaiter().GetResult();
        var kept = toasts.Last;
        toasts.FailNext = true;
        check(Throws(() => service.Show(new("kept", "Rejected replacement"))) && !kept.Disposed, "a failed replacement keeps the displayed toast");
        kept.Raise(new(ToastEventKind.Activated));
        Pump(app, () => received.Count == 3);
        check(received[^1] == new NotificationClick("kept", ""), "the kept toast still routes its click after a failed replacement");
        service.Show(new("a", "A")).GetAwaiter().GetResult();
        var a = toasts.Last;
        service.Show(new("b", "B", Group: "other")).GetAwaiter().GetResult();
        var b = toasts.Last;
        service.ClearNotifications();
        a.Raise(new(ToastEventKind.Activated));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(a.Hidden == 1 && b.Hidden == 1 && a.Disposed && b.Disposed && toasts.Clears == 1 && received.Count == 3, "ClearNotifications hides live toasts, clears the application history and drops pending clicks");
        service.Show(new("persistent", "Survives its owner")).GetAwaiter().GetResult();
        var persistent = toasts.Last;
        service.Dispose();
        persistent.Raise(new(ToastEventKind.Activated));
        app.RunFor(TimeSpan.FromMilliseconds(25));
        check(persistent.Disposed && persistent.Hidden == 0 && toasts.Disposed && received.Count == 3 && errors.Count == 1, "disposal releases handlers, leaves delivered toasts in history and ignores late callbacks");
    }

    static string[] Texts(string xml) => XDocument.Parse(xml).Descendants("binding").Single(e => (string?)e.Attribute("template") == "ToastGeneric").Elements("text").Select(e => e.Value).ToArray();
    static bool Hex(string value) => value.Length == WindowsToastContent.IdentityLength && value.All(char.IsAsciiHexDigitUpper);
    static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return true;
        }
    }

    static void Pump(App app, Func<bool> complete)
    {
        var deadline = Stopwatch.StartNew();
        // Bounded test event pumping, never an application render or service polling loop.
        while (!complete() && deadline.Elapsed < TimeSpan.FromSeconds(5))
            app.RunFor(TimeSpan.FromMilliseconds(10));
        if (!complete())
            throw new TimeoutException("Windows toast fixture event was not delivered.");
    }

    sealed class ToastFixture : IToastPlatform
    {
        readonly List<ShownToast> shown = new();
        internal List<(string Tag, string Group)> Removed { get; } = new();
        internal ShownToast Last => shown[^1];
        internal int Clears
        {
            get; private set;
        }

        internal bool Disposed
        {
            get; private set;
        }

        internal bool FailNext
        {
            get; set;
        }

        public IToastHandle Show(string xml, string tag, string group, Action<ToastEvent> callback)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("fixture toast display failure");
            }

            var toast = new ShownToast(xml, tag, group, callback);
            shown.Add(toast);
            return toast;
        }

        public void Remove(string tag, string group) => Removed.Add((tag, group));
        public void Clear() => Clears++;
        public void Dispose() => Disposed = true;
    }

    /// <summary>Raise keeps working after Dispose to model native callbacks already in flight.</summary>
    sealed class ShownToast(string xml, string tag, string group, Action<ToastEvent> callback) : IToastHandle
    {
        internal string Xml => xml;
        internal string Tag => tag;
        internal string Group => group;
        internal int Hidden
        {
            get; private set;
        }

        internal bool Disposed
        {
            get; private set;
        }

        public void Hide() => Hidden++;
        public void Dispose() => Disposed = true;
        internal void Raise(ToastEvent value) => Task.Run(() => callback(value)).GetAwaiter().GetResult();
    }

    sealed class BalloonFixture : INotificationBackend
    {
        internal int Shown
        {
            get; private set;
        }

        internal bool Disposed
        {
            get; private set;
        }

        public Task Show(NotificationOptions options)
        {
            Shown++;
            return Task.CompletedTask;
        }

        public void Close(string id, string group)
        {
        }

        public void Clear()
        {
        }

        public void Dispose() => Disposed = true;
    }
}
