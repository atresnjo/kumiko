using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using UIKit;
using static CapWordsFlow;

internal static class OpalPerformanceFlow
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
        await Task.Delay(1500);
        var count = All(view).Count();
        using var contact = new OpalContact();
        using var probe = new FramePacingProbe();
        await Task.Delay(500);
        probe.Phase = "idle-compact";
        await Task.Delay(2000);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            probe.Phase = "presentation-toggle";
            Tap(view, "opal-value");
            await Task.Delay(350);
            Tap(view, "opal-value");
            await Task.Delay(100);
            Tap(view, "opal-value");
            await Task.Delay(650);
            Require(program.Model.Opal.Expanded, "Pacing reversal leaves the expanded slider available");
            var slider = Find<UIView>(view, "opal-slider");
            var width = slider.Bounds.Width;
            probe.Card = slider.Subviews.Single();
            probe.Phase = "continuous-drag";
            OpalContact.Send(slider, contact, UIGestureRecognizerState.Began, width / 2);
            var start = CAAnimation.CurrentMediaTime();
            probe.Drive = now => OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed,
                width * (.5 + .48 * Math.Sin((now - start) * Math.PI * 2)));
            await Task.Delay(2000);
            probe.Phase = "edge-stretch";
            start = CAAnimation.CurrentMediaTime();
            probe.Drive = now => OpalContact.Send(slider, contact, UIGestureRecognizerState.Changed,
                cycle % 2 == 0 ? width + 50 + 50 * Math.Sin((now - start) * Math.PI * 2) : -50 - 50 * Math.Sin((now - start) * Math.PI * 2));
            await Task.Delay(1000);
            probe.Drive = null;
            probe.Phase = "release-return";
            OpalContact.Send(slider, contact, cycle % 2 == 0 ? UIGestureRecognizerState.Ended : UIGestureRecognizerState.Cancelled,
                cycle % 2 == 0 ? width + 80 : -80);
            await Task.Delay(1000);
            Require(slider.Subviews.Single().Layer.Transform.IsIdentity, "Pacing release/cancel settles the native rubber surface");
            probe.Phase = "presentation-toggle";
            Tap(view, "opal-value");
            await Task.Delay(700);
        }
        probe.Phase = "idle-after-interactions";
        await Task.Delay(2000);
        Require(All(view).Count() == count, "Repeated motion retains a bounded native view set");
        Require(error is null, "Pacing callbacks remain guarded");
        probe.Save(output, (int)view.Window!.WindowScene!.Screen.MaximumFramesPerSecond, count,
            "opal", "60 Hz contact-driven duration sweep, alternating edge overdrag, release/cancel and interrupted presentation toggles");
        Capture(view, output, "opal-performance-final");
        view.RemoveFromSuperview();
    }
}
