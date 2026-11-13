using Kumiko.Runtime;

internal sealed partial class NativeButtonApp : IKumiko<NativeButtonModel, NativeButtonMessage>
{
    /// <summary>The navigation host key; history requests from any other host are ignored.</summary>
    internal const string Host = "app";

    public static NavigationRoot<NativeButtonMessage> Root(NativeButtonModel model) => NavigationRoot<NativeButtonMessage>.Create(
        model.Navigation, Host, _ => new NativeButtonView(new(model.Count, model.Enabled, model.Visible)).Map<NativeButtonMessage>(static message => message),
        static request => new NativeButtonMessage.Navigate(request));
    public static NativeButtonModel Init => new();

    public static (NativeButtonModel, Cmd<NativeButtonMessage>) Update(NativeButtonModel model, NativeButtonMessage message) =>
        (message switch
        {
            NativeButtonMessage.Increment => model with { Count = model.Count + 1 },
            NativeButtonMessage.SetEnabled enabled => model with { Enabled = enabled.Value },
            NativeButtonMessage.SetVisible visible => model with { Visible = visible.Value },
            NativeButtonMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, Host) },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        }, Cmd<NativeButtonMessage>.None);
}
