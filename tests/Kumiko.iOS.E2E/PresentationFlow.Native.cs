using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using ObjCRuntime;
using UIKit;

internal static partial class PresentationFlow
{
    // A NativeView in a private window that always stays hidden, so UIKit starts real presentations.
    sealed class HiddenHost : IDisposable
    {
        internal UIWindow Window
        {
            get;
        }
        internal UIViewController Controller
        {
            get;
        }
        internal NativeView View
        {
            get;
        }

        internal HiddenHost(IProgram program, float width, float height, bool regular = false)
        {
#pragma warning disable CA1422 // This headless executable has no UIWindowScene; the window always stays hidden.
            Window = new UIWindow(new CGRect(0, 0, width, height));
#pragma warning restore CA1422
            Controller = new UIViewController();
            Window.Hidden = true;
            Window.RootViewController = Controller;
            Controller.LoadViewIfNeeded();
            if (Controller.View!.Superview is null)
                Window.AddSubview(Controller.View);
            if (regular)
            {
                if (!OperatingSystem.IsIOSVersionAtLeast(17))
                    throw new PlatformNotSupportedException("The regular-width check requires trait overrides.");
                Controller.TraitOverrides.HorizontalSizeClass = UIUserInterfaceSizeClass.Regular;
            }
            View = new NativeView(program) { Frame = new CGRect(0, 0, width, height - 93) };
            Controller.View.AddSubview(View);
            if (regular && OperatingSystem.IsIOSVersionAtLeast(17))
            {
                // This harness has no update cycle to propagate traits; apply them before the first frame.
                Controller.UpdateTraitsIfNeeded();
                View.UpdateTraitsIfNeeded();
            }
            View.Render();
        }

        internal UIViewController? Presented => Controller.PresentedViewController;

        public void Dispose()
        {
            View.Dispose();
            Controller.Dispose();
            Window.Dispose();
        }
    }

    static NativeView Detached(IProgram program)
    {
        var view = new NativeView(program) { Frame = new CGRect(0, 0, 393, 759) };
        view.Render();
        return view;
    }

    // A detached host keeps the declared controller's view in its hidden overlay.
    static UIAlertController Alert(UIView view, string key)
    {
        PumpUntil(() => All(view).Any(child => child.AccessibilityIdentifier == key && child.Subviews.Length == 1),
            "Declared presentation owns a native controller: " + key);
        var overlay = All(view).Single(child => child.AccessibilityIdentifier == key);
        Require(overlay.Hidden, "Detached presentation overlay stays hidden: " + key);
        return (UIAlertController)overlay.Subviews[0].NextResponder!;
    }

    // UIKit offers no public way to choose an alert action without touch. Invoke the action's own
    // handler block, the block UIKit calls for a tap; UIKit's own dismissal is not part of this path.
    static unsafe void Fire(UIAlertAction action)
    {
        using var key = new NSString("handler");
        var block = action.ValueForKey(key) ?? throw new InvalidOperationException("Alert action has no handler.");
        var invoke = *(IntPtr*)(block.Handle + 16);
        ((delegate* unmanaged<IntPtr, IntPtr, void>)invoke)(block.Handle, action.Handle);
    }

    // Requests the configuration through the interaction delegate, then runs its action provider.
    static unsafe UIMenu? Menu(UIView view)
    {
        var interaction = view.Interactions.OfType<UIContextMenuInteraction>().Single();
        var configuration = interaction.Delegate!.GetConfigurationForMenu(interaction, new CGPoint(8, 8));
        if (configuration is null)
            return null;
        using var key = new NSString("actionProvider");
        var provider = configuration.ValueForKey(key) ?? throw new InvalidOperationException("Menu has no provider.");
        using var suggested = NSArray.FromNSObjects();
        var invoke = *(IntPtr*)(provider.Handle + 16);
        var menu = ((delegate* unmanaged<IntPtr, IntPtr, IntPtr>)invoke)(provider.Handle, suggested.Handle);
        return Runtime.GetNSObject<UIMenu>(menu);
    }

    static void Perform(UIMenuElement element)
    {
        // UIControl dispatches the real UIAction handler without presenting a menu or synthesizing input.
        using var sender = new UIButton();
        sender.SendAction((UIAction)element);
    }

    static void Press(UIView view, string key) =>
        Find<UIButton>(view, key).SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);

    static void Capture(UIView view, string output, string state)
    {
        view.LayoutIfNeeded();
        Require(view.Bounds.Width >= 1 && view.Bounds.Height >= 1, "Captured view has native layout: " + state);
        using var format = new UIGraphicsImageRendererFormat { Scale = 2, Opaque = true };
        using var renderer = new UIGraphicsImageRenderer(view.Bounds.Size, format);
        using var image = renderer.CreateImage(context => view.Layer.RenderInContext(context.CGContext));
        using var png = image.AsPNG() ?? throw new InvalidOperationException("PNG encoding failed.");
        File.WriteAllBytes(Path.Combine(output, $"presentation-{state}.png"), png.ToArray());
    }

    static void Scroll(UIView view, string key, float offset)
    {
        view.LayoutIfNeeded();
        var scroll = Find<UIScrollView>(view, key);
        scroll.SetContentOffset(new CGPoint(0, Math.Min(offset, Math.Max(0, scroll.ContentSize.Height - scroll.Bounds.Height))), false);
        view.LayoutIfNeeded();
    }

    static string Describe(UIAlertController alert) => string.Join(",", alert.Actions.Select(action =>
        $"\"{action.Title}:{action.Style}:{(action.Enabled ? "enabled" : "disabled")}\""));

    static IEnumerable<UIView> All(UIView root) => root.Subviews.SelectMany(child => new[] { child }.Concat(All(child)));
    static T Find<T>(UIView view, string key) where T : UIView => All(view).OfType<T>().Single(child => child.AccessibilityIdentifier == key);
    static void Pump() => NSRunLoop.Current.RunUntil(NSDate.FromTimeIntervalSinceNow(.05));
    static void PumpUntil(Func<bool> done, string description)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            Pump();
        Require(done(), description);
    }
    static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
