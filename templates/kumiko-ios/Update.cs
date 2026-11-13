using System.Collections.Immutable;
using Kumiko.Runtime;

namespace KumikoiOSTemplate;

internal sealed partial class KumikoiOSTemplateApp : IKumiko<Model, Msg>
{
    const string Host = "app";

    public static NavigationRoot<Msg> Root(Model model) => NavigationRoot<Msg>.Create(model.Navigation, Host, route => route switch
    {
        Route.Home => new HomeScreen(new(model.Count, model.Name)).Map<Msg>(static message => message),
        Route.Settings => new SettingsScreen(new(model.Updates, model.Log, model.Navigation.BackRequest(Host))).Map<Msg>(static message => message),
        _ => throw new ArgumentOutOfRangeException(nameof(route))
    }, static request => new Msg.Navigate(request));

    public static Model Init => new();

    public static (Model, Cmd<Msg>) Update(Model model, Msg message) => (message switch
    {
        Msg.Increment => model with { Count = model.Count + 1 },
        Msg.Rename name => model with { Name = name.Text },
        Msg.SetUpdates updates => model with { Updates = updates.On },
        Msg.Open open => model with { Navigation = model.Navigation.Push(open.Route) },
        Msg.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
        Msg.PhaseChanged phase => model with { Log = model.Log.Add(phase.Phase.ToString()).TakeLast(5).ToImmutableArray() },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<Msg>.None);
}
