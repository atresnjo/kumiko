using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    public static Kumiko.Runtime.NavigationRoot<CrmMsg> Root(CrmModel model) => Kumiko.Runtime.NavigationRoot<CrmMsg>.Create(
        model.RootHistory, RootHost, _ => new CrmIosView(model).Map<CrmMsg>(static message => message),
        static request => new CrmMsg.MoveRoot(request));
}
