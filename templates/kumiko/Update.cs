using Kumiko.Runtime;

namespace KumikoTemplate;

internal sealed partial class KumikoTemplateApp : IKumiko<Model, Msg>
{
    public static NavigationRoot<Msg> Root(Model model) => NavigationRoot<Msg>.Create(model.Navigation, "app",
        route => route == "home" ? new KumikoTemplateView(model).Map<Msg>(static message => message)
            : throw new ArgumentOutOfRangeException(nameof(route)), static request => new Msg.Navigate(request));
    public static Model Init => new();
    public static (Model, Cmd<Msg>) Update(Model model, Msg message) => (message switch
    {
        Msg.Increment => model with { Count = model.Count + 1 },
        Msg.Rename name => model with { Name = name.Text },
        Msg.SetUpdates updates => model with { Updates = updates.On },
        Msg.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<Msg>.None);
}
