using Kumiko.Runtime;

internal sealed partial class SlackApp
{
    /// <summary>The navigation host key when the study runs on its own; history requests from any other host are ignored.</summary>
    internal const string Host = "SlackApp";

    public static NavigationRoot<SlackMessage> Root(SlackModel model) => NavigationRoot<SlackMessage>.Create(
        model.Navigation, Host, _ => new SlackScreen(model).Map<SlackMessage>(static message => message),
        static request => new SlackMessage.Navigate(request));
}
