using CoreAnimation;
using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

internal sealed record MotionProbeModel(bool Expanded = false)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("probe");
}
internal abstract partial record MotionProbeMessage
{
    public sealed record Toggle : MotionProbeMessage;
    public sealed record Navigate(HistoryRequest Request) : MotionProbeMessage;
}

internal sealed partial class MotionProbeApp : IKumiko<MotionProbeModel, MotionProbeMessage>
{
    public static MotionProbeModel Init => new();
    public static (MotionProbeModel, Cmd<MotionProbeMessage>) Update(MotionProbeModel model, MotionProbeMessage message) =>
        (message is MotionProbeMessage.Navigate navigate
            ? model with
            {
                Navigation = model.Navigation.Move(navigate.Request, "app")
            }
            : model with
            {
                Expanded = !model.Expanded
            }, Cmd<MotionProbeMessage>.None);

    public static NavigationRoot<MotionProbeMessage> Root(MotionProbeModel model) => NavigationRoot<MotionProbeMessage>.Create(
        model.Navigation, "app", _ => new MotionProbeScreen(model.Expanded).Map<MotionProbeMessage>(static message => message),
        static request => new MotionProbeMessage.Navigate(request));
}

/// <summary>One square that moves between a small and an expanded pose.</summary>
internal sealed class MotionProbeScreen(bool expanded) : Screen<bool, MotionProbeMessage>(expanded)
{
    protected override void Render()
    {
        var ui = Ui;
        var expanded = Model;
        var c = ui.Context;
        using var page = c.Div(key: "probe-page").SizeFull().Background(Color.White);
        using (ui.NativePressable("Move", new MotionProbeMessage.Toggle(), key: "move").Width(80).Height(44).Center())
            c.NativeLabel("Move", new(Size: 17, Color: Color.Black), key: "move-label");
        using var owner = c.Div(key: expanded ? "large" : "small").Absolute().SizeFull();
        using var square = c.NativeSurface(new(Color.Hex(0xfb923c), Radius: 12), key: "probe-square").Absolute()
            .Offset(expanded ? 110 : 20, expanded ? 190 : 80)
            .Width(expanded ? 160 : 60).Height(expanded ? 160 : 60)
            .SharedLayout("probe-square", Motion.Quick);
    }
}

internal static class MotionProbe
{
    internal static async Task Run(UIView parent, string output)
    {
        using var program = MotionProbeApp.CreateProgram();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 320, 480) };
        parent.AddSubview(view);
        view.Render();
        await Task.Delay(80);
        CapWordsFlow.Tap(view, "move");
        view.Render();
        CATransaction.Flush();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(160);
        var square = CapWordsFlow.Find<UIView>(view, "probe-square");
        var shown = square.Layer.PresentationLayer ?? throw new InvalidOperationException("Missing Quick presentation layer.");
        var frame = shown.ConvertRectToLayer(shown.Bounds, view.Layer.PresentationLayer ?? view.Layer);
        var target = square.ConvertRectToView(square.Bounds, view);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        var error = Math.Max(Math.Abs(frame.X - target.X), Math.Max(Math.Abs(frame.Y - target.Y), Math.Abs(frame.Width - target.Width)));
        CapWordsFlow.Require(elapsed < 200, "Quick spring was sampled before its endpoint");
        CapWordsFlow.Require(error < 1.5, "Quick spring approaches its endpoint without a visible snap");
        CapWordsFlow.Capture(view, output, "quick-near-end", presentation: true);
        File.WriteAllText(Path.Combine(output, "quick-report.json"), FormattableString.Invariant(
            $"{{\"passed\":true,\"sampleMs\":{elapsed:F2},\"endpointErrorPoints\":{error:F4},\"durationMs\":200}}"));
        await Task.Delay(80);
        view.RemoveFromSuperview();
    }
}
