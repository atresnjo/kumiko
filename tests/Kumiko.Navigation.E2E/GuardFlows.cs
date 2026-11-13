using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

static class GuardFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var program = GuardApp.CreateProgram();
        using var tester = new Tester(program, new(500, 400, DevMode: true));
        tester.Get(Role.Button, "Open report").Click();
        tester.Settle();
        check(tester.HasText("Sign in to continue") && program.Model.ReturnTo == "report",
            "a non-CRM ancestor guard defers a protected child before evaluating it");
        var snapshot = Path.Combine(output, "guard-model.json");
        program.SaveModel(snapshot);
        Capture(tester, output, "guard-deferred");
        tester.Get(Role.Button, "Sign in").Click();
        tester.Settle();
        check(tester.HasText("Report contents") && program.Model.ReturnTo is null,
            "a second app resumes its own deferred destination through the framework guard");
        Capture(tester, output, "guard-allowed");
        tester.Get(Role.Button, "Delete report").Click();
        tester.Get(Role.Button, "Open report").Click();
        tester.Settle();
        check(tester.HasText("Home contents") && program.Model.Route == "home",
            "a child redirect returns to the destination selected by the application");
        program.Save(Path.Combine(output, "guard-session.jsonl"));
        using var replay = GuardApp.CreateProgram();
        using var replayTester = new Tester(replay, new(500, 400, DevMode: true));
        replay.Replay(Path.Combine(output, "guard-session.jsonl"));
        replayTester.Settle();
        check(replay.Model == program.Model && replay.PendingCommands == 0, "framework guard decisions replay deterministically in a second app");
        check(replay.RestoreModel(snapshot), "a second app restores its deferred model");
        replayTester.Settle();
        check(replay.Model.Route == "sign-in" && replay.History.Count == 1 && replay.PendingCommands == 0,
            "restoring a deferred model installs it silently without evaluating guards");
        Capture(replayTester, output, "guard-restored");

        Reject<ArgumentNullException>(() => RouteGuard.BeforeLoad<GuardModel, string>(new(), null!, GuardApp.BeforeLoad), check, "null path");
        Reject<ArgumentException>(() => RouteGuard.BeforeLoad<GuardModel, string>(new(), [null!], GuardApp.BeforeLoad), check, "null route");
        Reject<InvalidOperationException>(() => RouteGuard.BeforeLoad<GuardModel, string>(new(), ["home"], static (_, _) => null!), check, "null decision");
    }

    static void Reject<TException>(Action action, Action<bool, string> check, string description) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            check(true, "framework guards reject " + description);
            return;
        }
        check(false, "framework guards reject " + description);
    }

    static void Capture(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        if (tester.Context.Warnings.Count != 0)
            throw new InvalidOperationException("Guard fixture rendered with warnings.");
    }
}

sealed record GuardModel(bool SignedIn = false, bool ReportExists = true, string Route = "home", string? ReturnTo = null);
abstract partial record GuardMsg
{
    public sealed record Open : GuardMsg;
    public sealed record SignIn : GuardMsg;
    public sealed record Delete : GuardMsg;
}

sealed partial class GuardApp : IKumiko<GuardModel, GuardMsg>
{
    public static Kumiko.Runtime.NavigationRoot<GuardMsg> Root(GuardModel model) => Kumiko.Runtime.NavigationRoot<GuardMsg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new GuardView(model).Map<GuardMsg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static GuardModel Init => new();

    public static (GuardModel, Cmd<GuardMsg>) Update(GuardModel m, GuardMsg msg) => (msg switch
    {
        GuardMsg.Open => Navigate(m),
        GuardMsg.SignIn => Navigate(m with { SignedIn = true }),
        GuardMsg.Delete => m with { ReportExists = false },
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    }, Cmd<GuardMsg>.None);

    static GuardModel Navigate(GuardModel m)
    {
        ImmutableArray<string> path = ["workspace", m.ReturnTo ?? "report"];
        return RouteGuard.BeforeLoad(m, path, BeforeLoad) switch
        {
            RouteDecision<string>.Allow => m with { Route = path[^1], ReturnTo = null },
            RouteDecision<string>.Redirect redirect => m with { Route = redirect.To, ReturnTo = null },
            RouteDecision<string>.Defer => m with { Route = "sign-in", ReturnTo = path[^1] },
            _ => throw new InvalidOperationException("Unknown route decision.")
        };
    }

    public static RouteDecision<string> BeforeLoad(GuardModel m, string route)
    {
        if (route == "workspace" && !m.SignedIn)
            return new RouteDecision<string>.Defer();
        if (route == "report" && !m.SignedIn)
            throw new InvalidOperationException("The child ran after its ancestor deferred.");
        return route == "report" && !m.ReportExists
            ? new RouteDecision<string>.Redirect("home") : new RouteDecision<string>.Allow();
    }
}

sealed class GuardView(GuardModel model) : Kumiko.Runtime.Screen<GuardModel, GuardMsg>(model)
{
    protected override void Render()
    {
        using var page = C.Div(key: "guard-page").SizeFull().Padding(Space.S4).Gap(Space.S3);
        C.Text(Model.Route switch
        {
            "report" => "Report contents",
            "sign-in" => "Sign in to continue",
            _ => "Home contents"
        }, Typography.Title, key: "contents");
        Ui.Button("Open report", new GuardMsg.Open(), key: "open");
        if (!Model.SignedIn)
            Ui.Button("Sign in", new GuardMsg.SignIn(), key: "sign-in");
        else
            Ui.Button("Delete report", new GuardMsg.Delete(), key: "delete");
    }
}
