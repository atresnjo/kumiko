using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    public static Kumiko.Runtime.NavigationRoot<CrmMsg> Root(CrmModel model) => Kumiko.Runtime.NavigationRoot<CrmMsg>.Create(
        model.RootHistory, RootHost, _ => new CrmScreen(model).Map<CrmMsg>(static message => message),
        static request => new CrmMsg.MoveRoot(request));
}

internal sealed class CrmScreen(CrmModel model) : Kumiko.Runtime.Screen<CrmModel, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model), "crm", static message => message);
}
