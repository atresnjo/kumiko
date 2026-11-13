using CoreGraphics;
using Kumiko.iOS;
using UIKit;

internal static class ResponsiveEnvironmentFlow
{
    internal static void Run(string output)
    {
        using var program = (Kumiko.Runtime.Program<ResponsiveModel, ResponsiveMessage>)ResponsiveApp.Create();
        using var parent = new UIViewController();
        // Private hidden window only supplies UIKit safe-area propagation.
#pragma warning disable CA1422
        using var window = new UIWindow(new CGRect(0, 0, 1024, 1366)) { RootViewController = parent, Hidden = true, OverrideUserInterfaceStyle = UIUserInterfaceStyle.Light };
#pragma warning restore CA1422
        parent.LoadViewIfNeeded();
        window.AddSubview(parent.View!);
        using var controller = new UIViewController();
        parent.AddChildViewController(controller);
        parent.View!.AddSubview(controller.View!);
        controller.DidMoveToParentViewController(parent);
        using var view = new NativeView(program);
        controller.View!.AddSubview(view);
        var errors = new List<Exception>();
        view.UnhandledException += errors.Add;
        foreach (var (width, height) in new[] { (320, 640), (390, 844), (844, 390), (320, 1024), (507, 1024), (678, 1024), (1024, 1366) })
        {
            window.Frame = new CGRect(0, 0, width, height);
            parent.View.Frame = window.Bounds;
            controller.View.Frame = parent.View.Bounds;
            view.Frame = controller.View.Bounds;
            var horizontal = width >= 678 && height > 390 ? UIUserInterfaceSizeClass.Regular : UIUserInterfaceSizeClass.Compact;
            var vertical = height < 600 ? UIUserInterfaceSizeClass.Compact : UIUserInterfaceSizeClass.Regular;
            SetTraits(parent, controller, horizontal, vertical);
            controller.AdditionalSafeAreaInsets = new UIEdgeInsets(20, 16, 24, 12);
            parent.View.LayoutIfNeeded();
            controller.View.LayoutIfNeeded();
            view.Render();
            Require(Label(view, "environment").Text!.StartsWith($"{horizontal}/{vertical}/", StringComparison.Ordinal), "native traits reach rendered content");
            Require(Label(view, "insets").Text == "20,12,24,16", $"controller safe area reaches content in physical edge order: {Label(view, "insets").Text} native={view.SafeAreaInsets} controller={controller.View.SafeAreaInsets}");
            var save = Find<UIButton>(view, "save");
            var bounds = save.ConvertRectToView(save.Bounds, view);
            Require(bounds.X >= 16 && bounds.Right <= width - 12 && bounds.Y >= 20 && bounds.Bottom <= height - 24,
                "foreground action fits inside the safe area");
            var before = program.Model.Count;
            save.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
            view.Render();
            Require(program.Model.Count == before + 1 && ReferenceEquals(save, Find<UIButton>(view, "save")), "action after reflow dispatches once and retains native identity");
            ResponsiveFlow.Capture(view, output, $"environment-{width}x{height}");
        }
        // Bounds stay fixed: only traits and insets change. Exercise UIKit invalidation,
        // without calling Render to manufacture the expected environment update.
        SetTraits(parent, controller, UIUserInterfaceSizeClass.Compact, UIUserInterfaceSizeClass.Regular);
        parent.View.LayoutIfNeeded();
        controller.View.LayoutIfNeeded();
        view.LayoutIfNeeded();
        Require(Label(view, "environment").Text!.StartsWith("Compact/", StringComparison.Ordinal), "trait-only changes rebuild through native layout");
        controller.AdditionalSafeAreaInsets = new UIEdgeInsets(30, 18, 26, 14);
        parent.View.LayoutIfNeeded();
        controller.View.LayoutIfNeeded();
        view.LayoutIfNeeded();
        Require(Label(view, "insets").Text == "30,14,26,18", "safe-area-only changes rebuild through native layout");
        view.Frame = controller.View.SafeAreaLayoutGuide.LayoutFrame;
        view.SetNeedsLayout();
        view.LayoutIfNeeded();
        Require(Label(view, "insets").Text == "0,0,0,0", "already safe-area-framed hosts do not double-inset content");
        Require(window.Hidden, "safe-area integration window remains hidden");
        Require(errors.Count == 0, "no native callback exceptions");
        ResponsiveFlow.Capture(view, output, "environment-updated");
    }

    internal static void SetTraits(UIViewController parent, UIViewController controller,
        UIUserInterfaceSizeClass horizontal, UIUserInterfaceSizeClass vertical)
    {
        if (OperatingSystem.IsIOSVersionAtLeast(17))
        {
            controller.TraitOverrides.HorizontalSizeClass = horizontal;
            controller.TraitOverrides.VerticalSizeClass = vertical;
            return;
        }
        // Exercise the supported legacy path without retaining temporary traits.
#pragma warning disable CA1422
        using var h = UITraitCollection.FromHorizontalSizeClass(horizontal);
        using var v = UITraitCollection.FromVerticalSizeClass(vertical);
        using var traits = UITraitCollection.FromTraitsFromCollections([h, v]);
        parent.SetOverrideTraitCollection(traits, controller);
#pragma warning restore CA1422
    }

    internal static T Find<T>(UIView view, string key) where T : UIView => ResponsiveFlow.All(view).OfType<T>().Single(v => v.AccessibilityIdentifier == key);
    static UILabel Label(UIView view, string key) => Find<UILabel>(view, key);
    internal static void Require(bool value, string description)
    {
        if (!value)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
