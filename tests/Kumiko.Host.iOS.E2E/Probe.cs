using System.Text.Json;
using System.Text.Json.Nodes;
using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using UIKit;

/// <summary>Hidden-window hosting, native actions, captures and assertions shared by the host checks.</summary>
internal static class Probe
{
    internal static UIWindow Window(UIViewController controller)
    {
#pragma warning disable CA1422 // This headless executable has no UIWindowScene; the window always stays hidden.
        var window = new UIWindow(new CGRect(0, 0, 393, 852));
#pragma warning restore CA1422
        window.Hidden = true;
        // Hosted as a child, as inside a navigation or split controller: a scene-less root ignores extra safe-area insets.
        var parent = new UIViewController();
        window.RootViewController = parent;
        parent.View!.Frame = window.Bounds;
        // A hidden window does not install its root view; add it so safe-area propagation runs.
        window.AddSubview(parent.View);
        parent.AddChildViewController(controller);
        controller.View!.Frame = parent.View.Bounds;
        parent.View.AddSubview(controller.View);
        controller.DidMoveToParentViewController(parent);
        window.LayoutIfNeeded();
        controller.View.LayoutIfNeeded();
        Require(window.Hidden, "Host checks never show a window");
        return window;
    }

    internal static void Detach(UIWindow window, UIViewController controller)
    {
        var parent = window.RootViewController;
        controller.WillMoveToParentViewController(null);
        controller.View!.RemoveFromSuperview();
        controller.RemoveFromParentViewController();
        parent?.View?.RemoveFromSuperview();
        window.RootViewController = null;
        parent?.Dispose();
    }

    internal static void Settle(NativeViewController controller)
    {
        controller.View!.SetNeedsLayout();
        controller.View.LayoutIfNeeded();
        controller.Content?.Render();
        Pump(TimeSpan.FromMilliseconds(60));
    }

    internal static void Tap(UIView root, string identifier)
    {
        var button = All(root).OfType<UIButton>().Single(view => view.AccessibilityIdentifier == identifier);
        button.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
    }

    internal static IEnumerable<UIView> All(UIView view) => view.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));

    internal static string Text(UIView root) => string.Join("\n", All(root).Select(view => view switch
    {
        UILabel label => label.Text,
        UIButton button => button.Configuration?.Title ?? button.Title(UIControlState.Normal),
        _ => null
    }).Where(text => !string.IsNullOrEmpty(text)));

    internal static void Post(NSString name) => NSNotificationCenter.DefaultCenter.PostNotificationName(name, null);

    internal static void Capture(UIView view, string path)
    {
        view.LayoutIfNeeded();
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(path, png.ToArray());
    }

    internal static void Pump(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }

    internal static void PumpUntil(Func<bool> done, string description)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.02));
        Require(done(), description);
    }

    internal static T Throws<T>(Action action, string description) where T : Exception
    {
        try
        {
            action();
        }
        catch (T error)
        {
            Console.WriteLine("PASS " + description + ": " + error.Message);
            return error;
        }
        throw new InvalidOperationException(description + ": no " + typeof(T).Name + " was thrown");
    }

    internal static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }

    internal static void Report(string path, List<string> checks)
    {
        var report = new JsonObject
        {
            ["passed"] = true,
            ["offscreen"] = true,
            ["syntheticTouch"] = false,
            ["checks"] = new JsonArray([.. checks.Select(check => JsonValue.Create(check))])
        };
        File.WriteAllText(path, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
