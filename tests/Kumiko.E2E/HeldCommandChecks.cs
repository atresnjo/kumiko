using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Held commands: the test, not a worker, decides when and how each started command answers.</summary>
static class HeldCommandChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var worked = new ConcurrentQueue<string>();
        var cancelled = new ConcurrentQueue<string>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Cmd<HeldMessage> Work(string name) => Cmd<HeldMessage>.Run(name, async token =>
        {
            worked.Enqueue(name);
            await Task.Yield();
            return new HeldMessage.Done(name);
        }, error => new HeldMessage.Failed(name, error.Message));
        Cmd<HeldMessage> Search(string query) => Cmd<HeldMessage>.Debounce("search", TimeSpan.Zero, async token =>
        {
            worked.Enqueue("search " + query);
            try
            {
                if (query == "slow")
                    await gate.Task.WaitAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                cancelled.Enqueue(query);
                throw;
            }
            return new HeldMessage.Done("search " + query);
        }, error => new HeldMessage.Failed("search " + query, error.Message));
        (HeldModel, Cmd<HeldMessage>) Update(HeldModel m, HeldMessage msg) => msg switch
        {
            HeldMessage.Start s => (m with
            {
                Log = m.Log.Add("start " + string.Join("+", s.Names))
            }, Cmd<HeldMessage>.Batch([.. s.Names.Select(Work)])),
            HeldMessage.StartChild => (m, Cmd<HeldChild>.Run("child", static _ => Task.FromResult<HeldChild>(new HeldChild.Answered()),
                error => new HeldChild.Broke(error.Message)).Map<HeldMessage>(child => new HeldMessage.FromChild(child))),
            HeldMessage.Query q => (m with { Query = q.Text }, Search(q.Text)),
            HeldMessage.Done d => (m with { Log = m.Log.Add("done " + d.Name) }, Cmd<HeldMessage>.None),
            HeldMessage.Failed f => (m with { Log = m.Log.Add($"failed {f.Name}: {f.Error}") }, Cmd<HeldMessage>.None),
            HeldMessage.FromChild { Child: HeldChild.Broke b } => (m with { Log = m.Log.Add("child broke: " + b.Error) }, Cmd<HeldMessage>.None),
            HeldMessage.FromChild => (m with { Log = m.Log.Add("child answered") }, Cmd<HeldMessage>.None),
            _ => throw new ArgumentOutOfRangeException(nameof(msg))
        };
        using var program = new Program<HeldModel, HeldMessage>(new("", []), Update, (c, m, send) =>
        {
            using var root = c.Div(key: "root").SizeFull().Column().Gap(8).Padding(12);
            c.TextField(m.Query, text => send(new HeldMessage.Query(text)), new(Name: "Query"), key: "query");
            using (c.Div(key: "panel-a").Semantics(Role.Group, "Panel A").Padding(4))
                c.Text("Ready", key: "ready");
            using (c.Div(key: "panel-b").Semantics(Role.Group, "Panel B").Padding(4))
                c.Text("Other", key: "other");
            foreach (var line in m.Log.TakeLast(3))
                c.Text(line, key: line);
        }, HeldJson.Default.HeldModel, HeldJson.Default.HeldMessage);
        program.HoldCommands = true;
        using var tester = new Tester(program, new(Width: 360, Height: 260, DevMode: true));
        tester.Frame(0);

        program.Send(new HeldMessage.Start(["load", "log"]));
        tester.Settle();
        check(program.Held.Names.SequenceEqual(["load", "log"]) && worked.IsEmpty && program.PendingCommands == 0,
            "held commands wait in start order without running work or counting as pending");
        check(program.History[^1].Commands.SequenceEqual(["load", "log"]), "holding records the same command names in history");
        check(Throws(() => program.Held.Expect("log", "load"), "load, log") && Throws(() => program.Held.Expect("load"), "load, log")
            && Throws(() => program.Held.Get("save"), "load, log"), "Expect and Get reject reordered, missing or unknown names and list what is held");
        program.Held.Expect("load", "log");

        var load = program.Held.Get("load");
        load.Resolve(new HeldMessage.Done("load"));
        check(!program.Model.Log.Contains("done load"), "Resolve queues the answer instead of applying it inside the call");
        tester.Settle();
        check(program.Model.Log[^1] == "done load" && program.History[^1].Message is HeldMessage.Done && program.Held.Names.SequenceEqual(["log"]),
            "a resolved answer goes through update and history like a finished worker");
        check(Throws(() => load.Resolve(new HeldMessage.Done("again")), "already settled"), "a command cannot answer twice");
        program.Held.Get("log").Fail(new IOException("disk full"));
        tester.Settle();
        check(program.Model.Log[^1] == "failed log: disk full", "Fail delivers the command's own error mapping");

        program.Send(new HeldMessage.StartChild());
        tester.Settle();
        program.Held.Get("child").Fail(new TimeoutException("late"));
        tester.Settle();
        check(program.Model.Log[^1] == "child broke: late", "a mapped child command fails through the child mapping and the parent wrapper");

        tester.Get(Role.TextField, "Query").Fill("a");
        var first = program.Held.Get("search");
        tester.Get(Role.TextField, "Query").Fill("ab");
        check(program.Held.Names.SequenceEqual(["search"]) && Throws(() => first.Resolve(new HeldMessage.Done("stale")), "replaced"),
            "a newer held command with the same key replaces the older one");
        check(tester.Get(Role.TextField, "Query").Value == "ab", "Value reads the control's accessible value");
        program.Held.Get("search").Resolve(new HeldMessage.Done("search ab"));
        tester.Settle();
        check(program.Model.Log[^1] == "done search ab", "the latest keyed command still answers");

        tester.Get(Role.TextField, "Query").Fill("slow");
        program.Held.Get("search").Run();
        if (!SpinWait.SpinUntil(() => worked.Contains("search slow"), TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Released search never started.");
        tester.Get(Role.TextField, "Query").Fill("fast");
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Cancelled search never finished.");
        tester.Settle();
        check(cancelled.SequenceEqual(["slow"]) && program.Held.Names.SequenceEqual(["search"]) && !program.Model.Log.Contains("done search slow"),
            "holding a newer keyed command cancels the running one with the same key");
        program.Held.Get("search").Resolve(new HeldMessage.Done("search fast"));
        tester.Settle();

        program.Send(new HeldMessage.Start(["sync"]));
        tester.Settle();
        program.Held.Get("sync").Run();
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0 && worked.Contains("sync"), TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Released command never finished.");
        tester.Settle();
        check(program.Model.Log[^1] == "done sync" && program.Held.Names.Count == 0, "Run lets the real work answer through the normal worker path");

        program.Send(new HeldMessage.Start(["late"]));
        tester.Settle();
        var late = program.Held.Get("late");
        check(Throws(() => program.HoldCommands = false, "late") && program.HoldCommands && program.Held.Names.SequenceEqual(["late"]),
            "holding cannot be turned off while commands are held");
        check(Throws(() => Task.Run(() => late.Resolve(new HeldMessage.Done("late"))).GetAwaiter().GetResult(), "UI thread"),
            "held commands settle only on the owning thread");
        tester.Step(0);
        check(program.Held.Names.Count == 0 && Throws(() => late.Resolve(new HeldMessage.Done("late")), "discarded"),
            "history navigation discards held commands");
        program.HoldCommands = false;
        program.Send(new HeldMessage.Start(["live"]));
        tester.Frame();
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0 && worked.Contains("live"), TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Unheld command never finished.");
        tester.Settle();
        check(program.Model.Log[^1] == "done live" && program.Held.Names.Count == 0, "with holding off, commands run as before");

        check(tester.Get(Role.Group, "Panel A").HasText("Ready") && !tester.Get(Role.Group, "Panel B").HasText("Ready"),
            "HasText only matches text inside the control");
        check(tester.Nth(Role.Group, 1).HasText("Other") && Throws(() => tester.Nth(Role.Group, 2), "found 2"),
            "Nth picks one of several controls in tree order and rejects an out-of-range index");
        tester.Screenshot(Path.Combine(output, "held-commands.png"));
        File.WriteAllText(Path.Combine(output, "held-commands.txt"), tester.Dump());
    }

    static bool Throws(Action action, string detail)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException error)
        {
            return error.Message.Contains(detail, StringComparison.Ordinal);
        }
    }
}

internal sealed record HeldModel(string Query, ImmutableArray<string> Log);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeldMessage.Start), "start")]
[JsonDerivedType(typeof(HeldMessage.StartChild), "start-child")]
[JsonDerivedType(typeof(HeldMessage.Query), "query")]
[JsonDerivedType(typeof(HeldMessage.Done), "done")]
[JsonDerivedType(typeof(HeldMessage.Failed), "failed")]
[JsonDerivedType(typeof(HeldMessage.FromChild), "from-child")]
internal abstract record HeldMessage
{
    public sealed record Start(ImmutableArray<string> Names) : HeldMessage;
    public sealed record StartChild : HeldMessage;
    public sealed record Query(string Text) : HeldMessage;
    public sealed record Done(string Name) : HeldMessage;
    public sealed record Failed(string Name, string Error) : HeldMessage;
    public sealed record FromChild(HeldChild Child) : HeldMessage;
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HeldChild.Answered), "answered")]
[JsonDerivedType(typeof(HeldChild.Broke), "broke")]
internal abstract record HeldChild
{
    public sealed record Answered : HeldChild;
    public sealed record Broke(string Error) : HeldChild;
}

[JsonSerializable(typeof(HeldModel))]
[JsonSerializable(typeof(HeldMessage))]
internal partial class HeldJson : JsonSerializerContext;
