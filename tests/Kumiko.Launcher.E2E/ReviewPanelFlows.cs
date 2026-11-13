using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Launcher;

static class ReviewPanelFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var stream = File.Create(Path.Combine(output, "review-panel.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartArray();
        HostQuit(json, check);
        foreach (var panelFirst in new[] { true, false })
            Veto(json, panelFirst, check);
        Visibility(check);
        json.WriteEndArray();
    }

    static void HostQuit(Utf8JsonWriter json, Action<bool, string> check)
    {
        using var app = new App(new("Hidden launcher host quit", false, true));
        using var program = LauncherApp.CreateProgram();
        using var catalog = new ApplicationCatalog(app, []);
        using var startup = new StartupItem(app, "dev.fixture.quit", "Quit fixture", new NoStartup());
        using var desktop = new NativeLauncherDesktop(app, program, catalog, startup);
        using var controller = new LauncherController(program, desktop, app.Dispatch);
        var will = 0;
        app.WillQuit += _ => will++;
        // Same typed request as Settings Quit, through the real host and native App.
        program.Send(new LauncherMsg.Quit());
        app.RunFor(TimeSpan.FromMilliseconds(40));
        json.WriteStartObject();
        json.WriteString("route", "typed Quit -> NativeLauncherDesktop.Quit -> App.Quit");
        json.WriteNumber("remainingWindows", app.Windows.Count);
        json.WriteNumber("willQuit", will);
        json.WriteEndObject();
        json.Flush();
        check(app.Windows.Count == 0 && will == 1, "actual hidden native launcher host Quit releases its panel and reaches WillQuit once");
    }

    static void Veto(Utf8JsonWriter json, bool panelFirst, Action<bool, string> check)
    {
        using var app = new App(new("Hidden launcher quit veto", false, true));
        using var program = PassiveApp.CreateProgram();
        using var otherProgram = PassiveApp.CreateProgram();
        Window? other = null;
        if (!panelFirst)
            other = app.NewWindow(new(Visible: false), otherProgram);
        var publication = new PrivatePresentation();
        using var panel = new LauncherPanel(app, program, null, publication);
        other ??= app.NewWindow(new(Visible: false), otherProgram);
        using (other)
        {
            var handle = panel.Window.NativeHandle;
            var screen = new Screen(new(0, 0, 1000, 800), new(0, 0, 1000, 800), 1, true);
            var will = 0;
            app.WillQuit += _ => will++;
            panel.Show(screen);
            panel.Window.Close();
            panel.Show(screen);
            check(panel.IsVisible && panel.Window.NativeHandle == handle && app.Windows.Count == 2,
                "ordinary Close hides and reuses the same real native panel");
            Action<CancelableEvent> veto = request => request.PreventDefault();
            other.Closing += veto;
            bool? reply = null;
            app.RequestNativeQuit(value => reply = value);
            check(reply == false && will == 0 && app.Windows.Count == 2 && panel.IsVisible,
                "another window veto preserves both windows and panel visibility in either close order");
            other.Closing -= veto;
            panel.Window.Closing += veto;
            app.Quit();
            check(will == 0 && app.Windows.Count == 2, "additional panel Closing handlers can still veto explicit application quit");
            panel.Window.Closing -= veto;
            // A global quitting flag would misclassify this ordinary reentrant close.
            var dismissed = false;
            app.BeforeQuit += _ =>
            {
                panel.Window.Close();
                dismissed = !panel.IsVisible && app.Windows.Count == 2;
            };
            app.RequestNativeQuit(value => reply = value);
            check(dismissed && reply == true && will == 1 && app.Windows.Count == 0,
                "reentrant ordinary dismissal remains distinct; retry quits through the native reply path");
            json.WriteStartObject();
            json.WriteBoolean("panelFirst", panelFirst);
            json.WriteBoolean("ordinaryCloseRetainedIdentity", true);
            json.WriteBoolean("vetoThenRetry", reply == true && dismissed);
            json.WriteNumber("willQuit", will);
            json.WriteEndObject();
        }
    }

    static void Visibility(Action<bool, string> check)
    {
        using var app = new App(new("Hidden launcher thread check", false, true));
        using var program = PassiveApp.CreateProgram();
        var presentation = new PrivatePresentation();
        using var panel = new LauncherPanel(app, program, null, presentation);
        var before = presentation.Reads;
        var rejected = Task.Run(() =>
        {
            try
            {
                _ = panel.IsVisible;
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();
        check(rejected && presentation.Reads == before, "worker IsVisible fails before touching the presentation or native state");
        panel.Dispose();
        check(!panel.IsVisible && !Task.Run(() => panel.IsVisible).GetAwaiter().GetResult(),
            "disposed panel visibility remains false without native access on either thread");
    }

    sealed class NoStartup : IStartupItemBackend
    {
        public bool WasLaunchedAtLogin => false;
        public StartupStatus Read() => new(StartupState.Disabled);
        public StartupStatus SetEnabled(bool enabled) => throw new InvalidOperationException("No startup writes allowed");
    }

    sealed class PrivatePresentation : ILauncherPanelPresentation
    {
        bool visible;
        public int Reads;
        public bool IsVisible
        {
            get
            {
                Interlocked.Increment(ref Reads);
                return visible;
            }
        }
        public void Show() => visible = true;
        public void Hide() => visible = false;
    }
}
