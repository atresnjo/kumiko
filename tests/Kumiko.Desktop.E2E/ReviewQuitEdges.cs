using System.Diagnostics;
using System.Text.Json;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Runtime;
using Kumiko.UI;
using Kumiko.Web;

static partial class ReviewLifecycle
{
    static Program<QuitModel, int> EdgeProgram(QuitModel initial) => new Program<QuitModel, int>(initial,
        static (model, id) => (model.Replace && id == 1 ? model with { First = 2 } : model.Veto ? model : model with { First = 0 }, Cmd<int>.None),
        static (_, _, _) => { }, ReviewLifecycleJson.Default.QuitModel, ReviewLifecycleJson.Default.Int32)
        .WithWindows(static model => model.First == 0 ? [] : new[] { new WindowSpec<int, int>(model.First, "Quit " + model.First, model.First) },
            static (_, id) => NavigationRoot<int>.Create(NavigationHistory<int>.Start(id), "quit", _ => new QuitScreen().Map<int>(static message => message), _ => 0), ReviewLifecycleJson.Default.Int32);

    static void Edges(string output, Action<bool, string> check)
    {
        var transcript = new List<string>();
        using (var app = new App(new("Will quit veto", Hidden: true), owner => new RecordingBackend(owner)))
        using (var model = EdgeProgram(new(Second: 0)))
        {
            var backend = (RecordingBackend)app.Backend;
            int before = 0, will = 0, did = 0;
            var replies = new List<bool>();
            app.BeforeQuit += _ => before++;
            app.WillQuit += e =>
            {
                if (++will == 1)
                    e.PreventDefault();
            };
            app.DidQuit += () => did++;
            app.NewWindows(model, new(Visible: false));
            app.RequestNativeQuit(replies.Add);
            PumpEdge(app, () => replies.Count > 0);
            app.RunFor(TimeSpan.FromMilliseconds(80));
            check(before == 1 && will == 1 && did == 0 && backend.Stops == 0 && replies.SequenceEqual([false]), "queued empty reconciliation cannot retry a canceled WillQuit");
            app.Quit();
            app.Run();
            check(before == 2 && will == 2 && did == 1 && backend.Stops == 1, "a later explicit Quit starts a fresh attempt after WillQuit veto");
            transcript.Add($"will veto before={before} will={will} did={did} stops={backend.Stops}");
        }
        using (var app = new App(new("Replacement veto", Hidden: true, QuitOnLastWindow: false), owner => new RecordingBackend(owner)))
        using (var model = EdgeProgram(new(Second: 0, Replace: true)))
        {
            bool? reply = null;
            app.WindowCreated += window => window.Closing += e =>
            {
                if (window.Options.Title == "Quit 2")
                    e.PreventDefault();
            };
            app.NewWindows(model, new(Visible: false));
            app.RequestNativeQuit(value => reply = value);
            PumpEdge(app, () => reply is not null);
            check(reply == false && app.Windows.Count == 1 && app.Windows[0].Options.Title == "Quit 2" && ((RecordingBackend)app.Backend).Stops == 0,
                "a replacement window outside the approved quit set survives and cancels shutdown");
            model.Save(Path.Combine(output, "review-quit-replacement.jsonl"));
            transcript.Add("replacement survived; native reply=false");
        }
        foreach (bool modelWindows in new[] { false, true })
        {
            using var app = new App(new("Terminal notification failure", Hidden: true, QuitOnLastWindow: false), owner => new RecordingBackend(owner));
            using var model = EdgeProgram(new(Second: 0));
            if (modelWindows)
                app.NewWindows(model, new(Visible: false));
            var errors = new List<Exception>();
            var replies = new List<bool>();
            app.UnhandledException += errors.Add;
            app.DidQuit += () => throw new InvalidOperationException("terminal fixture failure");
            app.RequestNativeQuit(replies.Add);
            PumpEdge(app, () => replies.Count > 0);
            check(replies.SequenceEqual([true]) && errors.Count == 1 && ((RecordingBackend)app.Backend).Stops == 1,
                "throwing DidQuit still receives one affirmative native reply after committed shutdown");
            transcript.Add($"terminal failure model={modelWindows} reply=true errors={errors.Count}");
        }
        foreach (bool fromClosing in new[] { false, true })
        {
            using var app = new App(new("Exit during quit", Hidden: true, QuitOnLastWindow: false), owner => new RecordingBackend(owner));
            using var window = app.NewWindow(new(Visible: false), _ => { });
            int closing = 0, will = 0, did = 0;
            app.BeforeQuit += _ =>
            {
                if (!fromClosing)
                    app.Exit(7);
            };
            window.Closing += _ =>
            {
                closing++;
                if (fromClosing)
                    app.Exit(7);
            };
            app.WillQuit += _ => will++;
            app.DidQuit += () => did++;
            app.Quit();
            check(closing == (fromClosing ? 1 : 0) && will == 0 && did == 0 && app.Windows.Contains(window) && app.ExitCode == 7 && ((RecordingBackend)app.Backend).Stops == 1,
                "reentrant Exit bypasses remaining close and quit stages and stops the backend once");
            transcript.Add($"reentrant exit fromClosing={fromClosing} close={closing} will={will} did={did}");
            Environment.ExitCode = 0;
        }
        using var stream = File.Create(Path.Combine(output, "review-quit-edges.json"));
        using var json = new Utf8JsonWriter(stream);
        json.WriteStartArray();
        foreach (string line in transcript)
            json.WriteStringValue(line);
        json.WriteEndArray();
    }

    static void PumpEdge(App app, Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(5))
            app.RunFor(TimeSpan.FromMilliseconds(20));
        if (!ready())
            throw new TimeoutException("Deferred quit edge fixture");
    }

    sealed class RecordingBackend : IAppBackend
    {
        readonly MacApp native;
        internal int Stops;
        internal RecordingBackend(App app) => native = new(app);
        public IWindowBackend CreateWindow(Window window, PageOptions? page) => native.CreateWindow(window, page);
        public void Dispatch(Action action) => native.Dispatch(action);
        public void RunFor(TimeSpan duration) => native.RunFor(duration);
        public void Run()
        {
        }
        public void Quit() => Stops++;
        public void Activate() => throw new InvalidOperationException("Fixture must not activate");
        public void Dispose() => native.Dispose();
    }
}
