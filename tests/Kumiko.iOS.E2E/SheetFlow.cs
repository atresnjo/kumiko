using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using UIKit;

internal static class SheetFlow
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var program = FocusApp.CreateProgram();
        program.Record(Path.Combine(output, "sheet-session.jsonl"));
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        view.Render();
        program.Send(new FocusMessage.OpenDrawer("duration"));
        view.Render();
        var panel = All(view).Single(child => child.AccessibilityIdentifier == "sheet");
        var owner = Owner(panel);
        Capture(view, Path.Combine(output, "sheet-before.png"));
        File.WriteAllText(Path.Combine(output, "sheet-capabilities.json"),
            owner?.SheetPresentationController is null ? "{\"systemSheet\":false}" : "{\"systemSheet\":true}");
        Require(owner?.SheetPresentationController is not null, "Bottom drawer is owned by a UISheetPresentationController");
        var sheet = owner!.SheetPresentationController!;
        Require(sheet.Detents.Length >= 2, "System sheet has collapsed and expanded detents");
        Require(sheet.PrefersScrollingExpandsWhenScrolledToEdge, "UIKit owns scroll-to-expand handoff");
        Require(!owner.ModalInPresentation, "System pull-to-dismiss is enabled");
        Require(sheet.Delegate is not null, "System dismissal is connected to Kumiko state");
        sheet.Delegate!.DidDismiss(sheet);
        PumpUntil(() => program.Model.Drawer is null);
        Require(All(view).All(child => child.AccessibilityIdentifier != "sheet"), "Native dismissal removes the drawer");
        program.Send(new FocusMessage.OpenDrawer("blocking"));
        view.Render();
        var next = All(view).Single(child => child.AccessibilityIdentifier == "sheet");
        var nextOwner = Owner(next)!;
        Require(!ReferenceEquals(owner, nextOwner), "Reopening creates a fresh controller lifetime");
        var table = All(view).OfType<UITableView>().Single(child => child.AccessibilityIdentifier == "drawer-block-list");
        using (var row = NSIndexPath.FromRowSection(0, 0))
            table.Source!.RowSelected(table, row);
        PumpUntil(() => !program.Model.Blocked.Contains("app-mail"));
        var collapsedHeight = table.Frame.Height;
        var nextSheet = nextOwner.SheetPresentationController!;
        nextSheet.SelectedDetentIdentifier = UISheetPresentationControllerDetentIdentifier.Large;
        nextSheet.Delegate!.DidChangeSelectedDetentIdentifier(nextSheet);
        view.Render();
        Require(table.Frame.Height > collapsedHeight, "Expanded detent gives the native list more room");
        Require(!program.Model.Blocked.Contains("app-mail") && program.Model.Drawer == "blocking", "Detent change preserves selection and open state");
        Capture(view, Path.Combine(output, "sheet-expanded.png"));
        nextSheet.Delegate.DidDismiss(nextSheet);
        PumpUntil(() => program.Model.Drawer is null);
        Capture(view, Path.Combine(output, "sheet-after.png"));
        HiddenPresentation();
        File.WriteAllText(Path.Combine(output, "sheet-report.json"),
            """
            {"passed":true,"systemSheet":true,"nativeDetents":true,"scrollExpansion":true,
             "nativeDismissalUpdatesModel":true,"reopen":true,"selectionRetained":true,
             "hiddenPresentation":true,"programmaticDismissal":"pending","completedTransitions":false,
             "offscreen":true,"syntheticTouch":false}
            """);
    }

    static void HiddenPresentation()
    {
        using var program = FocusApp.CreateProgram();
#pragma warning disable CA1422 // This headless executable has no UIWindowScene; the window always stays hidden.
        using var window = new UIWindow(new CGRect(0, 0, 393, 852));
#pragma warning restore CA1422
        using var host = new UIViewController();
        using var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        window.Hidden = true;
        window.RootViewController = host;
        host.LoadViewIfNeeded();
        if (host.View!.Superview is null)
            window.AddSubview(host.View);
        host.View.AddSubview(view);
        view.Render();
        program.Send(new FocusMessage.OpenDrawer("duration"));
        view.Render();
        PumpUntil(() => host.PresentedViewController?.SheetPresentationController is not null);
        Require(window.Hidden, "Presentation verification never shows the window");
        Require(host.PresentedViewController!.PresentingViewController == host, "UIKit presents the sheet from the actual host controller");
        // This process has no UIApplication, so UIKit starts the presentation but never completes its
        // transition, and UIKit ignores dismissal until it does. Verify that closing in the model leaves the
        // dismissal pending on that same controller rather than presenting another one; completed dismissal
        // and reopening need a real application process.
        var presented = host.PresentedViewController;
        program.Send(new FocusMessage.CloseDrawer());
        view.Render();
        Require(program.Model.Drawer is null, "Closing the drawer updates the model immediately");
        Require(host.PresentedViewController is null || ReferenceEquals(host.PresentedViewController, presented),
            "A pending presentation is dismissed, not replaced");
        Require(window.Hidden, "Native sheet presentation stays offscreen");
    }

    static UIViewController? Owner(UIView view)
    {
        for (UIResponder? current = view; current is not null; current = current.NextResponder)
            if (current is UIViewController controller)
                return controller;
        return null;
    }
    static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static void Capture(UIView view, string path)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(path, png.ToArray());
    }
    static void PumpUntil(Func<bool> done)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        Require(done(), "Native sheet lifecycle delivered the model change");
    }
    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
