using Kumiko.Runtime;
using Kumiko.UI;

internal abstract partial record ResponsiveMessage
{
    public sealed partial record Increment : ResponsiveMessage;
    public sealed partial record Navigate(HistoryRequest Request) : ResponsiveMessage;
}
internal sealed record ResponsiveModel(int Count = 0)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("probe");
}
internal sealed partial class ResponsiveApp : IKumiko<ResponsiveModel, ResponsiveMessage>
{
    public static ResponsiveModel Init => new();
    public static (ResponsiveModel, Cmd<ResponsiveMessage>) Update(ResponsiveModel model, ResponsiveMessage message) => (message is ResponsiveMessage.Navigate navigate
        ? model with
        {
            Navigation = model.Navigation.Move(navigate.Request, "responsive")
        }
        : model with
        {
            Count = model.Count + 1
        }, Cmd<ResponsiveMessage>.None);
    public static NavigationRoot<ResponsiveMessage> Root(ResponsiveModel model) => NavigationRoot<ResponsiveMessage>.Create(
        model.Navigation, "responsive", _ => new ResponsiveScreen(model.Count).Map<ResponsiveMessage>(static m => m),
        static request => new ResponsiveMessage.Navigate(request));
}
internal sealed class ResponsiveScreen(int count) : Kumiko.Runtime.Screen<int, ResponsiveMessage>(count)
{
    protected override void Render()
    {
        using var root = C.Div(key: "background").SizeFull().Background(C.Theme.Background);
        using var safe = C.Div(key: "safe").SizeFull()
            .Padding(LayoutEdge.Left, C.SafeAreaInsets.Left)
            .Padding(LayoutEdge.Right, C.SafeAreaInsets.Right)
            .Padding(LayoutEdge.Top, C.SafeAreaInsets.Top)
            .Padding(LayoutEdge.Bottom, C.SafeAreaInsets.Bottom).Gap(Space.S3);
        C.Text($"{C.HorizontalSizeClass}/{C.VerticalSizeClass}/{C.WidthBreakpoint}", key: "environment");
        C.Text($"{C.SafeAreaInsets.Top},{C.SafeAreaInsets.Right},{C.SafeAreaInsets.Bottom},{C.SafeAreaInsets.Left}", key: "insets");
        using var panes = C.Div(key: "panes").FillWidth().Flex().Gap(Space.S3);
        if (C.HorizontalSizeClass == SizeClass.Regular)
            panes.Row();
        using (C.Div(key: "list").FillWidth().Flex())
            Ui.Button("Select company", new ResponsiveMessage.Increment(), key: "select");
        using (C.Div(key: "details").FillWidth().Flex())
            Ui.Button($"Save {Model}", new ResponsiveMessage.Increment(), key: "save");
    }
}
