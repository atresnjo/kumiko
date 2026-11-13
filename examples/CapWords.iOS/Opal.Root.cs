using Kumiko.Runtime;

internal sealed partial class OpalApp
{
    /// <summary>The navigation host key when the study runs on its own; history requests from any other host are ignored.</summary>
    internal const string Host = "OpalApp";

    public static NavigationRoot<OpalMessage> Root(OpalModel model) => NavigationRoot<OpalMessage>.Create(
        model.Navigation, Host, _ => new OpalScreen(model).Map<OpalMessage>(static message => message),
        static request => new OpalMessage.Navigate(request));
}

internal sealed class OpalScreen(OpalModel model) : Screen<OpalModel, OpalMessage>(model)
{
    protected override void Render() => Ui.Child(new OpalView(Model), "timer", static message => message);
}
