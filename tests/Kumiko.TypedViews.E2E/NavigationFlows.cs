using Kumiko.Runtime;
using Kumiko.UI;

static class NavigationFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = HistoryApp.CreateProgram();
        using var tester = new Tester(program, new(640, 480, DevMode: true));
        tester.Context.ReduceMotion = true;
        program.Record(Path.Combine(output, "navigation-recording.jsonl"));
        tester.Get(Role.TextField, "Draft").Fill("abcd");
        tester.Get(Role.TextField, "Draft").Click();
        tester.Key(Key.Home);
        tester.Key(Key.Right);
        var firstId = tester.Get(Role.TextField, "Draft").Id;
        tester.Get(Role.Button, "Save domain value").Invoke();
        tester.Get(Role.Button, "Open next").Invoke();
        tester.Settle();
        var secondId = tester.Get(Role.TextField, "Draft").Id;
        check(secondId != firstId && program.Model.Navigation.Current.Route == "next", "each screen visit has its own control identity");
        var stale = program.Model.Navigation.BackRequest("app")!;
        tester.Get(Role.Button, "Open next").Invoke();
        tester.Settle();
        var thirdId = tester.Get(Role.TextField, "Draft").Id;
        check(thirdId != secondId, "repeated visits to the same route have independent identities");
        program.Send(new HistoryMsg.Move(stale));
        tester.Settle();
        check(program.Model.Navigation.Position == 2, "stale history requests cannot remove newer visits");
        var current = program.Model.Navigation.BackRequest("app")!;
        program.Send(new HistoryMsg.Move(current with
        {
            Stack = "another-window"
        }));
        program.Send(new HistoryMsg.Move(current with
        {
            To = 0
        }));
        tester.Settle();
        check(program.Model.Navigation.Position == 2, "cross-host and nonadjacent requests are rejected");
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        check(program.Model.Navigation.Position == 1 && tester.Get(Role.TextField, "Draft").Id == secondId && program.Model.Saved == "abcd",
            "mouse Back selects the previous visit without undoing saved domain data");
        tester.Navigate(NavigationDirection.Forward);
        tester.Settle();
        check(program.Model.Navigation.Position == 2 && tester.Get(Role.TextField, "Draft").Id == thirdId,
            "mouse Forward returns to the retained next visit exactly once");
        tester.Key(OperatingSystem.IsMacOS() ? Key.BracketLeft : Key.Left, OperatingSystem.IsMacOS() ? Modifiers.Primary : Modifiers.Alt);
        tester.Settle();
        check(program.Model.Navigation.Position == 1, "keyboard Back uses the same history-message path");
        tester.Key(OperatingSystem.IsMacOS() ? Key.BracketRight : Key.Right, OperatingSystem.IsMacOS() ? Modifiers.Primary : Modifiers.Alt);
        tester.Settle();
        check(program.Model.Navigation.Position == 2, "keyboard Forward uses the same history-message path");
        tester.Settle(500);
        tester.Navigate(NavigationDirection.Back);
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        tester.TypeText("X");
        tester.Settle();
        check(program.Model.Draft == "aXbcd" && tester.Get(Role.TextField, "Draft").Id == firstId,
            "covered visits retain focus and caret after 500 frames without rendering their controls");
        tester.Key(Key.Z, Modifiers.Primary);
        tester.Settle();
        check(program.Model.Draft == "abcd", "the returning visit keeps its own undo state");
        tester.Navigate(NavigationDirection.Forward);
        tester.Navigate(NavigationDirection.Forward);
        tester.Settle();
        tester.Get(Role.Button, "Block navigation").Invoke();
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        check(program.Model.Navigation.Position == 2, "application admission policy can reject native history input");
        tester.Get(Role.Button, "Allow navigation").Invoke();
        tester.Get(Role.Button, "Open modal").Invoke();
        tester.Settle();
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        check(program.Model.Navigation.Position == 2 && program.Model.Modal, "modal input ownership blocks screen history");
        tester.Key(Key.Escape);
        tester.Settle();
        tester.Get(Role.TextField, "Draft").Click();
        tester.Compose("pending", false);
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        check(program.Model.Navigation.Position == 2, "uncommitted IME input blocks history movement");
        tester.Compose("", true);
        tester.Settle();
        tester.Get(Role.Button, "Back").Invoke();
        tester.Settle();
        Capture.Write(tester, output, "09-history-back");
        tester.Get(Role.Button, "Open branch").Invoke();
        tester.Settle();
        check(!program.Model.Navigation.CanGoForward && program.Model.Navigation.Current.Route == "branch", "new navigation truncates the abandoned forward branch");
        tester.Get(Role.Button, "Replace visit").Invoke();
        tester.Settle();
        check(program.Model.Navigation.Position == 2 && program.Model.Navigation.Current.Route == "replacement", "Replace keeps the history position and selects a new visit");
        Capture.Write(tester, output, "10-history-branch");
        var session = Path.Combine(output, "navigation-session.jsonl");
        program.Save(session);
        using (var replay = HistoryApp.CreateProgram())
        {
            replay.Replay(session);
            using var replayTester = new Tester(replay, new(640, 480, DevMode: true));
            check(replay.Model.Navigation.Path.Entries.SequenceEqual(program.Model.Navigation.Path.Entries) &&
                replay.Model.Navigation.Position == program.Model.Navigation.Position && replay.Model.Navigation.Path.NextId == program.Model.Navigation.Path.NextId &&
                replay.Model.Navigation.Path.Revision == program.Model.Navigation.Path.Revision && replay.Model.Saved == program.Model.Saved,
                "generated metadata replays navigation visits, cursor, allocator, revision and domain state");
            Capture.Write(replayTester, output, "11-history-replay");
        }
        tester.Get(Role.Button, "Reset history").Invoke();
        tester.Settle();
        check(!program.Model.Navigation.CanGoBack && !program.Model.Navigation.CanGoForward && program.Model.Navigation.Path.Entries.Length == 1 &&
            tester.Get(Role.TextField, "Draft").Id != firstId, "Reset discards both directions and allocates a fresh root identity");
        var count = program.History.Count;
        tester.Navigate(NavigationDirection.Back);
        tester.Navigate(NavigationDirection.Forward);
        tester.Settle();
        check(program.History.Count == count, "history boundaries do not dispatch spurious messages");
        Capture.Write(tester, output, "12-history-reset");
    }
}

internal sealed record HistoryModel(NavigationHistory<string> Navigation, string Draft = "", string Saved = "", bool Block = false, bool Modal = false);
internal abstract partial record HistoryMsg
{
    public sealed record Open(string Route) : HistoryMsg;
    public sealed record Move(HistoryRequest Request) : HistoryMsg;
    public sealed record Edit(string Text) : HistoryMsg;
    public sealed record Save : HistoryMsg;
    public sealed record Block(bool Value) : HistoryMsg;
    public sealed record Modal(bool Value) : HistoryMsg;
    public sealed record Replace : HistoryMsg;
    public sealed record Reset : HistoryMsg;
}
internal sealed partial class HistoryApp : IKumiko<HistoryModel, HistoryMsg>
{
    public static HistoryModel Init => new(NavigationHistory<string>.Start("home"));
    public static NavigationRoot<HistoryMsg> Root(HistoryModel model) => NavigationRoot<HistoryMsg>.Create(model.Navigation, "app",
        route => new HistoryScreen(new(route, model.Draft, model.Saved, model.Block, model.Modal,
            model.Navigation.BackRequest("app"), model.Navigation.ForwardRequest("app"))).Map<HistoryMsg>(static m => m),
        static request => new HistoryMsg.Move(request));
    public static (HistoryModel, Cmd<HistoryMsg>) Update(HistoryModel model, HistoryMsg message) => (message switch
    {
        HistoryMsg.Open open => model with { Navigation = model.Navigation.Push(open.Route) },
        HistoryMsg.Move move => model.Block ? model : model with { Navigation = model.Navigation.Move(move.Request, "app") },
        HistoryMsg.Edit edit => model with { Draft = edit.Text },
        HistoryMsg.Save => model with { Saved = model.Draft },
        HistoryMsg.Block block => model with { Block = block.Value },
        HistoryMsg.Modal modal => model with { Modal = modal.Value },
        HistoryMsg.Replace => model with { Navigation = model.Navigation.Replace("replacement") },
        HistoryMsg.Reset => model with { Navigation = model.Navigation.Reset("home") },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<HistoryMsg>.None);
}
internal sealed record HistoryProps(string Route, string Draft, string Saved, bool Block, bool Modal, HistoryRequest? Back, HistoryRequest? Forward);
internal sealed class HistoryScreen(HistoryProps props) : Screen<HistoryProps, HistoryMsg>(props)
{
    protected override void Render()
    {
        using (C.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S2))
        {
            C.Text("Visit: " + Model.Route, Typography.Title);
            C.Text("Saved: " + Model.Saved);
            Ui.TextField("draft", "Draft", Model.Draft, static value => new HistoryMsg.Edit(value));
            Ui.Button("Save domain value", new HistoryMsg.Save());
            using (C.Div(key: "history").Row().Gap(Space.S2))
            {
                if (Model.Back is { } back)
                    Ui.Button("Back", new HistoryMsg.Move(back));
                if (Model.Forward is { } forward)
                    Ui.Button("Forward", new HistoryMsg.Move(forward));
            }
            using (C.Div(key: "actions").Row().Gap(Space.S2))
            {
                Ui.Button("Open next", new HistoryMsg.Open("next"));
                Ui.Button("Open branch", new HistoryMsg.Open("branch"));
                Ui.Button("Replace visit", new HistoryMsg.Replace());
            }
            Ui.Button(Model.Block ? "Allow navigation" : "Block navigation", new HistoryMsg.Block(!Model.Block));
            Ui.Button("Open modal", new HistoryMsg.Modal(true));
            Ui.Button("Reset history", new HistoryMsg.Reset());
        }
        if (Model.Modal)
        {
            var ui = Ui;
            using var dialog = C.Kit.Dialog("Stay here", () => ui.Dispatch(new HistoryMsg.Modal(false)), key: "modal");
            C.Text("Close this dialog before navigating.");
        }
    }
}
