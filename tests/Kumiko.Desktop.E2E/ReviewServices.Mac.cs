using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;

static partial class Tier1Checks
{
    internal static void ReviewNativeServices(App app, Action<bool, string> check)
    {
        using var pool = new AutoreleasePool();
        using var fixture = new StatusItemFixture();
        void SetAppearance(bool dark) => ObjC.V(fixture.Button, "setAppearance:",
            ObjC.O(ObjC.Class("NSAppearance"), "appearanceNamed:", ObjC.TempString(dark ? "NSAppearanceNameDarkAqua" : "NSAppearanceNameAqua")));
        SetAppearance(false);
        using var tray = new Tray(app, new("review-appearance"), callback => new MacTrayBackend(app, callback, fixture.Handle));
        var appearances = new List<bool>();
        int thread = Environment.CurrentManagedThreadId;
        bool mainThread = true;
        using var png = new MemoryStream();
        Png.Write(png, new Pixels(Enumerable.Repeat((byte)255, 18 * 18 * 4).ToArray(), 18, 18));
        tray.AppearanceChanged += dark =>
        {
            mainThread &= Environment.CurrentManagedThreadId == thread;
            appearances.Add(dark);
            tray.SetIcon(png.ToArray(), template: dark);
        };
        check(app.Windows.Count == 0 && !tray.IsDark, "tray appearance starts from its private status button with zero windows");
        SetAppearance(true);
        Pump(app, () => appearances.Count > 0, "tray-only appearance notification");
        SetAppearance(true);
        app.RunFor(TimeSpan.FromMilliseconds(35));
        check(appearances.SequenceEqual(new[] { true }) && tray.IsDark && mainThread &&
            ObjC.B(ObjC.O(fixture.Button, "image"), "isTemplate") != 0,
            "status-button appearance observation is main-thread, duplicate-filtered and supports icon updates");
        SetAppearance(false);
        Pump(app, () => appearances.Count == 2, "tray light appearance notification");
        check(!tray.IsDark && appearances.SequenceEqual(new[] { true, false }), "tray appearance follows both directions without window broadcasts");
        SetAppearance(true);
        tray.Dispose();
        SetAppearance(false);
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(appearances.Count == 2 && ObjC.O(fixture.Button, "target") == 0,
            "tray disposal detaches observation and suppresses already queued appearance delivery");

        using var center = new NotificationCenterFixture();
        using (var previous = new Notification(app, callback => new MacNotificationBackend(app, callback, center.Handle)))
        {
            var shown = previous.Show(new("previous-owner", "Private fixture", Group: "review"));
            center.Grant();
            Pump(app, () => shown.IsCompleted, "previous-owner notification");
            shown.GetAwaiter().GetResult();
        }
        using var current = new Notification(app, callback => new MacNotificationBackend(app, callback, center.Handle));
        int clicks = 0;
        current.Clicked += _ => clicks++;
        center.Click(fromWorker: true);
        app.Dispatch(current.ClearNotifications);
        app.RunFor(TimeSpan.FromMilliseconds(50));
        check(clicks == 0 && center.ClickCompletions == 1 && center.Removals >= 2,
            "Clear between native and public recovered-click dispatch suppresses delivery and completes native response");
    }
}
