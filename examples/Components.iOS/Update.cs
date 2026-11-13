using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed partial class ComponentsApp : IKumiko<ComponentsModel, ComponentsMessage>
{
    /// <summary>The navigation host key; history requests from any other host are ignored.</summary>
    internal const string Host = "app";

    public static NavigationRoot<ComponentsMessage> Root(ComponentsModel model) => NavigationRoot<ComponentsMessage>.Create(
        model.Navigation, Host, _ => new ComponentsView(new(model.Items, model.SelectedId)).Map<ComponentsMessage>(static message => message),
        static request => new ComponentsMessage.Navigate(request));
    public static ComponentsModel Init => new([
        new("item-1", "Plan the week", "Choose three things to focus on."),
        new("item-2", "Take a walk", "Make room for a little fresh air."),
        new("item-3", "Read a chapter", "Pick up where you left off.")]);

    public static (ComponentsModel, Cmd<ComponentsMessage>) Update(ComponentsModel model, ComponentsMessage message) =>
        (message switch
        {
            ComponentsMessage.Add => model with
            {
                Items = model.Items.Add(new NativeListItem($"item-{model.NextId}", $"New item {model.NextId}", "Select to see its details.")),
                NextId = model.NextId + 1
            },
            ComponentsMessage.Select select => model.Items.Any(item => item.Id == select.Id && item.Enabled)
                ? model with
                {
                    SelectedId = select.Id
                } : model,
            ComponentsMessage.Close => model with { SelectedId = null },
            ComponentsMessage.RemoveSelected => model with
            {
                Items = model.Items.RemoveAll(item => item.Id == model.SelectedId),
                SelectedId = null
            },
            ComponentsMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<ComponentsMessage>.None);
}
