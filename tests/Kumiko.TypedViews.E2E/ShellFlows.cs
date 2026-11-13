using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>A persistent navigation shell keeps one identity while the visit inside its outlet changes.</summary>
static class ShellFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = ShellApp.CreateProgram();
        using var tester = new Tester(program, new(640, 480, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        var search = tester.Get(Role.TextField, "Search").Id;
        var inbox = tester.Get(Role.TextField, "Draft").Id;
        tester.Get(Role.TextField, "Search").Click();
        tester.TypeText("a");
        program.Send(new ShellMsg.Tab("archive"));
        tester.Settle();
        tester.TypeText("b");
        tester.Settle();
        var archive = tester.Get(Role.TextField, "Draft").Id;
        check(tester.Get(Role.TextField, "Search").Id == search && archive != inbox,
            "Replace selects a new visit inside the outlet while the shell keeps its identity");
        check(program.Model.Query == "ab", "shell focus and caret survive a visit change");
        check(!program.Model.Navigation.CanGoBack, "tab-style Replace leaves no Back entry");
        tester.Get(Role.Button, "Open detail").Invoke();
        tester.Settle();
        var detail = tester.Get(Role.TextField, "Draft").Id;
        check(tester.Get(Role.TextField, "Search").Id == search && detail != archive, "Push keeps the shell and adds a visit");
        tester.Navigate(NavigationDirection.Back);
        tester.Settle();
        check(program.Model.Navigation.Current.Route == "archive" && tester.Get(Role.TextField, "Draft").Id == archive &&
            tester.Get(Role.TextField, "Search").Id == search, "mouse Back routes through the shell host and restores the covered visit");
        tester.Navigate(NavigationDirection.Forward);
        tester.Settle();
        check(tester.Get(Role.TextField, "Draft").Id == detail && tester.Get(Role.TextField, "Search").Id == search,
            "Forward returns to the retained visit under the same shell");
        tester.Screenshot(Path.Combine(output, "13-shell.png"));
        var session = Path.Combine(output, "shell-session.jsonl");
        program.Save(session);
        using (var replay = ShellApp.CreateProgram())
        {
            replay.Replay(session);
            using var replayTester = new Tester(replay, new(640, 480, DevMode: true));
            replayTester.Settle();
            check(replay.Model.Query == program.Model.Query && replay.Model.Navigation.Current == program.Model.Navigation.Current &&
                replay.Model.Navigation.Position == program.Model.Navigation.Position && replayTester.Get(Role.TextField, "Search").Value == "ab" &&
                replayTester.HasText("Visit: detail"), "replay shows the same shell and visit");
        }
        program.Send(new ShellMsg.Reset());
        tester.Settle();
        check(tester.Get(Role.TextField, "Search").Id == search && tester.Get(Role.TextField, "Draft").Id != inbox,
            "Reset allocates a fresh visit under the same shell");
        foreach (var (outlets, name) in new[] { (0, "a shell without an outlet"), (2, "a shell rendering its outlet twice") })
        {
            using var broken = ShellApp.CreateProgram(ShellApp.Init with
            {
                Outlets = outlets
            });
            string? error = null;
            try
            {
                using var brokenTester = new Tester(broken, new(640, 480, DevMode: true));
                brokenTester.Settle();
            }
            catch (InvalidOperationException failure)
            {
                error = failure.Message;
            }
            check(error?.Contains("Outlet", StringComparison.Ordinal) == true, name + " fails with a message naming Outlet");
        }
    }
}

internal sealed record ShellModel(NavigationHistory<string> Navigation, string Query = "", string Draft = "", int Outlets = 1);
internal abstract partial record ShellMsg
{
    public sealed record Tab(string Route) : ShellMsg;
    public sealed record Open : ShellMsg;
    public sealed record Move(HistoryRequest Request) : ShellMsg;
    public sealed record Search(string Text) : ShellMsg;
    public sealed record Edit(string Text) : ShellMsg;
    public sealed record Reset : ShellMsg;
}
internal sealed partial class ShellApp : IKumiko<ShellModel, ShellMsg>
{
    public static ShellModel Init => new(NavigationHistory<string>.Start("inbox"));
    public static NavigationRoot<ShellMsg> Root(ShellModel model) => NavigationRoot<ShellMsg>.Create(model.Navigation, "app",
        route => new ShellVisit(new(route, model.Draft)).Map<ShellMsg>(static m => m),
        static request => new ShellMsg.Move(request),
        new ShellFrame(new(model.Navigation.Current.Route, model.Query, model.Outlets)).Map<ShellMsg>(static m => m));
    public static (ShellModel, Cmd<ShellMsg>) Update(ShellModel model, ShellMsg message) => (message switch
    {
        ShellMsg.Tab tab => tab.Route == model.Navigation.Current.Route ? model : model with { Navigation = model.Navigation.Replace(tab.Route) },
        ShellMsg.Open => model with { Navigation = model.Navigation.Push("detail") },
        ShellMsg.Move move => model with { Navigation = model.Navigation.Move(move.Request, "app") },
        ShellMsg.Search search => model with { Query = search.Text },
        ShellMsg.Edit edit => model with { Draft = edit.Text },
        ShellMsg.Reset => model with { Navigation = model.Navigation.Reset("inbox") },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<ShellMsg>.None);
}
internal sealed record ShellFrameProps(string Route, string Query, int Outlets);
internal sealed class ShellFrame(ShellFrameProps props) : NavigationShell<ShellFrameProps, ShellMsg>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "frame").SizeFull().Padding(Space.S4).Gap(Space.S2);
        Ui.TextField("search", "Search", Model.Query, static text => new ShellMsg.Search(text));
        using (C.Div(key: "tabs").Row().Gap(Space.S2))
        {
            Ui.Button("Inbox", new ShellMsg.Tab("inbox"));
            Ui.Button("Archive", new ShellMsg.Tab("archive"));
            Ui.Button("Open detail", new ShellMsg.Open());
        }
        using (C.Div(key: "outlet").Flex())
            for (var i = 0; i < Model.Outlets; i++)
                Outlet();
        C.Text("Shell footer", key: "footer");
    }
}
internal sealed record ShellVisitProps(string Route, string Draft);
internal sealed class ShellVisit(ShellVisitProps props) : Screen<ShellVisitProps, ShellMsg>(props)
{
    protected override void Render()
    {
        C.Text("Visit: " + Model.Route, Typography.Title, key: "title");
        Ui.TextField("draft", "Draft", Model.Draft, static text => new ShellMsg.Edit(text));
    }
}
