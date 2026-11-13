using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using UIKit;

internal static class NativeTextLayoutFlow
{
    // Failure scenarios: stale wrapping after a text edit, stale metrics after a font
    // design or Dynamic Type change, lost native identity, or a host layout notification
    // failing to remeasure a resized or trait-changed view.
    internal static void Run(string output)
    {
        using var program = NativeTextLayoutApp.CreateProgram();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 280, 900) };
        view.Render();
        var label = Find(view);
        var shortHeight = label.Frame.Height;
        program.Send(new NativeButtonMessage.Increment());
        view.Render();
        Require(ReferenceEquals(label, Find(view)) && label.Frame.Height > shortHeight * 2,
            "Changing native text reflows the retained label");
        var wrappedHeight = label.Frame.Height;
        view.Render();
        Require(label.Frame.Height == wrappedHeight, "Repeated native layout preserves wrapping");
        program.Send(new NativeButtonMessage.Increment());
        view.Render();
        using (var expected = UIFont.GetMonospacedSystemFont(20, UIFontWeight.Regular) ?? throw new InvalidOperationException("System monospaced font unavailable"))
            Require(label.Font.FamilyName == expected.FamilyName, "Changing font design updates the retained native label");
        var designed = label.SizeThatFits(new CGSize(label.Frame.Width, nfloat.MaxValue));
        Require(Math.Abs(label.Frame.Height - Math.Ceiling(designed.Height)) < 1,
            "Font design changes reflow with the displayed font metrics");
        var ordinarySize = label.Font.PointSize;
        if (!OperatingSystem.IsIOSVersionAtLeast(17))
            throw new PlatformNotSupportedException("Trait overrides require iOS 17.");
        var originalCategory = view.TraitCollection.PreferredContentSizeCategory;
        view.TraitOverrides.PreferredContentSizeCategory = UIContentSizeCategory.AccessibilityExtraExtraExtraLarge.GetConstant()!.ToString();
        view.UpdateTraitsIfNeeded();
        using var large = UITraitCollection.Create(UIContentSizeCategory.AccessibilityExtraExtraExtraLarge);
        Exception? traitError = null;
        large.PerformAsCurrentTraitCollection(() =>
        {
            try
            {
                view.SetNeedsLayout();
                view.LayoutIfNeeded();
                Require(label.Font.PointSize > ordinarySize && label.Frame.Height > wrappedHeight,
                    $"Dynamic Type invalidates native text layout ({label.Font.PointSize}/{ordinarySize}, {label.Frame.Height}/{wrappedHeight})");
                view.Frame = new CGRect(0, 0, 400, 900);
                view.LayoutIfNeeded();
                var measured = label.SizeThatFits(new CGSize(label.Frame.Width, nfloat.MaxValue));
                Require(Math.Abs(label.Frame.Height - Math.Ceiling(measured.Height)) < 1,
                    "Resizing remeasures the enlarged wrapped label");
                using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
                using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
                using var image = renderer.CreateImage(c => view.Layer.RenderInContext(c.CGContext));
                using var png = image.AsPNG()!;
                File.WriteAllBytes(Path.Combine(output, "native-text-layout.png"), png.ToArray());
            }
            catch (Exception error)
            {
                traitError = error;
            }
        });
        if (traitError is not null)
            throw new InvalidOperationException("Native text trait verification failed.", traitError);
        view.TraitOverrides.PreferredContentSizeCategory = originalCategory;
        view.UpdateTraitsIfNeeded();
        view.Render();
        Require(label.Font.PointSize == ordinarySize, "Restoring Dynamic Type restores native geometry");
        File.WriteAllText(Path.Combine(output, "native-text-layout-report.json"),
            """{"passed":true,"textChange":true,"identity":true,"fontDesign":true,"dynamicType":true,"resize":true,"restoredMetrics":true}""");
    }

    static UILabel Find(UIView view) => view.Subviews.SelectMany(All).OfType<UILabel>()
        .Single(label => label.AccessibilityIdentifier == "copy");
    static IEnumerable<UIView> All(UIView view) => new[] { view }.Concat(view.Subviews.SelectMany(All));
    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}

internal sealed partial class NativeTextLayoutApp : IKumiko<NativeButtonModel, NativeButtonMessage>
{
    public static NativeButtonModel Init => new();
    public static (NativeButtonModel, Cmd<NativeButtonMessage>) Update(NativeButtonModel model, NativeButtonMessage message) =>
        NativeButtonApp.Update(model, message);
    public static NavigationRoot<NativeButtonMessage> Root(NativeButtonModel model) => NavigationRoot<NativeButtonMessage>.Create(
        model.Navigation, NativeButtonApp.Host, _ => new NativeTextLayoutScreen(model.Count).Map<NativeButtonMessage>(static message => message),
        static request => new NativeButtonMessage.Navigate(request));
}

/// <summary>The count selects the copy and font design so the same label rewraps.</summary>
internal sealed class NativeTextLayoutScreen(int count) : Screen<int, NativeButtonMessage>(count)
{
    protected override void Render()
    {
        using var root = C.Div(key: "root").SizeFull().Padding(16).Scroll();
        C.NativeLabel(Model == 0 ? "Short" :
            "Native text must wrap correctly when its words change, its font changes, or the available width changes.",
            new(Size: 20, Wrap: true), Model < 2 ? NativeFontDesign.Default : NativeFontDesign.Monospaced, key: "copy");
    }
}
