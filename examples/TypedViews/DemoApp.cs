using Kumiko.Runtime;

namespace TypedViews;

public enum DemoRoute
{
    Auth, Main
}
public sealed record DemoModel(NavigationHistory<DemoRoute> Navigation, string SignInName = "", string? UserName = null, EditNameProps? Dialog = null);
public abstract partial record DemoMsg
{
    public sealed record Auth(AuthMsg Message) : DemoMsg;
    public sealed record Main(MainMsg Message) : DemoMsg;
    public sealed record Navigate(HistoryRequest Request) : DemoMsg;
}

/// <summary>Local demonstration only: signing in stores a display name in memory.</summary>
public sealed partial class DemoApp : IKumiko<DemoModel, DemoMsg>
{
    public static DemoModel Init => new(NavigationHistory<DemoRoute>.Start(DemoRoute.Auth));
    public static NavigationRoot<DemoMsg> Root(DemoModel model) => NavigationRoot<DemoMsg>.Create(model.Navigation, "app",
        route => route switch
        {
            DemoRoute.Auth => new AuthScreen(new(model.SignInName)).Map<DemoMsg>(static message => new DemoMsg.Auth(message)),
            DemoRoute.Main => new MainScreen(new(model.UserName ?? throw new InvalidOperationException("Main requires a demo session."), model.Dialog))
                .Map<DemoMsg>(static message => new DemoMsg.Main(message)),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        }, static request => new DemoMsg.Navigate(request));

    public static (DemoModel, Cmd<DemoMsg>) Update(DemoModel model, DemoMsg message) => (message switch
    {
        DemoMsg.Auth auth => Authenticate(model, auth.Message),
        DemoMsg.Main main => OnMain(model, main.Message),
        DemoMsg.Navigate navigate => Navigate(model, navigate.Request),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<DemoMsg>.None);

    static DemoModel Authenticate(DemoModel model, AuthMsg message) => message switch
    {
        AuthMsg.NameChanged changed => model.UserName is null ? model with { SignInName = changed.Name } : model,
        AuthMsg.SignIn => model.UserName is null && !string.IsNullOrWhiteSpace(model.SignInName)
            ? new(model.Navigation.Replace(DemoRoute.Main), UserName: model.SignInName.Trim()) : model,
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static DemoModel OnMain(DemoModel model, MainMsg message) => message switch
    {
        MainMsg.EditName => model.UserName is { } name ? model with { Dialog = new(name) } : model,
        MainMsg.SignOut => new(model.Navigation.Reset(DemoRoute.Auth)),
        MainMsg.Edit edit => Edit(model, edit.Message),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static DemoModel Edit(DemoModel model, EditNameMsg message) => message switch
    {
        EditNameMsg.NameChanged changed => model.Dialog is not null ? model with { Dialog = new(changed.Name) } : model,
        EditNameMsg.Cancel => model with { Dialog = null },
        EditNameMsg.Save => model.Dialog is { Name: var name } && !string.IsNullOrWhiteSpace(name)
            ? model with
            {
                UserName = name.Trim(),
                Dialog = null
            } : model,
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static DemoModel Navigate(DemoModel model, HistoryRequest request)
    {
        if (model.Dialog is not null)
            return model;
        var desired = model.Navigation.Move(request, "app");
        var admitted = RouteGuard.BeforeLoad(model, desired.Path.Entries.Select(static entry => entry.Route),
            static (state, route) => route == DemoRoute.Auth ? state.UserName is null ? new RouteDecision<DemoRoute>.Allow() : new RouteDecision<DemoRoute>.Defer()
                : state.UserName is not null ? new RouteDecision<DemoRoute>.Allow() : new RouteDecision<DemoRoute>.Defer());
        return admitted is RouteDecision<DemoRoute>.Allow ? model with
        {
            Navigation = desired
        } : model;
    }
}
