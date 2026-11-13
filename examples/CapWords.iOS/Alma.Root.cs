using Kumiko.Runtime;

internal sealed partial class AlmaApp
{
    /// <summary>The navigation host key when the study runs on its own; history requests from any other host are ignored.</summary>
    internal const string Host = "AlmaApp";

    public static NavigationRoot<AlmaMessage> Root(AlmaModel model) => NavigationRoot<AlmaMessage>.Create(
        model.Navigation, Host, _ => new AlmaScreen(model).Map<AlmaMessage>(static message => message),
        static request => new AlmaMessage.Navigate(request));
}
