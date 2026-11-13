using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class GalleryLiveFlow
{
    internal static async Task Run(UIView parent, CGRect frame, string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        using var view = new NativeView(program) { Frame = frame };
        parent.AddSubview(view);
        view.Render();
        await Task.Delay(100);
        Tap(view, "open-alma");
        view.Render();
        var original = program.Model.Alma.Index;
        await Task.Delay(2650);
        Require(program.Model.Alma.Index != original, "A composed child timer wakes the gallery without polling");
        Tap(view, "alma-playback");
        view.Render();
        var orbit = Find<UIView>(view, "nutrients-orbit");
        Tap(view, "alma-next");
        view.Render();
        var cards = All(orbit).Where(child => child.GetType().Name == "CarouselCard").ToArray();
        Require(cards.Length == 8 && cards.Any(card => card.Layer.AnimationForKey("kumiko-orbit-position") is CAKeyFrameAnimation),
            "Native compositor owns bounded circular-path keyframes");
        var focused = cards.Single(card => card.AccessibilityLabel == orbit.AccessibilityLabel);
        await Task.Delay(180);
        var shown = focused.Layer.PresentationLayer ?? throw new InvalidOperationException("Missing orbit presentation layer.");
        Require(Math.Abs(shown.Position.X - focused.Layer.Position.X) > 1 && Math.Abs(shown.Transform.M12) > .001,
            "Nutrient cards move and rotate between positions");
        Capture(view, output, "alma-in-flight", presentation: true);
        var paused = program.Model.Alma.Index;
        await Task.Delay(2700);
        Require(program.Model.Alma.Index == paused, "Pause removes the native carousel timer");
        Capture(view, output, "alma-live");
        await CarouselSwipeFlow.Run(view, program, output);
        Tap(view, "alma-back");
        view.Render();
        Capture(view, output, "gallery-live");
        Require(Has(view, "open-capwords") && Has(view, "open-alma"), "Both example cards remain available after live motion");
        view.RemoveFromSuperview();
        File.WriteAllText(Path.Combine(output, "gallery-live-report.json"),
            """{"passed":true,"childTimerWake":true,"circularCompositor":true,"rotation":true,"pause":true,"galleryReturn":true} """);
    }
}
