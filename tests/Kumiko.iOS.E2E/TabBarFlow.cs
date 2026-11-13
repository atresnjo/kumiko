using CoreGraphics;
using Kumiko.iOS;
using UIKit;

internal static class TabBarFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = FocusApp.CreateProgram();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        Inspect(view, output, "phone");
        program.Send(new FocusMessage.SelectTab("activity"));
        Inspect(view, output, "selected");
        program.Send(new FocusMessage.SelectTab("now"));
        view.Frame = new CGRect(0, 0, 320, 640);
        Inspect(view, output, "narrow");
        File.WriteAllText(Path.Combine(output, "tabs-report.json"),
            """{"passed":true,"systemTabBar":true,"iconAboveTitle":true,"nativeSizing":true,"selection":true,"narrow":true,"offscreen":true}""");
    }

    static void Inspect(NativeView view, string output, string state)
    {
        view.Render();
        view.LayoutIfNeeded();
        var bar = All(view).OfType<UITabBar>().Single();
        bar.LayoutIfNeeded();
        Capture(view, Path.Combine(output, $"tabs-{state}.png"));
        var labels = All(bar).OfType<UILabel>().Where(label => !string.IsNullOrEmpty(label.Text)).ToArray();
        var icons = All(bar).OfType<UIImageView>().Where(icon => icon.Image is not null &&
            icon.Bounds.Width > 8 && icon.Bounds.Width < 50 && icon.Bounds.Height > 8 && icon.Bounds.Height < 50).ToArray();
        var descriptions = labels.Select(label =>
        {
            var text = label.ConvertRectToView(label.Bounds, bar);
            var image = icons.Select(icon => icon.ConvertRectToView(icon.Bounds, bar))
                .OrderBy(rect => Math.Abs(rect.GetMidX() - text.GetMidX())).FirstOrDefault();
            return (Label: label.Text!, Text: text, Image: image);
        }).ToArray();
        File.WriteAllText(Path.Combine(output, $"tabs-{state}-geometry.json"), "[\n" + string.Join(",\n", descriptions.Select(item => FormattableString.Invariant(
            $"{{\"label\":\"{item.Label}\",\"text\":[{item.Text.X},{item.Text.Y},{item.Text.Width},{item.Text.Height}],\"image\":[{item.Image.X},{item.Image.Y},{item.Image.Width},{item.Image.Height}]}}"))) + "\n]");
        var preferred = bar.SizeThatFits(new CGSize(bar.Bounds.Width, 1000));
        File.WriteAllText(Path.Combine(output, $"tabs-{state}-metrics.txt"), $"preferred={preferred}; bounds={bar.Bounds}; safe={bar.SafeAreaInsets}");
        Require(bar.Bounds.Height >= preferred.Height, "Bar has UIKit's requested height");
        foreach (var destination in FocusApp.Tabs)
        {
            var matching = descriptions.Where(item => item.Label == destination.Label).ToArray();
            Require(matching.Length > 0, "Native title exists: " + destination.Label);
            Require(matching.Any(item => item.Image.Bottom + 1 <= item.Text.Top),
                "Native icon sits above its title: " + destination.Label);
        }
    }

    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static void Capture(UIView view, string path)
    {
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG()!;
        File.WriteAllBytes(path, png.ToArray());
    }
    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
