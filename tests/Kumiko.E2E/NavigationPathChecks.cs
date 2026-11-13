using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Kumiko.Runtime;
using Kumiko.UI;

static class NavigationPathChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        using var program = PathProbeApp.CreateProgram();
        using var tester = new Tester(program, new(520, 420, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Get(Role.Button, "Open left").Click();
        tester.Get(Role.Button, "Open right").Click();
        tester.Get(Role.Button, "Edit left").Click();
        tester.Get(Role.Button, "Edit right").Click();
        check(tester.HasText("Left: saved draft") && tester.HasText("Right: 42"),
            "two generic paths with colliding entry type names render independent typed state");
        var revision = program.Model.Left.Revision;
        tester.Get(Role.Button, "Edit left").Click();
        check(program.Model.Left.Revision == revision && program.Model.Left.NextId == 2,
            "editing an entry preserves path revision and allocation");
        tester.Get(Role.Button, "Return left").Click();
        check(program.Model.Pending is not null && program.Model.Left.Entries.Length == 2,
            "a generic pending confirmation retains the proposed path without committing it");
        var model = Path.Combine(output, "path-model.json");
        program.SaveModel(model);
        var session = Path.Combine(output, "path-session.jsonl");
        program.Save(session);
        tester.MoveTo(-100, -100);
        tester.Context.Blur();
        tester.Frame(0);
        Capture(tester, output, "path-live");
        var expected = tester.Dump();

        using var replay = PathProbeApp.CreateProgram();
        using var replayTester = new Tester(replay, new(520, 420, DevMode: true));
        replayTester.Context.ReduceMotion = true;
        replay.Replay(session);
        replayTester.Frame(0);
        check(replayTester.Dump() == expected && replay.Model.Left.Top.Note == "saved draft" && replay.Model.Right.Top.Count == 42,
            "generic paths, distinct entry payloads and pending confirmation replay with generated metadata");
        check(replay.PendingCommands == 0 && replay.Model.Pending is not null,
            "generic path replay starts no synthetic work or confirmation");
        Capture(replayTester, output, "path-replay");
        replayTester.Get(Role.Button, "Confirm left").Click();
        check(replay.Model.Left.Entries.Length == 1 && replay.Model.Left.Revision == revision + 1,
            "a current generic confirmation commits its versioned desired path");
        check(replay.RestoreModel(model), "a model containing generic paths restores through generated JSON metadata");
        replayTester.Frame(0);
        check(replay.Model.Pending is not null && replay.History.Count == 1 && replay.PendingCommands == 0,
            "restore installs a generic pending path silently");
        Capture(replayTester, output, "path-restored");
        var request = replay.Model.Pending!.Request;
        replayTester.Get(Role.Button, "Open left").Click();
        replay.Send(new PathProbeMsg.Confirm(request));
        replayTester.Frame(0);
        check(replay.Model.Left.Entries.Length == 3 && replay.Model.Left.Top.Note == "",
            "a stale generic confirmation cannot discard a newly allocated visit");

        foreach (var (name, mutate) in InvalidModels())
        {
            var json = JsonNode.Parse(File.ReadAllText(model))!;
            mutate(json);
            var invalid = Path.Combine(output, "path-invalid-" + name + ".json");
            File.WriteAllText(invalid, json.ToJsonString());
            var before = replay.Model;
            var count = replay.History.Count;
            check(!replay.RestoreModel(invalid) && ReferenceEquals(before, replay.Model) && replay.History.Count == count,
                "invalid generic " + name + " is rejected before installing native or model state");
        }
        tester.Get(Role.Button, "Confirm left").Click();
        tester.Get(Role.Button, "Open left").Click();
        var back = program.Model.Left.RequestTo(0, "left");
        tester.Get(Role.Button, "Open left").Click();
        program.Send(new PathProbeMsg.Back(back));
        tester.Frame(0);
        check(program.Model.Left.Entries.Length == 3, "a stale generic Back request cannot pop newer visits");
        program.Send(new PathProbeMsg.Back(program.Model.Left.RequestTo(0, "other-stack")));
        tester.Frame(0);
        check(program.Model.Left.Entries.Length == 3, "a generic Back request cannot cross stack ownership");
    }

    static IEnumerable<(string Name, Action<JsonNode> Mutate)> InvalidModels()
    {
        yield return ("duplicate-id", j => j["Left"]!["Entries"]![1]!["Id"] = 0);
        yield return ("allocator", j => j["Left"]!["NextId"] = 1);
        yield return ("revision", j => j["Left"]!["Revision"] = -1);
        yield return ("root-shape", j => j["Left"]!["Entries"]![0]!["Route"] = 1);
        yield return ("entry-state", j => j["Right"]!["Entries"]![1]!["Count"] = "invalid");
    }

    static void Capture(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        if (tester.Context.Warnings.Count != 0)
            throw new InvalidOperationException("Generic path fixture rendered with warnings.");
    }
}

enum ProbeRoute
{
    Root, Detail
}

static class MetadataLeft
{
    internal sealed record Entry(int Id, ProbeRoute Route, string Note = "") : INavigationEntry<ProbeRoute, Entry>
    {
        public static Entry Create(int id, ProbeRoute route) => new(id, route);
        public static bool IsValidPath(ImmutableArray<Entry> entries) => entries[0].Route == ProbeRoute.Root &&
            entries.Skip(1).All(e => e.Route == ProbeRoute.Detail) && entries.All(e => e.Note is not null);
    }
}

static class MetadataRight
{
    internal sealed record Entry(int Id, ProbeRoute Route, int Count = 0) : INavigationEntry<ProbeRoute, Entry>
    {
        public static Entry Create(int id, ProbeRoute route) => new(id, route);
        public static bool IsValidPath(ImmutableArray<Entry> entries) => entries[0].Route == ProbeRoute.Root &&
            entries.Skip(1).All(e => e.Route == ProbeRoute.Detail);
    }
}

sealed record PathProbeModel
{
    public NavigationPath<ProbeRoute, MetadataLeft.Entry> Left { get; init; } = NavigationPath<ProbeRoute, MetadataLeft.Entry>.Start(ProbeRoute.Root);
    public NavigationPath<ProbeRoute, MetadataRight.Entry> Right { get; init; } = NavigationPath<ProbeRoute, MetadataRight.Entry>.Start(ProbeRoute.Root);
    public PendingNavigation<ProbeRoute, MetadataLeft.Entry>? Pending
    {
        get; init;
    }
}

abstract partial record PathProbeMsg
{
    public sealed record OpenLeft : PathProbeMsg;
    public sealed record OpenRight : PathProbeMsg;
    public sealed record EditLeft : PathProbeMsg;
    public sealed record EditRight : PathProbeMsg;
    public sealed record ReturnLeft : PathProbeMsg;
    public sealed record Confirm(BackRequest Request) : PathProbeMsg;
    public sealed record Back(BackRequest Request) : PathProbeMsg;
}

sealed partial class PathProbeApp : IKumiko<PathProbeModel, PathProbeMsg>
{
    public static PathProbeModel Init => new();
    public static NavigationRoot<PathProbeMsg> Root(PathProbeModel model) => NavigationRoot<PathProbeMsg>.Create(
        NavigationHistory<string>.Start("paths"), "app", _ => new PathProbeView(model).Map<PathProbeMsg>(static m => m),
        static _ => throw new InvalidOperationException("One probe screen."));
    public static (PathProbeModel, Cmd<PathProbeMsg>) Update(PathProbeModel m, PathProbeMsg msg) => (msg switch
    {
        PathProbeMsg.OpenLeft => m with { Left = m.Left.Push(ProbeRoute.Detail) },
        PathProbeMsg.OpenRight => m with { Right = m.Right.Push(ProbeRoute.Detail) },
        PathProbeMsg.EditLeft => m with { Left = m.Left.Edit(m.Left.Top.Id, static e => e with { Note = "saved draft" }) },
        PathProbeMsg.EditRight => m with { Right = m.Right.Edit(m.Right.Top.Id, static e => e with { Count = 42 }) },
        PathProbeMsg.ReturnLeft => m with { Pending = new(m.Left.RequestTo(0, "left"), m.Left.Reset(ProbeRoute.Root)) },
        PathProbeMsg.Confirm c => m.Pending?.Confirm(m.Left, c.Request, "left") is { } path ? m with { Left = path, Pending = null } : m,
        PathProbeMsg.Back b => m.Left.Back(b.Request, "left") is { } path ? m with { Left = path } : m,
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    }, Cmd<PathProbeMsg>.None);
}

sealed class PathProbeView(PathProbeModel model) : Screen<PathProbeModel, PathProbeMsg>(model)
{
    protected override void Render()
    {
        using var page = C.Div(key: "paths").SizeFull().Padding(Space.S4).Gap(Space.S2);
        C.Text("Left: " + Model.Left.Top.Note, key: "left-state");
        C.Text("Right: " + Model.Right.Top.Count, key: "right-state");
        Ui.Button("Open left", new PathProbeMsg.OpenLeft(), key: "open-left");
        Ui.Button("Open right", new PathProbeMsg.OpenRight(), key: "open-right");
        Ui.Button("Edit left", new PathProbeMsg.EditLeft(), key: "edit-left");
        Ui.Button("Edit right", new PathProbeMsg.EditRight(), key: "edit-right");
        Ui.Button("Return left", new PathProbeMsg.ReturnLeft(), key: "return-left");
        if (Model.Pending is { } pending)
            Ui.Button("Confirm left", new PathProbeMsg.Confirm(pending.Request), key: "confirm");
    }
}
