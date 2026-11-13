using Kumiko;
using Kumiko.Runtime;
using Kumiko.UI;

static class DebugChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var effects = 0;
        var starts = 0;
        var stops = 0;
        Dispatch<KumikoMessage>? tick = null;
        (KumikoModel, Cmd<KumikoMessage>) Update(KumikoModel m, KumikoMessage message) => message switch
        {
            KumikoMessage.Add => (m with { Count = m.Count + 1 }, Cmd<KumikoMessage>.None),
            KumikoMessage.Text t => (m with { Text = t.Value }, Cmd<KumikoMessage>.None),
            KumikoMessage.Watch => (m with { Watching = !m.Watching }, Cmd<KumikoMessage>.None),
            KumikoMessage.Start => (m with
            {
                Status = "Working"
            }, Cmd<KumikoMessage>.Run("load", _ =>
            {
                Interlocked.Increment(ref effects);
                return Task.FromResult<KumikoMessage>(new KumikoMessage.Finished("Loaded"));
            }, e => new KumikoMessage.Finished(e.Message))),
            KumikoMessage.Finished f => (m with { Status = f.Value }, Cmd<KumikoMessage>.None),
            _ => (m, Cmd<KumikoMessage>.None)
        };
        IEnumerable<Sub<KumikoMessage>> Subs(KumikoModel m)
        {
            if (m.Watching)
                yield return new("watch", send =>
                {
                    starts++;
                    tick = send;
                    return new Stop(() => stops++);
                });
        }
        Program<KumikoModel, KumikoMessage> Make() => new(new(0, "", "Ready", false), Update, View,
            KumikoJson.Default.KumikoModel, KumikoJson.Default.KumikoMessage, Subs);
        using var program = Make();
        using var tester = new Tester(program, new(900, 620, DevMode: true));
        check(!tester.ContainsText("Event history"), "event debugger is absent until explicitly enabled");
        var appId = tester.Get(Role.Button, "Add").Id;
        program.DebugEnabled = true;
        tester.Frame(0);
        check(tester.Get(Role.Button, "Add").Id == appId && program.History.Count == 1,
            "enabling debug preserves app identity and does not create events");
        check(!tester.Get(Role.Button, "Back").Enabled && !tester.Get(Role.Button, "Forward").Enabled,
            "debug navigation is disabled for an empty history");
        for (var i = 0; i < 3; i++)
            tester.Get(Role.Button, "Add").Click();
        Artifact(tester, output, "live");
        tester.Get(Role.Button, "Back").Click();
        check(program.Position == 2 && program.Model.Count == 2 && program.IsPaused && program.History.Count == 4,
            "debug Back restores the displayed model without logging a debugger action");
        check(Math.Abs(tester.Context.Time - program.History[2].Time) < .0001, "debug navigation restores the selected event clock");
        tester.Key(Key.Enter);
        check(program.Position == 1, "history navigation preserves debugger keyboard focus");
        tester.Key(Key.Left, Modifiers.Primary | Modifiers.Alt);
        check(program.Position == 0 && !tester.Get(Role.Button, "Back").Enabled,
            "debug keyboard shortcuts stop at the initial model");
        tester.Key(Key.Right, Modifiers.Primary | Modifiers.Alt);
        check(program.Position == 1 && program.Model.Count == 1, "debug Forward follows the selected branch");
        Artifact(tester, output, "paused");
        tester.Get(Role.Button, "Show events").Click();
        tester.Get(Role.Button, "Event #2: add").Click();
        check(program.Position == 2 && tester.ContainsText("Button Add") && tester.ContainsText("key=add"),
            "event selection shows the originating semantic control");
        tester.Get(Role.Button, "Message").Click();
        check(tester.ContainsText("\"type\": \"add\""), "debugger displays the serialized message payload");
        tester.Get(Role.Button, "Changes").Click();
        check(tester.ContainsText("/Count") && tester.ContainsText("before") && tester.ContainsText("after"),
            "debugger displays model changes for the selected event");
        tester.Get(Role.Button, "Model").Click();
        check(tester.ContainsText("\"Count\": 2"), "debugger inspects the selected model");
        var line = tester.Context.Nodes.ToArray().Single(n => n.Key == "scroll-line.0").Rect.Y;
        var panel = tester.Context.Nodes.ToArray().Single(n => n.Key == "kumiko.debugger").Rect;
        tester.MoveTo(panel.X + 4, panel.Y + 4);
        tester.Scroll(200);
        check(tester.Context.Nodes.ToArray().Single(n => n.Key == "scroll-line.0").Rect.Y == line,
            "wheel input over the debugger cannot scroll underlying app content");
        Artifact(tester, output, "expanded");
        tester.Get(Role.Button, "Event #1: add").Click();
        tester.Get(Role.Button, "Hide events").Click();
        tester.Get(Role.Button, "Add").Click();
        check(program.Position == 4 && program.History[^1].Parent == 1 && program.Model.Count == 2,
            "editing a rewound app creates a new history branch");
        tester.Get(Role.Button, "Back").Click();
        tester.Get(Role.Button, "Forward").Click();
        check(program.Position == 4, "forward returns along the newly chosen branch");
        tester.Get(Role.Button, "Show events").Click();
        tester.Get(Role.Button, "Event #3: add").Click();
        tester.Get(Role.Button, "Back").Click();
        tester.Get(Role.Button, "Forward").Click();
        check(program.Position == 3 && program.Model.Count == 3, "the old future remains navigable after branching");
        tester.Get(Role.Button, "Latest").Click();
        check(program.Position == 4 && program.IsPaused, "Latest selects the newest event without resuming effects");
        Artifact(tester, output, "branch");
        tester.Get(Role.Button, "Hide events").Click();
        tester.Get(Role.Button, "Resume").Click();
        tester.Get(Role.Button, "Watch").Click();
        check(starts == 1, "debugger resumes model-driven subscriptions");
        tester.Get(Role.Button, "Pause").Click();
        var count = program.Model.Count;
        tick!(new KumikoMessage.Add());
        tester.Frame(0);
        check(stops == 1 && program.Model.Count == count && program.IsPaused,
            "Pause disposes subscriptions and ignores stale source callbacks");
        tester.Get(Role.Button, "Resume").Click();
        check(starts == 2, "Resume restarts subscriptions for the displayed model");
        tick!(new KumikoMessage.Add());
        tester.Frame(0);
        check(program.Model.Count == count + 1, "resumed sources deliver new events");
        tester.Get(Role.Button, "Load").Click();
        var request = program.History.Last(h => h.Message is KumikoMessage.Start).Id;
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Debug command did not finish");
        tester.Frame(0);
        program.Step(request);
        tester.Get(Role.Button, "Show events").Click();
        check(tester.ContainsText("Commands: load") && effects == 1,
            "inspecting command-start events shows their descriptions without re-executing work");
        tester.Get(Role.Button, "Forward").Click();
        check(program.Model.Status == "Loaded" && effects == 1, "forward displays recorded command results without repeating effects");
        program.Save(Path.Combine(output, "debug-session.jsonl"));
        foreach (var (width, height, dark, name) in new[] { (420, 300, false, "narrow"), (900, 620, true, "dark") })
        {
            tester.Context.Theme = dark ? Theme.DarkTheme : Theme.Light;
            tester.Resize(width, height);
            check(tester.Get(Role.Button, "Back").Rect.Right <= width && tester.Get(Role.Button, "Hide events").Rect.Right <= width,
                "debug controls remain reachable after resizing and theme changes");
            Artifact(tester, output, name);
            check(tester.Context.Warnings.Count == 0, "debugger has no layout or contrast warnings at its supported sizes");
        }
        check(!tester.Context.Animating && tester.Context.NextFrame is null, "paused debugger does not schedule polling frames");
        var historyCount = program.History.Count;
        program.DebugEnabled = false;
        tester.Frame(0);
        check(!tester.ContainsText("Event history") && program.History.Count == historyCount && program.IsPaused,
            "disabling debug removes its UI without changing history or resuming effects");
        CrmDebugChecks.Run(output, check);
        using var cli = Make();
        Dev.Run("Debug E2E", 600, 420, cli,
            ["--kumiko-debug", "--kumiko-scale=1", "--kumiko-dev", "--kumiko-script=click:Add;click:Add;click:Back",
                "--kumiko-snapshot=" + Path.Combine(output, "debug-cli.png"), "--kumiko-tree=" + Path.Combine(output, "debug-cli.txt")]);
        check(cli.DebugEnabled && cli.Position == 1 && cli.Model.Count == 1 && cli.IsPaused,
            "the per-app debug CLI flag supports offscreen event navigation");
    }

    static void View(Context c, KumikoModel model, Dispatch<KumikoMessage> send)
    {
        if (c.Shortcut(Key.Enter, Modifiers.None))
            send(new KumikoMessage.Add());
        using var page = c.Div(key: "debug-app").SizeFull().Padding(Space.S4).Gap(Space.S2);
        c.Text($"Count: {model.Count}");
        c.Text(model.Status);
        using (c.Div(key: "actions").Row().Gap(Space.S2))
        {
            if (c.Button("Add", key: "add").Clicked)
                send(new KumikoMessage.Add());
            if (c.Button("Watch", key: "watch").Clicked)
                send(new KumikoMessage.Watch());
            if (c.Button("Load", key: "load").Clicked)
                send(new KumikoMessage.Start());
        }
        c.TextField(model.Text, text => send(new KumikoMessage.Text(text)), new(Name: "Search"), key: "search");
        using (c.Div(key: "app-scroll").FillWidth().Grow().Scroll())
            for (var i = 0; i < 40; i++)
                c.Text($"App row {i}", key: "scroll-line." + i);
    }

    static void Artifact(Tester tester, string output, string name)
    {
        var stem = Path.Combine(output, "debug-" + name);
        tester.Screenshot(stem + ".png");
        File.WriteAllText(stem + ".txt", tester.Dump());
        tester.WriteWarnings(stem + ".warnings.json");
    }

    sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}
