using System.Text.Json.Serialization;
using Kumiko;
using Kumiko.Runtime;
using Kumiko.UI;

static class KumikoChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var owner = Environment.CurrentManagedThreadId;
        var effects = 0;
        var starts = 0;
        var stops = 0;
        Dispatch<KumikoMessage>? tick = null;
        var release = new TaskCompletionSource<KumikoMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        (KumikoModel, Cmd<KumikoMessage>) Update(KumikoModel m, KumikoMessage msg)
        {
            if (Environment.CurrentManagedThreadId != owner)
                throw new InvalidOperationException("Update escaped the UI thread");
            return msg switch
            {
                KumikoMessage.Add => (m with { Count = m.Count + 1 }, Cmd<KumikoMessage>.None),
                KumikoMessage.Text x => (m with { Text = x.Value }, Cmd<KumikoMessage>.None),
                KumikoMessage.Start => (m with
                {
                    Status = "Working"
                }, Cmd<KumikoMessage>.Run("load", async _ =>
                {
                    Interlocked.Increment(ref effects);
                    return await release.Task;
                }, e => new KumikoMessage.Finished(e.Message))),
                KumikoMessage.Fail => (m, Cmd<KumikoMessage>.Run("fail", _ => throw new IOException("offline"), e => new KumikoMessage.Finished(e.Message))),
                KumikoMessage.Finished x => (m with { Status = x.Value }, Cmd<KumikoMessage>.None),
                KumikoMessage.Watch => (m with { Watching = !m.Watching }, Cmd<KumikoMessage>.None),
                _ => throw new ArgumentOutOfRangeException(nameof(msg))
            };
        }
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
        using var tester = new Tester(program, new(Width: 480, Height: 380, DevMode: true));
        var path = Path.Combine(output, "kumiko-session.jsonl");
        program.Record(path);
        tester.Get(Role.Button, "Add").Click();
        check(program.Model.Count == 1 && tester.HasText("Count: 1") && program.History.Count == 2,
            "Kumiko messages update the same frame exactly once");
        tester.Get(Role.TextField, "Search").Click();
        tester.TypeText("Stripe");
        tester.Get(Role.Button, "Watch").Click();
        tester.Settle();
        check(starts == 1, "model subscriptions start once across view rebuilds");
        Task.Run(() => tick!(new KumikoMessage.Add())).GetAwaiter().GetResult();
        tester.Frame();
        check(tester.HasText("Count: 2"), "worker subscription results are applied by the owning UI thread");
        tester.Get(Role.Button, "Watch").Click();
        tick!(new KumikoMessage.Add());
        tester.Frame();
        check(stops == 1 && program.Model.Count == 2, "removed subscription callbacks are ignored");
        tester.Get(Role.Button, "Load").Click();
        release.SetResult(new KumikoMessage.Finished("Loaded"));
        Wait(program);
        tester.Frame();
        tester.Get(Role.Button, "Fail").Click();
        Wait(program);
        tester.Frame();
        check(program.Model.Status == "offline", "command failures become messages without escaping callbacks");
        tester.MoveTo(-100, -100);
        tester.Context.Blur();
        tester.Frame(0);
        var tree = tester.Dump();
        Artifact(tester, output, "live");
        program.Save(path);
        using var replay = Make();
        using var replayTester = new Tester(replay, new(Width: 480, Height: 380, DevMode: true));
        var effectCount = effects;
        replayTester.Replay(path);
        check(replayTester.Dump() == tree && effects == effectCount && starts == 1,
            "recorded sessions reproduce the tree without running effects or subscriptions");
        check(tester.Render().Bytes.SequenceEqual(replayTester.Render().Bytes), "replay reproduces offscreen pixels with transient input cleared");
        Artifact(replayTester, output, "replay");
        replayTester.Step(1);
        check(replay.Model.Count == 1 && replay.Model.Text == "" && replayTester.Context.Accessibility.Single(n => n.Role == Role.TextField).Value == "",
            "stepping restores model-owned text along with the displayed count");
        Artifact(replayTester, output, "step");
        var oldEnd = replay.History.Count - 1;
        replayTester.Step(oldEnd);
        check(replayTester.Dump() == tree, "stepping forward restores the recorded result");
        replayTester.Step(1);
        replayTester.Get(Role.Button, "Add").Click();
        check(replay.Model.Count == 2 && replay.History[^1].Parent == 1 && replay.History.Count == oldEnd + 2,
            "input while rewound creates a branch and retains the previous future");
        replay.Save(Path.Combine(output, "kumiko-branch.jsonl"));
        Artifact(replayTester, output, "branch");
        var source = program.History[1].Source;
        check(source is { Role: Role.Button, Name: "Add", Key: "add" } && source.Path.Contains("add", StringComparison.Ordinal),
            "message origins include the semantic control and stable path");
        var before = replay.Model;
        try
        {
            replay.Step(9999);
            throw new Exception("Invalid step accepted");
        }
        catch (ArgumentOutOfRangeException)
        {
            check(replay.Model == before, "invalid history navigation is atomic");
        }
        var corrupt = Path.Combine(output, "kumiko-corrupt.jsonl");
        File.WriteAllText(corrupt, File.ReadAllText(path).Replace("\"Count\":1", "\"Count\":999", StringComparison.Ordinal));
        try
        {
            replay.Replay(corrupt);
            throw new Exception("Divergent session accepted");
        }
        catch (InvalidDataException)
        {
            check(replay.Model == before, "replay rejects divergence without changing the selected model");
        }
        check(!tester.Context.Animating && tester.Context.NextFrame is null, "idle Kumiko programs do not poll for frames");

        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = Make();
        using var cancelTester = new Tester(canceled);
        cancelTester.Get(Role.Button, "Watch").Click();
        var staleTick = tick!;
        cancelTester.Step(0);
        staleTick(new KumikoMessage.Add());
        cancelTester.Frame();
        check(canceled.Model.Count == 0 && stops == 2, "history navigation disposes and invalidates subscriptions");
        cancelTester.Get(Role.Button, "Load").Click();
        cancelTester.Step(0);
        release.SetResult(new KumikoMessage.Finished("Stale result"));
        Wait(canceled);
        cancelTester.Frame();
        check(canceled.Model.Status == "Ready", "completions from a discarded timeline cannot mutate the selected model");
        using var branchReplay = Make();
        branchReplay.Replay(Path.Combine(output, "kumiko-branch.jsonl"));
        using var branchTester = new Tester(branchReplay);
        check(branchReplay.Model.Count == 2, "replay follows the recorded branch parent");
        branchTester.Step(oldEnd);
        check(branchReplay.Model.Status == "offline", "replay retains the original future for navigation");
        using var native = Make();
        using var app = new App(new("Kumiko E2E", Hidden: true));
        using var window = app.NewWindow(new(Title: "Kumiko E2E", Width: 480, Height: 380, Visible: false), native.View);
        native.Attach(window);
        Task.Run(() => native.Send(new KumikoMessage.Add())).GetAwaiter().GetResult();
        app.RunFor(TimeSpan.FromMilliseconds(60));
        check(native.Model.Count == 1 && window.Context!.Nodes.ToArray().Any(n => n.Text == "Count: 1") && !window.IsVisible,
            "worker messages wake a hidden native host through App.Dispatch");
    }

    static void Wait(Program<KumikoModel, KumikoMessage> program)
    {
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Kumiko command did not complete");
    }

    static void View(Context c, KumikoModel m, Dispatch<KumikoMessage> send)
    {
        using var root = c.Div(key: "kumiko").SizeFull().Padding(Space.S4).Gap(Space.S2);
        c.Text($"Count: {m.Count}");
        c.Text(m.Status);
        c.TextField(m.Text, text => send(new KumikoMessage.Text(text)), new(Name: "Search"), key: "search");
        if (c.Button("Add", key: "add").Clicked)
            send.Map((int _value) => (KumikoMessage)new KumikoMessage.Add())(0);
        if (c.Button("Load", key: "load").Clicked)
            send(new KumikoMessage.Start());
        if (c.Button("Fail", key: "fail").Clicked)
            send(new KumikoMessage.Fail());
        if (c.Button("Watch", key: "watch").Clicked)
            send(new KumikoMessage.Watch());
    }

    static void Artifact(Tester tester, string output, string name)
    {
        var stem = Path.Combine(output, "kumiko-" + name);
        tester.Screenshot(stem + ".png");
        File.WriteAllText(stem + ".txt", tester.Dump());
        tester.WriteWarnings(stem + ".warnings.json");
    }

    sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}

internal sealed record KumikoModel(int Count, string Text, string Status, bool Watching);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KumikoMessage.Add), "add")]
[JsonDerivedType(typeof(KumikoMessage.Text), "text")]
[JsonDerivedType(typeof(KumikoMessage.Start), "start")]
[JsonDerivedType(typeof(KumikoMessage.Fail), "fail")]
[JsonDerivedType(typeof(KumikoMessage.Finished), "finished")]
[JsonDerivedType(typeof(KumikoMessage.Watch), "watch")]
internal abstract record KumikoMessage
{
    public sealed record Add : KumikoMessage;
    public sealed record Text(string Value) : KumikoMessage;
    public sealed record Start : KumikoMessage;
    public sealed record Fail : KumikoMessage;
    public sealed record Finished(string Value) : KumikoMessage;
    public sealed record Watch : KumikoMessage;
}
[JsonSerializable(typeof(KumikoModel))]
[JsonSerializable(typeof(KumikoMessage))]
internal partial class KumikoJson : JsonSerializerContext;
