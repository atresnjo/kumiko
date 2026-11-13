using Kumiko.Runtime;

internal sealed partial class CapWordsApp
{
    /// <summary>The navigation host key when the study runs on its own; history requests from any other host are ignored.</summary>
    internal const string Host = "CapWordsApp";

    public static NavigationRoot<CapWordsMessage> Root(CapWordsModel model) => NavigationRoot<CapWordsMessage>.Create(
        model.Navigation, Host, _ => new CapWordsScreen(model).Map<CapWordsMessage>(static message => message),
        static request => new CapWordsMessage.Navigate(request));
}
