using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

sealed record CheckedEntry(int Id, string Route, string Note = "") : INavigationEntry<string, CheckedEntry>
{
    public static CheckedEntry Create(int id, string route) => new(id, route);
    public static bool IsValidPath(ImmutableArray<CheckedEntry> entries) => entries.All(e => e.Note is not null);
}
sealed record CheckedModel
{
    public NavigationPath<string, CheckedEntry> Path { get; init; } = NavigationPath<string, CheckedEntry>.Start("home");
    public NavigationGuard<string, CheckedEntry> Guard { get; init; } = new();
    public PendingNavigation<string, CheckedEntry>? Discard
    {
        get; init;
    }
    public bool SignedIn { get; init; } = true;
    public string Status { get; init; } = "Ready";
}
abstract partial record CheckedMsg
{
    public sealed record Go(string Route) : CheckedMsg;
    public sealed record Reply(RouteGuardReply<string> Result, bool Foreign = false) : CheckedMsg;
    public sealed record Confirm : CheckedMsg;
    public sealed record Edit : CheckedMsg;
    public sealed record Permission(bool Value) : CheckedMsg;
    public sealed record SignOut : CheckedMsg;
    public sealed record Refresh : CheckedMsg;
}
interface IRoutePermissions
{
    Task<RouteGuardReply<string>> Check(RouteGuardRequest<string> request, CancellationToken token);
}
sealed partial class CheckedApp : IKumiko<CheckedModel, CheckedMsg>
{
    public static NavigationRoot<CheckedMsg> Root(CheckedModel model) => NavigationRoot<CheckedMsg>.Create(
        NavigationHistory<string>.Start("root"), "app", _ => new CheckedView(model).Map<CheckedMsg>(static message => message),
        static _ => throw new InvalidOperationException("This fixture has one root screen."));
    public static CheckedModel Init => new();
    public static (CheckedModel, Cmd<CheckedMsg>) Update(CheckedModel m, CheckedMsg msg) => msg switch
    {
        CheckedMsg.Go go => Navigate(m, m.Path.Reset("home").Push(go.Route)),
        CheckedMsg.Confirm => m.Discard?.Confirm(m.Path, m.Discard.Request, "main") is { } desired
            ? Navigate(m, desired, confirmed: true) : (m, Cmd<CheckedMsg>.None),
        CheckedMsg.Reply reply => Complete(m, reply),
        CheckedMsg.Edit => (m with { Path = m.Path.Edit(m.Path.Top.Id, static e => e with { Note = "Keep this draft" }), Guard = m.Guard.Cancel(), Discard = null }, Cmd<CheckedMsg>.None),
        CheckedMsg.Permission permission => (m with { SignedIn = permission.Value }, Cmd<CheckedMsg>.None),
        CheckedMsg.Refresh => (m with { Path = m.Path.Replace("home") }, Cmd<CheckedMsg>.None),
        CheckedMsg.SignOut => (m with { SignedIn = false, Guard = m.Guard.Cancel(), Discard = null, Path = m.Path.Reset("home"), Status = "Signed out" }, Cmd<CheckedMsg>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    };

    static (CheckedModel, Cmd<CheckedMsg>) Navigate(CheckedModel m, NavigationPath<string, CheckedEntry> desired, bool confirmed = false)
    {
        m = m with
        {
            Guard = m.Guard.Cancel(),
            Discard = null
        };
        switch (GuardNavigation(m, desired))
        {
            case RouteDecision<string>.Defer:
                return (m with
                {
                    Status = "Sign in"
                }, Cmd<CheckedMsg>.None);
            case RouteDecision<string>.Redirect redirect:
                return Navigate(m, m.Path.Reset(redirect.To), confirmed);
        }
        if (!confirmed && m.Path.RequiresConfirmation(desired, static e => e.Note.Length > 0))
            return (m with
            {
                Discard = new(m.Path.RequestTo(desired.Top.Id, "main"), desired),
                Status = "Discard draft?"
            }, Cmd<CheckedMsg>.None);
        m = m with
        {
            Guard = m.Guard.Start(m.Path, desired, "main"),
            Status = "Checking"
        };
        return (m, CheckNavigation(m, m.Guard, static reply => new CheckedMsg.Reply(reply)));
    }

    static (CheckedModel, Cmd<CheckedMsg>) Complete(CheckedModel m, CheckedMsg.Reply reply)
    {
        var result = CompleteNavigation(m, m.Path, m.Guard, reply.Result, reply.Foreign ? "other" : "main");
        if (ReferenceEquals(result.State, m.Guard))
            return (m, Cmd<CheckedMsg>.None);
        m = m with
        {
            Guard = result.State
        };
        if (result.Path is { } path)
            return (m with
            {
                Path = path,
                Status = "Allowed"
            }, Cmd<CheckedMsg>.None);
        if (result.Decision is RouteDecision<string>.Redirect redirect)
            return Navigate(m, m.Path.Reset(redirect.To));
        if (result.Decision is RouteDecision<string>.Defer)
            return (m with
            {
                Status = "Sign in"
            }, Cmd<CheckedMsg>.None);
        return m.Guard.Pending is null
            ? (m with
            {
                Status = "Rejected"
            }, Cmd<CheckedMsg>.None)
            : (m, CheckNavigation(m, m.Guard, static value => new CheckedMsg.Reply(value)));
    }

    static RouteDecision<string> BeforeEnter(CheckedModel m, string route) => route != "home" && !m.SignedIn
        ? new RouteDecision<string>.Defer() : new RouteDecision<string>.Allow();

    static Cmd<RouteGuardReply<string>> BeforeEnterAsync(CheckedModel m, RouteGuardRequest<string> request) =>
        Cmd<RouteGuardReply<string>>.Run<IRoutePermissions>("route-permission",
            (service, token) => service.Check(request, token),
            _ => new(request.Stack, request.Request, request.Step, RouteAdmission.Reject));
}
sealed class CheckedView(CheckedModel model) : Screen<CheckedModel, CheckedMsg>(model)
{
    protected override void Render()
    {
        using var page = C.Div(key: "checked").SizeFull().Padding(Space.S4).Gap(Space.S2);
        C.Heading(Model.Path.Top.Route, key: "route");
        C.Text(Model.Status, key: "status");
        C.Text(Model.Path.Top.Note, key: "note");
        Ui.Button("Open one", new CheckedMsg.Go("one"), key: "one");
        Ui.Button("Open two", new CheckedMsg.Go("two"), key: "two");
        Ui.Button("Edit draft", new CheckedMsg.Edit(), key: "edit");
        if (Model.Discard is not null)
            Ui.Button("Discard", new CheckedMsg.Confirm(), key: "discard");
    }
}
