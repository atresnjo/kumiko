using Kumiko.Runtime;

internal sealed partial class UnwindApp
{
    /// <summary>The navigation host key when the study runs on its own; history requests from any other host are ignored.</summary>
    internal const string Host = "UnwindApp";

    public static NavigationRoot<UnwindMessage> Root(UnwindModel model) => NavigationRoot<UnwindMessage>.Create(
        model.Navigation, Host, _ => new UnwindScreen(model).Map<UnwindMessage>(static message => message),
        static request => new UnwindMessage.Navigate(request));
}
