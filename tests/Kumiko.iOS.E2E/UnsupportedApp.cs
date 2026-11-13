using Kumiko.Runtime;
using Kumiko.UI;

internal sealed partial class UnsupportedApp : IKumiko<NativeButtonModel, NativeButtonMessage>
{
    public static NativeButtonModel Init => new();
    public static (NativeButtonModel, Cmd<NativeButtonMessage>) Update(NativeButtonModel model, NativeButtonMessage message) =>
        NativeButtonApp.Update(model, message);
    public static NavigationRoot<NativeButtonMessage> Root(NativeButtonModel model) => NavigationRoot<NativeButtonMessage>.Create(
        model.Navigation, NativeButtonApp.Host, _ => new UnsupportedScreen(model.Count).Map<NativeButtonMessage>(static message => message),
        static request => new NativeButtonMessage.Navigate(request));
}

/// <summary>Draws a shadowed box, which a native-controls view must reject rather than drop silently.</summary>
internal sealed class UnsupportedScreen(int count) : Screen<int, NativeButtonMessage>(count)
{
    protected override void Render()
    {
        using (C.Div(key: "shadowed").Width(20).Height(20).Shadow(12, 4, Color.Black))
        {
        }
    }
}
