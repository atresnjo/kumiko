using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

static class StackHostChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        using var program = StackHostApp.CreateProgram();
        using var tester = new Tester(program, new(560, 440, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Get(Role.TextField, "Draft 0").Click();
        tester.Key(Key.Home);
        tester.Key(Key.Right);
        tester.Get(Role.Button, "Visit again").Click();
        check(!tester.Context.Accessibility.Any(n => n.Name == "Draft 0") && tester.HasText("Visit 1"),
            "Stack covers the previous visit without rendering its input");
        tester.Get(Role.TextField, "Draft 1").Click();
        tester.TypeText("new visit");
        var request = program.Model.Path.RequestTo(0, "main");
        for (var i = 0; i < 500; i++)
            tester.Frame();
        program.Send(new StackHostMsg.Back(request));
        tester.Settle();
        check(program.Model.Path.Top.Id == 0 && program.Model.Path.Top.Text == "abc",
            "Stack separates state for repeated visits to the same route");
        tester.Get(Role.TextField, "Draft 0").Click();
        tester.Key(Key.Home);
        tester.Key(Key.Right);
        tester.TypeText("X");
        tester.Settle();
        check(program.Model.Path.Top.Text == "aXbc", "a returned Stack visit remains editable");
        program.Send(new StackHostMsg.Back(request));
        tester.Settle();
        check(program.Model.Path.Top.Text == "aXbc" && program.Model.Path.Entries.Length == 1,
            "a stale Stack Back cannot remove or reset the returned visit");
        var model = Path.Combine(output, "stack-model.json");
        program.SaveModel(model);
        program.Save(Path.Combine(output, "stack-session.jsonl"));
        tester.Screenshot(Path.Combine(output, "stack-host.png"));
        File.WriteAllText(Path.Combine(output, "stack-host.txt"), tester.Dump());
        check(program.RestoreModel(model), "Stack restores a model without navigation messages");
        tester.Settle();
        check(program.Model.Path.Top.Text == "aXbc", "restored Stack entry state renders without admission work");
    }
}

sealed record StackHostEntry(int Id, string Route, string Text = "abc") : INavigationEntry<string, StackHostEntry>
{
    public static StackHostEntry Create(int id, string route) => new(id, route);
    public static bool IsValidPath(ImmutableArray<StackHostEntry> entries) => entries.All(e => e.Text is not null);
}
sealed record StackHostModel(NavigationPath<string, StackHostEntry> Path);
abstract partial record StackHostMsg
{
    public sealed record Push : StackHostMsg;
    public sealed record Edit(int Id, string Text) : StackHostMsg;
    public sealed record Back(BackRequest Request) : StackHostMsg;
}
sealed partial class StackHostApp : IKumiko<StackHostModel, StackHostMsg>
{
    public static NavigationRoot<StackHostMsg> Root(StackHostModel model) => NavigationRoot<StackHostMsg>.Create(
        NavigationHistory<string>.Start("root"), "app", _ => new StackHostView(model).Map<StackHostMsg>(static message => message),
        static _ => throw new InvalidOperationException("This fixture has one root screen."));
    public static StackHostModel Init => new(NavigationPath<string, StackHostEntry>.Start("same-route"));
    public static (StackHostModel, Cmd<StackHostMsg>) Update(StackHostModel m, StackHostMsg msg) => (msg switch
    {
        StackHostMsg.Push => m with { Path = m.Path.Push("same-route") },
        StackHostMsg.Edit e => m with { Path = m.Path.Edit(e.Id, entry => entry with { Text = e.Text }) },
        StackHostMsg.Back b => m.Path.Back(b.Request, "main") is { } path ? m with { Path = path } : m,
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    }, Cmd<StackHostMsg>.None);
}
sealed class StackHostView(StackHostModel model) : Screen<StackHostModel, StackHostMsg>(model)
{
    protected override void Render()
    {
        using var host = C.Div(key: "host").SizeFull().Column().Gap(Space.S3).Padding(Space.S3);
        Ui.Button("Visit again", new StackHostMsg.Push(), key: "push");
        Ui.Stack(Model.Path, Model, static (bound, props, entry) =>
        {
            var id = entry.Id;
            bound.Context.Heading($"Visit {id}", key: "visit");
            bound.TextField("draft", $"Draft {id}", entry.Text, text => new StackHostMsg.Edit(id, text));
        });
    }
}
