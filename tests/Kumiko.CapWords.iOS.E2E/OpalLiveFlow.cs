using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class OpalLiveFlow
{
    internal static async Task Run(UIView parent, CGRect frame, string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        program.Send(new GalleryMessage.Open(GalleryScreen.Opal));
        using var view = new NativeView(program) { Frame = frame };
        Exception? error = null;
        view.UnhandledException += failure => error = failure;
        parent.AddSubview(view);
        view.Render();
        await Task.Delay(300);
        var value = Find<UIButton>(view, "opal-value");
        value.Highlighted = true;
        await Task.Delay(120);
        Require(value.Layer.Transform.M11 < .96, "Duration press has the reference's 95% scale feedback");
        value.Highlighted = false;
        value.SendActionForControlEvents(UIControlEvent.TouchDown);
        view.Render();
        Require(program.Model.Opal.Expanded, "Duration contact expands on press-in before finger release");
        value.SendActionForControlEvents(UIControlEvent.PrimaryActionTriggered);
        view.Render();
        Require(program.Model.Opal.Expanded, "Releasing the value button does not toggle a second time");
        CATransaction.Flush();
        await Task.Delay(80);
        var moving = Find<UIView>(view, "opal-value-pose").Subviews.Single();
        var shown = moving.Layer.PresentationLayer!.Transform.M41;
        Require(shown < -5 && shown > -46, "Value moves left through a native spring intermediate");
        Capture(view, output, "opal-expanding", presentation: true);
        Tap(view, "opal-value");
        view.Render();
        using var reversed = moving.Layer.AnimationForKey("kumiko-presentation-transform") as CASpringAnimation;
        Require(reversed?.From is Foundation.NSValue start && Math.Abs(start.CATransform3DValue.M41 - shown) < 7,
            "Rapid presentation reversal starts from the shown pose");
        await Task.Delay(700);
        Tap(view, "opal-value");
        view.Render();
        await Task.Delay(700);
        OpalFlow.Reach(view, "opal-value");
        OpalFlow.Reach(view, "opal-slider");
        foreach (var key in new[] { "opal-back", "opal-start", "opal-home", "opal-blocks", "opal-screen-time", "opal-leaderboard", "opal-account" })
            OpalFlow.Reach(view, key);
        var slider = Find<UIView>(view, "opal-slider");
        Require(slider.GestureRecognizers?.Single() is UILongPressGestureRecognizer { MinimumPressDuration: 0 },
            "Native immediate-contact recognizer handles stationary contact and dragging");
        var rubber = Find<UIView>(slider, "kumiko-rubber-stretch");
        using var contact = new OpalContact();
        var width = slider.Bounds.Width;
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Began, width * .3);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed, width + 90);
        view.Render();
        Require(program.Model.Opal.Index == 35 && rubber.Layer.Transform.M11 > 1.2 && rubber.Layer.Transform.M22 < .94,
            "Right overdrag selects 24h, stretches horizontally and compresses vertically");
        Require(rubber.Layer.AnchorPoint.X == 0 && rubber.Layer.AnimationKeys?.Length is null or 0,
            "Edge dragging follows the finger directly about the opposite edge");
        Capture(view, output, "opal-stretch-right");
        await Task.Delay(80);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Ended, width + 90);
        Require(rubber.Layer.AnimationForKey("kumiko-rubber-transform") is CASpringAnimation, "Release starts a native return spring");
        await Task.Delay(80);
        var returning = rubber.Layer.PresentationLayer!.Transform;
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Began, width * .5);
        using var interrupted = rubber.Layer.AnimationForKey("kumiko-rubber-transform") as CASpringAnimation;
        Require(interrupted?.From is Foundation.NSValue origin && Math.Abs(origin.CATransform3DValue.M11 - returning.M11) < .04,
            "A new contact interrupts the return at its presentation scale");
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed, -90);
        view.Render();
        Require(program.Model.Opal.Index == 1 && rubber.Layer.AnchorPoint.X == 1 && rubber.Layer.Transform.M11 > 1.2,
            "Left overdrag anchors the right edge and clamps to five minutes");
        Capture(view, output, "opal-stretch-left");
        await Task.Delay(80);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Cancelled, -90);
        view.Render();
        await Task.Delay(1100);
        var settled = rubber.Layer.PresentationLayer?.Transform ?? rubber.Layer.Transform;
        Require(program.Model.Opal.Index == 1 && Math.Abs(settled.M11 - 1) < .001 && Math.Abs(settled.M22 - 1) < .001,
            $"Cancellation keeps the touched duration and visibly settles deformation (index={program.Model.Opal.Index}, scale={settled.M11}, {settled.M22})");
        slider.AccessibilityIncrement();
        view.Render();
        Require(program.Model.Opal.Index == 2, "Native accessibility increment uses the same model selection path");
        Capture(view, output, "opal-live-settled", presentation: true);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Began, width + 120);
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed, width + 120);
        view.Render();
        view.Frame = new CGRect(frame.X, frame.Y, 320, 600);
        view.Render();
        view.LayoutIfNeeded();
        Require(rubber.Layer.Transform.IsIdentity && rubber.Layer.AnimationKeys?.Length is null or 0, "Resize cancels obsolete deformation");
        OpalContact.Send(slider, contact, UIGestureRecognizerState.Ended, -100);
        view.Render();
        Require(program.Model.Opal.Index == 35, "Late release from the old resized gesture cannot change selection");
        OpalFlow.Reach(view, "opal-value");
        OpalFlow.Reach(view, "opal-slider");
        Require(error is null, "Live native callbacks remain guarded");
        File.WriteAllText(Path.Combine(output, "opal-live-report.json"),
            """{"passed":true,"nativeContact":true,"presentationSpring":true,"pressFeedback":true,"rapidReversal":true,"directTracking":true,"bothEdgeAnchors":true,"compression":true,"releaseSpring":true,"interruption":true,"cancellation":true,"resize":true,"accessibilityAdjustment":true,"reachableControls":true} """);
        view.RemoveFromSuperview();
        await OpalReducedMotionFlow.Run(parent, frame, output);
    }
}
