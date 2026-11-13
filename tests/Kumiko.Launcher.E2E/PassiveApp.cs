using Kumiko.Runtime;
using Kumiko.UI;

public enum PassiveRoute
{
    Main
}
public sealed record PassiveModel(NavigationHistory<PassiveRoute> Navigation);
public abstract partial record PassiveMsg
{
    public sealed record None : PassiveMsg;
}
public sealed partial class PassiveApp : IKumiko<PassiveModel, PassiveMsg>
{
    public static PassiveModel Init => new(NavigationHistory<PassiveRoute>.Start(PassiveRoute.Main));
    public static (PassiveModel, Cmd<PassiveMsg>) Update(PassiveModel model, PassiveMsg message) => (model, Cmd<PassiveMsg>.None);
    public static NavigationRoot<PassiveMsg> Root(PassiveModel model) => NavigationRoot<PassiveMsg>.Create(model.Navigation, "passive",
        _ => new PassiveScreen(0).Map<PassiveMsg>(static message => message), static _ => new PassiveMsg.None());
}
public sealed class PassiveScreen(int value) : Screen<int, PassiveMsg>(value)
{
    protected override void Render()
    {
        using var root = C.Div(key: "passive").SizeFull();
        C.Text("Native panel input boundary", key: "label");
    }
}
