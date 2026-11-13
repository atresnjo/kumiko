using Kumiko.Runtime;
using Kumiko.UI;

static class IdentityFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = PairApp.CreateProgram();
        using var tester = new Tester(program, new(720, 240, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        var left = tester.Get(Role.TextField, "First");
        var right = tester.Get(Role.TextField, "Second");
        var leftId = left.Id;
        var rightId = right.Id;
        check(leftId != rightId, "nested repeated child views isolate identical local keys");
        var firstButton = tester.Get(Role.Button, "First reset");
        var secondButton = tester.Get(Role.Button, "Second reset");
        check(left.Rect.Y == firstButton.Rect.Y && left.Rect.Y == right.Rect.Y && right.Rect.Y == secondButton.Rect.Y &&
            left.Rect.Right < firstButton.Rect.X && firstButton.Rect.Right < right.Rect.X && right.Rect.Right < secondButton.Rect.X,
            "children contribute multiple direct row siblings without an extra layout box");
        left.Fill("abcd");
        right.Fill("wxyz");
        check(program.Model.First == "abcd" && program.Model.Second == "wxyz" &&
            program.History[^1].Source?.Path.Contains("view[\"second\"]/view[\"editor\"]", StringComparison.Ordinal) == true,
            "deferred edits traverse nested mappings and keep the originating child path");
        left.Click();
        tester.Key(Key.Home);
        tester.Key(Key.Right);
        program.Send(new PairMsg.Reverse());
        tester.Settle();
        tester.TypeText("X");
        tester.Settle();
        check(program.Model.First == "aXbcd" && program.Model.Second == "wxyz" && left.Id == leftId && right.Id == rightId && left.Rect.X > right.Rect.X,
            "reordering preserves the owning editor's identity, focus and caret");
        tester.Key(Key.Z, Modifiers.Primary);
        tester.Settle();
        check(program.Model.First == "abcd", "undo stays with the keyed child after reordering");
        secondButton.Invoke();
        check(program.Model.First == "abcd" && program.Model.Second == "", "nested button output reaches only its owning instance");
        left.Click();
        tester.Key(Key.End);
        program.Send(new PairMsg.ShowFirst(false));
        tester.Settle();
        check(!tester.Context.Accessibility.Any(n => n.Name == "First"), "removing a child removes its accessible editor");
        program.Send(new PairMsg.ShowFirst(true));
        tester.Settle();
        tester.TypeText("lost");
        tester.Settle();
        check(program.Model.First == "abcd", "removed and recreated children do not keep abandoned focus");
        left.Click();
        tester.Key(Key.End);
        tester.TypeText("!");
        tester.Settle();
        check(program.Model.First == "abcd!" && program.Model.Second == "", "recreated editor remains independently usable");
        Capture.Write(tester, output, "07-keyed-instances");
        var session = Path.Combine(output, "keyed-session.jsonl");
        program.Save(session);
        using var replay = PairApp.CreateProgram();
        replay.Replay(session);
        using var replayTester = new Tester(replay, new(720, 240, DevMode: true));
        check(replay.Model == program.Model, "nested child outputs replay into the same parent state");
        Capture.Write(replayTester, output, "08-keyed-replay");
    }
}

internal sealed record PairModel(string First = "", string Second = "", bool Reversed = false, bool HasFirst = true);
internal abstract partial record PairMsg
{
    public sealed record From(bool First, EditorMsg Message) : PairMsg;
    public sealed record Reverse : PairMsg;
    public sealed record ShowFirst(bool Show) : PairMsg;
}
internal sealed partial class PairApp : IKumiko<PairModel, PairMsg>
{
    public static PairModel Init => new();
    public static Kumiko.Runtime.NavigationRoot<PairMsg> Root(PairModel model) => Kumiko.Runtime.NavigationRoot<PairMsg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new PairView(model).Map<PairMsg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static (PairModel, Cmd<PairMsg>) Update(PairModel model, PairMsg message) => (message switch
    {
        PairMsg.From from => from.First ? model with { First = from.Message.Text } : model with { Second = from.Message.Text },
        PairMsg.Reverse => model with { Reversed = !model.Reversed },
        PairMsg.ShowFirst show => model with { HasFirst = show.Show },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<PairMsg>.None);
}
internal sealed class PairView(PairModel model) : Kumiko.Runtime.Screen<PairModel, PairMsg>(model)
{
    protected override void Render()
    {
        using var row = C.Div(key: "row").Row().SizeFull().Padding(Space.S4).Gap(Space.S2).Items(Align.Start);
        foreach (var first in Model.Reversed ? new[] { false, true } : new[] { true, false })
        {
            if (first && !Model.HasFirst)
                continue;
            Ui.Child(new EditorView(new(first ? "First" : "Second", first ? Model.First : Model.Second)),
                first ? "first" : "second", message => new PairMsg.From(first, message));
        }
    }
}
internal sealed record EditorProps(string Label, string Text);
internal sealed record EditorMsg(string Text);
internal sealed record FieldMsg(string Text);
internal sealed class EditorView(EditorProps props) : View<EditorProps, EditorMsg>(props)
{
    protected override void Render() => Ui.Child(new FieldView(Model), "editor", static message => new EditorMsg(message.Text));
}
internal sealed class FieldView(EditorProps props) : View<EditorProps, FieldMsg>(props)
{
    protected override void Render()
    {
        Ui.TextField("value", Model.Label, Model.Text, static text => new FieldMsg(text), new(Width: 170));
        Ui.Button(Model.Label + " reset", new FieldMsg(""), key: "reset");
    }
}
