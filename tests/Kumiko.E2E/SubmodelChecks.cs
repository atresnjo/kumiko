using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Component composition: mapped views, lifted commands and out-messages.</summary>
static class SubmodelChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = SubmodelApp.CreateProgram();
        using var tester = new Tester(program, new(Width: 480, Height: 240, DevMode: true));
        tester.Get(Role.Group, "Left").Get(Role.Button, "Load").Click();
        check(program.Model.Left.Label == "Loading" && program.Model.Right == new LoaderModel() &&
            program.History[^1].Commands.SequenceEqual(["load", "log"]), "child commands start before the commands the parent adds for its out-message");
        Wait(program);
        tester.Frame();
        Wait(program);
        tester.Frame();
        check(program.Model.Left is { Label: "Ready", Loads: 1 } && program.Model.Right == new LoaderModel() &&
            program.Model.Log.Order(StringComparer.Ordinal).SequenceEqual(["Left started", "Left: Ready"]), "a child command result returns wrapped for the same child");
        var path = Path.Combine(output, "submodel-session.jsonl");
        program.Save(path);
        tester.MoveTo(-100, -100);
        tester.Context.Blur();
        tester.Frame(0);
        var tree = tester.Dump();
        using var replay = SubmodelApp.CreateProgram();
        using var replayTester = new Tester(replay, new(Width: 480, Height: 240, DevMode: true));
        replayTester.Replay(path);
        check(replayTester.Dump() == tree && replay.Model with
        {
            Log = []
        } == program.Model with
        {
            Log = []
        } &&
            replay.Model.Log.SequenceEqual(program.Model.Log), "component sessions replay without running child commands");
    }

    static void Wait(Program<SubmodelModel, SubmodelMessage> program)
    {
        if (!SpinWait.SpinUntil(() => program.PendingCommands == 0, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Component command did not complete");
    }
}

public sealed record LoaderModel(string Label = "Idle", int Loads = 0);
public abstract partial record LoaderMessage
{
    public sealed record ClickedLoad : LoaderMessage;
    public sealed record Loaded(string Label) : LoaderMessage;
}
public abstract record LoaderEvent
{
    public sealed record Started : LoaderEvent;
    public sealed record Finished(string Label) : LoaderEvent;
}

/// <summary>A child that loads its label through a command and tells its parent when it starts and finishes.</summary>
public sealed partial class Loader : IKumikoComponent<LoaderModel, LoaderMessage, LoaderEvent>
{
    public static Outcome<LoaderModel, LoaderMessage, LoaderEvent> Update(LoaderModel model, LoaderMessage message) => message switch
    {
        LoaderMessage.ClickedLoad => new(model with
        {
            Label = "Loading"
        },
            Cmd<LoaderMessage>.Run("load", static _ => Task.FromResult<LoaderMessage>(new LoaderMessage.Loaded("Ready")),
                static error => new LoaderMessage.Loaded(error.Message)), new LoaderEvent.Started()),
        LoaderMessage.Loaded loaded => new(model with
        {
            Label = loaded.Label,
            Loads = model.Loads + 1
        },
            outMessage: new LoaderEvent.Finished(loaded.Label)),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

}

public sealed record LoaderProps(LoaderModel Value, string Title);
public sealed class LoaderView(LoaderProps props) : View<LoaderProps, LoaderMessage>(props)
{
    protected override void Render()
    {
        using var root = C.Div(key: "loader").Row().ItemsCenter().Gap(Space.S2).Semantics(Role.Group, Model.Title);
        C.Text(Model.Value.Label, key: "label");
        Ui.Button("Load", new LoaderMessage.ClickedLoad(), key: "load");
    }
}

public sealed record SubmodelModel(LoaderModel Left, LoaderModel Right, ImmutableArray<string> Log);
public abstract partial record SubmodelMessage
{
    public sealed record GotLeftMessage(LoaderMessage Message) : SubmodelMessage;
    public sealed record GotRightMessage(LoaderMessage Message) : SubmodelMessage;
    public sealed record Logged(string Entry) : SubmodelMessage;
}

public sealed partial class SubmodelApp : IKumiko<SubmodelModel, SubmodelMessage>
{
    public static Kumiko.Runtime.NavigationRoot<SubmodelMessage> Root(SubmodelModel model) => Kumiko.Runtime.NavigationRoot<SubmodelMessage>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new SubmodelView(model).Map<SubmodelMessage>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static SubmodelModel Init => new(new(), new(), []);

    public static (SubmodelModel, Cmd<SubmodelMessage>) Update(SubmodelModel model, SubmodelMessage message) => message switch
    {
        SubmodelMessage.GotLeftMessage got => Loader.Update(model.Left, got.Message)
            .Fold(left => model with { Left = left }, FromLeft, (next, e) => (next, Log("Left", e))),
        SubmodelMessage.GotRightMessage got => Loader.Update(model.Right, got.Message)
            .Fold(right => model with { Right = right }, FromRight, (next, e) => (next, Log("Right", e))),
        SubmodelMessage.Logged logged => (model with { Log = model.Log.Add(logged.Entry) }, Cmd<SubmodelMessage>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static SubmodelMessage FromLeft(LoaderMessage message) => new SubmodelMessage.GotLeftMessage(message);
    static SubmodelMessage FromRight(LoaderMessage message) => new SubmodelMessage.GotRightMessage(message);

    static Cmd<SubmodelMessage> Log(string side, LoaderEvent e)
    {
        var entry = e is LoaderEvent.Finished finished ? $"{side}: {finished.Label}" : $"{side} started";
        return Cmd<SubmodelMessage>.Run("log", _ => Task.FromResult<SubmodelMessage>(new SubmodelMessage.Logged(entry)),
            static error => new SubmodelMessage.Logged(error.Message));
    }
}

sealed class SubmodelView(SubmodelModel model) : Kumiko.Runtime.Screen<SubmodelModel, SubmodelMessage>(model)
{
    protected override void Render()
    {
        using var root = C.Div(key: "loaders").SizeFull().Padding(Space.S4).Gap(Space.S3);
        Ui.Child(new LoaderView(new(Model.Left, "Left")), "left", static message => new SubmodelMessage.GotLeftMessage(message));
        Ui.Child(new LoaderView(new(Model.Right, "Right")), "right", static message => new SubmodelMessage.GotRightMessage(message));
    }
}
