using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

internal static class FlexboxFlow
{
    internal static void Run(string output)
    {
        using var program = FlexboxApp.Create();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 260, 400) };
        view.Render();
        var button = Find<UIButton>(view, "button-3");
        var card = Find<UIView>(view, "card-3");
        Require(card.ConvertPointToView(CGPoint.Empty, view).Y == 95 && card.Frame.Width == 100 && Math.Abs(card.Frame.Height - 60) < .01,
            "UIKit cards wrap using margins and aspect ratio");
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        Require(((Program<NativeButtonModel, NativeButtonMessage>)program).Model.Count == 1,
            "Reflowed native button dispatches once");
        Baseline(view);
        Capture(view, output, "phone");
        view.Frame = new CGRect(0, 0, 520, 400);
        view.Render();
        Require(ReferenceEquals(button, Find<UIButton>(view, "button-3")) && card.ConvertPointToView(CGPoint.Empty, view).Y == 15,
            "UIKit reflow unwraps cards while retaining native identity");
        Capture(view, output, "wide");
        YogaFeatureFlow.Run(output);
        NativeTextLayoutFlow.Run(output);
        ExactTextFlow.Run(output);
        File.WriteAllText(Path.Combine(output, "flexbox-report.json"),
            """{"passed":true,"offscreen":true,"wrapping":true,"margins":true,"aspectRatio":true,"baseline":true,"identity":true,"nativeAction":true}""");
    }

    static void Baseline(UIView view)
    {
        var large = Find<UILabel>(view, "large");
        var small = Find<UILabel>(view, "small");
        var a = large.ConvertPointToView(new CGPoint(0, large.Font.Ascender), view);
        var b = small.ConvertPointToView(new CGPoint(0, small.Font.Ascender), view);
        Require(Math.Abs(a.Y - b.Y) < 1, "UIKit baseline layout uses native font metrics through nested containers");
    }
    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(v => new[] { v }.Concat(All(v)));
    static T Find<T>(UIView view, string key) where T : UIView =>
        All(view).OfType<T>().Single(v => v.AccessibilityIdentifier == key);
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
    static void Capture(UIView view, string output, string name)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(c => view.Layer.RenderInContext(c.CGContext));
        using var png = image.AsPNG()!;
        File.WriteAllBytes(Path.Combine(output, $"flexbox-{name}.png"), png.ToArray());
    }
}

internal sealed partial class FlexboxApp : IKumiko<NativeButtonModel, NativeButtonMessage>
{
    public static NativeButtonModel Init => new();
    public static (NativeButtonModel, Cmd<NativeButtonMessage>) Update(NativeButtonModel model, NativeButtonMessage message) =>
        NativeButtonApp.Update(model, message);
    public static NavigationRoot<NativeButtonMessage> Root(NativeButtonModel model) => NavigationRoot<NativeButtonMessage>.Create(
        model.Navigation, NativeButtonApp.Host, _ => new FlexboxScreen(model.Count).Map<NativeButtonMessage>(static message => message),
        static request => new NativeButtonMessage.Navigate(request));
}

/// <summary>Wrapped cards and a baseline row. The count is the number of card presses.</summary>
internal sealed class FlexboxScreen(int count) : Screen<int, NativeButtonMessage>(count)
{
    protected override void Render()
    {
        var c = C;
        using var root = c.Div(key: "root").SizeFull().Padding(10).Gap(10);
        using (c.Div(key: "cards").Row().Wrap().FillWidth().Gap(10))
        {
            for (var i = 0; i < 4; i++)
            {
                using var margin = c.Div(key: $"margin-{i}").Margin(5);
                using var card = c.Div(key: $"card-{i}").Width(100).AspectRatio(5f / 3)
                    .Background(Color.Hex(0xeeeeee));
                Ui.Button($"Card {i}", new NativeButtonMessage.Increment(), key: $"button-{i}");
            }
        }
        using (c.Div(key: "baseline").Row().Items(Align.Baseline).Gap(10))
        {
            c.Text("Large", new(Size: 28), key: "large");
            using var nested = c.Div(key: "nested").Padding(5);
            c.Text("Small", new(Size: 12), key: "small");
        }
    }
}
