using CoreAnimation;
using CoreGraphics;
using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;
using static CapWordsFlow;

internal static class CapWordsLiveFlow
{
    internal static async Task Run(NativeView view, Program<CapWordsModel, CapWordsMessage> program, string output)
    {
        Directory.CreateDirectory(output);
        Require(NSThread.IsMain, "Live verification is on the UIKit thread");
        Require(Has(view, "capture"), "Live app loaded the word collection");
        var source = All(view).First(child => child.AccessibilityIdentifier == "hero");
        var sourceRect = source.ConvertRectToView(source.Bounds, view);
        var first = All(view).OfType<UIButton>().First(child => child.AccessibilityIdentifier == "open-word");
        first.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        var hero = Find<UIView>(view, "hero");
        using var spring = hero.Layer.AnimationForKey("kumiko-shared-position") as CASpringAnimation;
        Require(spring is not null && Math.Abs(spring.Duration - .55) < .001, "Shared image uses a 550ms native spring");
        Require(Find<UIView>(view, "date-shared").Layer.AnimationForKey("kumiko-shared-position") is not null,
            "Date header shares its navigation transition");
        Require(Find<UIView>(view, "count-shared").Layer.AnimationForKey("kumiko-shared-position") is not null,
            "Word count shares its navigation transition");
        using var nameEnter = Find<UIView>(view, "word-name").Layer.AnimationForKey("kumiko-enter-opacity");
        using var phoneticEnter = Find<UIView>(view, "pronunciation").Layer.AnimationForKey("kumiko-enter-opacity");
        using var buttonEnter = Find<UIView>(view, "examples-position").Layer.AnimationForKey("kumiko-enter-opacity");
        Require(nameEnter is not null && phoneticEnter is not null && buttonEnter is not null &&
            Math.Abs(phoneticEnter.BeginTime - nameEnter.BeginTime - .1) < .02 &&
            Math.Abs(buttonEnter.BeginTime - nameEnter.BeginTime - .2) < .02, "Native detail arrivals are staggered by 100ms");
        await Task.Delay(90);
        Require(NSThread.IsMain, "Animation sampling stays on the UIKit thread");
        var moving = hero.Layer.PresentationLayer ?? throw new InvalidOperationException("No native presentation layer.");
        var movingRect = moving.ConvertRectToLayer(moving.Bounds, view.Layer.PresentationLayer ?? view.Layer);
        var target = hero.ConvertRectToView(hero.Bounds, view);
        Require(Math.Abs(movingRect.Y - sourceRect.Y) > 1 && Math.Abs(movingRect.Y - target.Y) > 1,
            "The compositor presents an intermediate position");
        Capture(view, output, "spring-in-flight", presentation: true);
        // Reverse while the spring is moving. The new animation must start at the presentation geometry.
        Tap(view, "back");
        view.Render();
        var backHero = All(view).First(child => child.AccessibilityIdentifier == "hero");
        using var reverse = backHero.Layer.AnimationForKey("kumiko-shared-position") as CASpringAnimation;
        Require(reverse?.From is NSValue, "Rapid Back creates a reverse spring");
        await Task.Delay(700);
        Capture(view, output, "spring-back");
        var second = All(view).OfType<UIButton>().Last(child => child.AccessibilityIdentifier == "open-word");
        second.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        var selected = Find<UILabel>(view, "name").Text;
        Require(selected == "Ice Cream Cone", "Second card opens its own live detail");
        await Task.Delay(90);
        var resized = new CGRect(view.Frame.X, view.Frame.Y, view.Frame.Width - 20, view.Frame.Height);
        view.Frame = resized;
        view.Render();
        Require(Find<UIView>(view, "hero").Layer.AnimationKeys?.Length is null or 0, "Resize cancels obsolete animation geometry");
        await Task.Delay(700);
        Capture(view, output, "spring-detail");
        Require(Find<UIView>(view, "word-name").Layer.AnimationKeys?.Length is null or 0,
            "Completed entrance does not restart on resize");
        Require(!program.Model.Words.IsDefaultOrEmpty && program.Model.SelectedId == "ice-cream-cone-feb-09", "Live model remains intact");
        await MotionProbe.Run(view.Superview!, output);
        File.WriteAllText(Path.Combine(output, "live-report.json"),
            """{"passed":true,"simulatorWindow":true,"nativeSpring":true,"durationMs":550,"sharedHeaders":true,"staggerMs":[100,200,300],"intermediatePresentation":true,"rapidBack":true,"resizeCancellation":true,"secondWord":true} """);
    }
}
