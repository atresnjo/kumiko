using System.Text.Json;
using Kumiko;
using Kumiko.Runtime;
using Kumiko.UI;

static class HotReloadChecks
{
    static string label = "Before edit";

    public static void Run(string output, Action<bool, string> check)
    {
        var builds = 0;
        void View(Context c)
        {
            builds++;
            using var root = c.Div(key: "hot").SizeFull().Padding(Space.S4);
            c.Text(label);
        }
        using (var app = new App(new("Hot reload E2E", Hidden: true)))
        using (var window = app.NewWindow(new(Title: "Hot reload E2E", Width: 320, Height: 200, Visible: false), View))
        {
            app.RunFor(TimeSpan.FromMilliseconds(60));
            File.WriteAllText(Path.Combine(output, "hot-reload-before.txt"), Texts(window));
            var idle = builds;
            app.RunFor(TimeSpan.FromMilliseconds(120));
            check(builds == idle, "idle hot-reloadable windows do not rebuild");
            label = "After edit";
            Task.Run(() => HotReload.UpdateApplication(null)).GetAwaiter().GetResult();
            app.RunFor(TimeSpan.FromMilliseconds(60));
            File.WriteAllText(Path.Combine(output, "hot-reload-after.txt"), Texts(window));
            check(builds > idle && Texts(window).Contains("After edit", StringComparison.Ordinal),
                "a worker-thread metadata update redraws open windows without input");
        }
        HotReload.UpdateApplication(null);
        check(true, "metadata updates after App disposal are ignored");

        var state = Path.Combine(output, "hot-reload-state.json");
        File.Delete(state);
        var starts = 0;
        (KumikoModel, Cmd<KumikoMessage>) Update(KumikoModel m, KumikoMessage msg) => (msg switch
        {
            KumikoMessage.Add => m with { Count = m.Count + 1 },
            KumikoMessage.Watch => m with { Watching = !m.Watching },
            _ => m
        }, Cmd<KumikoMessage>.None);
        IEnumerable<Sub<KumikoMessage>> Subs(KumikoModel m) => m.Watching
            ? [new Sub<KumikoMessage>("watch", _ =>
            {
                starts++;
                return new Stop();
            })]
            : [];
        Program<KumikoModel, KumikoMessage> Make() => new(new(0, "", "Ready", false), Update, KumikoView,
            KumikoJson.Default.KumikoModel, KumikoJson.Default.KumikoMessage, Subs);

        using (var app = new App(new("Hot reload E2E", Hidden: true)))
        {
            var program = Make();
            var window = app.NewWindow(new(Title: "Hot reload E2E", Width: 320, Height: 200, Visible: false), program.View);
            check(!HotReload.Persist(program, window, state) && program.Model.Count == 0,
                "a missing hot reload state file starts from the initial model");
            program.Attach(window);
            program.Send(new KumikoMessage.Add());
            program.Send(new KumikoMessage.Add());
            program.Send(new KumikoMessage.Watch());
            app.RunFor(TimeSpan.FromMilliseconds(300));
            var saved = JsonSerializer.Deserialize(File.ReadAllText(state), KumikoJson.Default.KumikoModel);
            check(saved is { Count: 2, Watching: true }, "model changes are saved while the window stays open");
            program.Send(new KumikoMessage.Add());
            window.Capture();
            window.Close();
            saved = JsonSerializer.Deserialize(File.ReadAllText(state), KumikoJson.Default.KumikoModel);
            check(saved is { Count: 3 }, "the last model change is saved when the window closes");
        }

        using (var app = new App(new("Hot reload E2E", Hidden: true)))
        {
            var program = Make();
            var window = app.NewWindow(new(Title: "Hot reload E2E", Width: 320, Height: 200, Visible: false), program.View);
            var restored = HotReload.Persist(program, window, state);
            program.Attach(window);
            app.RunFor(TimeSpan.FromMilliseconds(60));
            check(restored && program.Model is { Count: 3, Watching: true } && program.History.Count == 1 &&
                !program.IsPaused && starts == 2 && Texts(window).Contains("Count: 3", StringComparison.Ordinal),
                "a restart restores the saved model live, with subscriptions and a fresh history");
            program.Send(new KumikoMessage.Add());
            app.RunFor(TimeSpan.FromMilliseconds(60));
            check(program.History is [_, { Parent: 0 }] && program.Model.Count == 4, "messages after a restore branch from the restored step");
            window.Close();
        }

        var valid = File.ReadAllText(state);
        string[] incompatible =
        [
            valid.Replace("\"Text\":\"\",", "", StringComparison.Ordinal),
            valid.Replace("{", "{\"Removed\":1,", StringComparison.Ordinal),
            valid.Replace("\"Count\":4", "\"Count\":\"4\"", StringComparison.Ordinal),
            "{"
        ];
        using var fresh = Make();
        using var tester = new Tester(fresh);
        var rejected = incompatible.All(json =>
        {
            File.WriteAllText(state, json);
            return json != valid && !fresh.RestoreModel(state);
        });
        File.WriteAllText(state, valid);
        check(rejected && fresh.Model.Count == 0 && fresh.History.Count == 1,
            "missing, extra, retyped or malformed saved fields start from the initial model");
    }

    static void KumikoView(Context c, KumikoModel m, Dispatch<KumikoMessage> send)
    {
        using var root = c.Div(key: "hot-kumiko").SizeFull().Padding(Space.S4);
        c.Text($"Count: {m.Count}");
    }

    static string Texts(Window window) =>
        string.Join('\n', window.Context!.Nodes.ToArray().Select(n => n.Text).OfType<string>());

    sealed class Stop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
