using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

/// <summary>
/// The NativeExactText opt-in (parts shared with desktop). Failure scenarios: plain text keeps
/// a Dynamic Type role or system color after the opt-in; the opt-in leaks into a frame without it; text built before
/// the call stays system-styled; unset colors ignore the theme; headings lose their trait; text below 11 points is
/// clamped instead of failing with its path; a named image box throws or lets VoiceOver read its children.
/// </summary>
internal static class ExactTextFlow
{
    internal static void Run(string output)
    {
        using var program = ExactTextApp.CreateProgram();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 320, 400) };
        view.Render();
        var colored = Find<UILabel>(view, "colored");
        // Dynamic Type scaling (UIFontMetrics) rounds to whole points, as NativeLabel does.
        using var asked = UIFont.SystemFontOfSize(13.5f)!;
        var scaled = UIFontMetrics.DefaultMetrics.GetScaledFont(asked, view.TraitCollection).PointSize;
        Require(colored.Font.PointSize == scaled && scaled != UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body, view.TraitCollection)!.PointSize &&
            Same(colored.TextColor, ExactTextScreen.Ink), "Exact text keeps its scaled size and color, even when built before the opt-in");
        Require(Same(Find<UILabel>(view, "themed").TextColor, Theme.Light.Text), "Exact text without a color takes the theme text color");
        Require(Find<UILabel>(view, "heading").AccessibilityTraits.HasFlag(UIAccessibilityTrait.Header), "Exact headings keep the header trait");
        var avatar = Find<UIView>(view, "avatar");
        Require(avatar.IsAccessibilityElement && avatar.AccessibilityLabel == "Maya Chen" &&
            avatar.AccessibilityTraits.HasFlag(UIAccessibilityTrait.Image) && Find<UILabel>(view, "initials").Font.PointSize == 13,
            "A named image box is one VoiceOver image around its exact initials");
        Capture(view, output, "exact-text");
        program.Send(new NativeButtonMessage.Increment());
        view.Render();
        var preferred = UIFont.GetPreferredFontForTextStyle(UIFontTextStyle.Body, view.TraitCollection)!.PointSize;
        var system = Find<UILabel>(view, "colored");
        Require(system.Font.PointSize == preferred && system.TextColor == UIColor.Label,
            "A frame without the opt-in restores system text roles and colors");
        Require(Find<UIView>(view, "avatar") is { IsAccessibilityElement: true, AccessibilityLabel: "Maya Chen" },
            "Named image boxes do not depend on exact text");
        program.Send(new NativeButtonMessage.Increment());
        string? message = null;
        try
        {
            view.Render();
        }
        catch (NotSupportedException error)
        {
            message = error.Message;
        }
        Require(message is not null && message.Contains("tiny", StringComparison.Ordinal) && message.Contains("9 pt", StringComparison.Ordinal),
            "Exact text below 11 points fails with its size and path");
    }

    static bool Same(UIColor? actual, Color expected)
    {
        if (actual is null)
            return false;
        actual.GetRGBA(out var r, out var g, out var b, out var a);
        return Math.Abs(r - expected.R) < .01 && Math.Abs(g - expected.G) < .01 && Math.Abs(b - expected.B) < .01 && Math.Abs(a - expected.A) < .01;
    }

    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>().Single(v => v.AccessibilityIdentifier == key);
    static IEnumerable<UIView> All(UIView view) => new[] { view }.Concat(view.Subviews.SelectMany(All));

    static void Capture(UIView view, string output, string name)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, name + ".png"), png.ToArray());
    }

    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}

internal sealed partial class ExactTextApp : IKumiko<NativeButtonModel, NativeButtonMessage>
{
    public static NativeButtonModel Init => new();
    public static (NativeButtonModel, Cmd<NativeButtonMessage>) Update(NativeButtonModel model, NativeButtonMessage message) =>
        NativeButtonApp.Update(model, message);
    public static NavigationRoot<NativeButtonMessage> Root(NativeButtonModel model) => NavigationRoot<NativeButtonMessage>.Create(
        model.Navigation, NativeButtonApp.Host, _ => new ExactTextScreen(model.Count).Map<NativeButtonMessage>(static message => message),
        static request => new NativeButtonMessage.Navigate(request));
}

/// <summary>Count 0 opts in after building its first text, 1 draws the same tree without the opt-in, 2 asks for 9-point text.</summary>
internal sealed class ExactTextScreen(int count) : Screen<int, NativeButtonMessage>(count)
{
    internal static Color Ink => Color.Hex(0x0d9488);

    protected override void Render()
    {
        using var root = C.Div(key: "root").SizeFull().Padding(16).Column().Gap(8);
        C.Text("Exact", new(Size: 13.5f, Color: Ink), key: "colored");
        if (Model != 1)
            C.NativeExactText();
        C.Text("Themed", new(Size: 15), key: "themed");
        C.Heading("Section", 20, key: "heading");
        using (C.Div(key: "avatar").Width(32).Height(32).Center().Rounded(16).Background(Color.Hex(0x7c3aed)).Semantics(Role.Image, "Maya Chen"))
            C.Text("MC", new(Size: 13, Weight: Weight.Semibold, Color: Color.White), key: "initials");
        if (Model == 2)
            C.Text("Too small", new(Size: 9), key: "tiny");
    }
}
