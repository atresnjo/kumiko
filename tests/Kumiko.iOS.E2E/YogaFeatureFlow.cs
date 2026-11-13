using CoreGraphics;
using Kumiko.iOS;
using Kumiko.UI;
using UIKit;

internal static class YogaFeatureFlow
{
    internal static void Run(string output)
    {
        using var program = YogaFeaturesApp.CreateProgram();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 390, 720) };
        view.Render();
        CheckGeometry(view);
        Capture(view, output, "phone");
        var button = Find<UIButton>(view, "toggle");
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        Require(program.Model.Hidden && !All(view).OfType<UIButton>().Any(v => v.AccessibilityIdentifier == "hidden-action"),
            "Display-none removes native controls and a native button dispatches the toggle once");
        Capture(view, output, "hidden");
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        Require(!program.Model.Hidden && ReferenceEquals(button, Find<UIButton>(view, "toggle")) &&
            Find<UIButton>(view, "hidden-action") is not null, "Visibility changes preserve native identity for retained controls");
        view.Frame = new CGRect(0, 0, 720, 720);
        view.Render();
        CheckGeometry(view);
        Capture(view, output, "wide");
        using var rtlProgram = YogaFeaturesApp.CreateProgram(new YogaFeaturesModel(Rtl: true));
        using var rtl = new NativeView(rtlProgram) { Frame = new CGRect(0, 0, 390, 720) };
        rtl.Render();
        Require(Near(Box(rtl, "auto-last").X, 12), "UIKit logical auto margins mirror in RTL");
        Capture(rtl, output, "rtl");
        File.WriteAllText(Path.Combine(output, "yoga-features-report.json"),
            """{"passed":true,"offscreen":true,"autoMargins":true,"relativeUnits":true,"insets":true,"boxSizing":true,"display":true,"baseline":true,"rtl":true,"nativeAction":true}""");
    }

    static void CheckGeometry(UIView view)
    {
        var width = (float)view.Bounds.Width - 24;
        Require(Near(Box(view, "auto-last").Right, (float)view.Bounds.Width - 12), "UIKit automatic margins consume free space");
        Require(Near(Box(view, "content-box").Width, 100) && Near(Box(view, "border-box").Width, 80),
            "UIKit content-box and border-box dimensions match desktop");
        Require(Near(Box(view, "minimum").Width, width * .25f) && Near(Box(view, "maximum").Width, width * .4f) &&
            Near(Box(view, "maximum").X - Box(view, "minimum").Right, width * .04f),
            "UIKit percentage bounds and gaps respond to resizing");
        var insets = Box(view, "insets");
        Require(Near(Box(view, "pinned").Right, insets.Right - 8) &&
            Near(Box(view, "stretched").Width, insets.Width - 16), "UIKit opposing and logical insets match desktop");
        Require(Find<UIView>(view, "overflow").ClipsToBounds, "UIKit hidden overflow uses a clipping native box");
        Require(Near(Box(view, "static-marker").X, insets.X + 5) && Near(Box(view, "static-marker").Y, insets.Y + 5),
            "UIKit static-scope parenting preserves the containing block's coordinates");
        var reference = Find<UILabel>(view, "reference");
        var selected = Find<UILabel>(view, "selected-baseline");
        var a = reference.ConvertPointToView(new CGPoint(0, reference.Font.Ascender), view);
        var b = selected.ConvertPointToView(new CGPoint(0, selected.Font.Ascender), view);
        Require(Math.Abs(a.Y - b.Y) < 1, "UIKit reference baseline uses the selected nested native label");
        Require(!All(view).Any(v => v.AccessibilityIdentifier == "contents"), "Display-contents creates no native box");
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < .1f;
    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(v => new[] { v }.Concat(All(v)));
    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>().Single(v => v.AccessibilityIdentifier == key);
    static Rect Box(UIView view, string key)
    {
        var child = Find<UIView>(view, key);
        var p = child.ConvertPointToView(CGPoint.Empty, view);
        return new((float)p.X, (float)p.Y, (float)child.Bounds.Width, (float)child.Bounds.Height);
    }
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
        File.WriteAllBytes(Path.Combine(output, $"yoga-features-{name}.png"), png.ToArray());
        var keys = new[] { "auto-last", "percent-child", "minimum", "maximum", "content-box", "border-box", "pinned", "stretched" };
        File.WriteAllText(Path.Combine(output, $"yoga-features-{name}.json"), "[" + string.Join(",", keys.Select(key =>
        {
            var r = Box(view, key);
            return FormattableString.Invariant($"{{\"key\":\"{key}\",\"x\":{r.X},\"y\":{r.Y},\"width\":{r.Width},\"height\":{r.Height}}}");
        })) + "]\n");
    }
}
