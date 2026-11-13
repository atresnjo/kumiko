using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using Kumiko.Runtime;
using ObjCRuntime;
using UIKit;
using static CapWordsFlow;

internal static class CarouselSwipeFlow
{
    // Failure cases: an overlay steals the gesture; dragging jumps to the old target;
    // release loses velocity; cancelled drags commit; wrapping grows views; auto-play fights a swipe.
    internal static async Task Run(NativeView view, Program<GalleryModel, GalleryMessage> program, string output)
    {
        var orbit = Find<UIView>(view, "nutrients-orbit");
        var center = orbit.ConvertPointToView(new CGPoint(orbit.Bounds.Width / 2, orbit.Bounds.Height * .52), view);
        var hit = view.HitTest(center, null);
        Require(hit is not null && (hit == orbit || hit.IsDescendantOfView(orbit)), "Finger hit testing reaches the carousel above the page");
        Require(orbit.GestureRecognizers?.Single() is UIPanGestureRecognizer, "A native pan recognizer owns finger tracking");
        foreach (var key in new[] { "alma-back", "alma-playback", "alma-previous", "alma-next", "alma-continue" })
        {
            var button = Find<UIButton>(view, key);
            var point = button.ConvertPointToView(new CGPoint(button.Bounds.Width / 2, button.Bounds.Height / 2), view);
            var target = view.HitTest(point, null);
            Require(target is not null && (target == button || target.IsDescendantOfView(button)), $"Swipe canvas preserves {key} hit testing");
        }
        var original = program.Model.Alma.Index;
        using var input = new DrivenPan();
        var nativePan = (UIPanGestureRecognizer)orbit.GestureRecognizers!.Single();
        input.Speed = new CGPoint(0, 500);
        Require(nativePan.ShouldBegin!(input) == false, "Vertical finger movement does not begin a carousel swipe");
        input.Speed = new CGPoint(500, 0);
        Require(nativePan.ShouldBegin!(input), "Horizontal finger movement begins a carousel swipe");
        Send(orbit, input, UIGestureRecognizerState.Began, 0);
        Send(orbit, input, UIGestureRecognizerState.Changed, -orbit.Bounds.Height * .19);
        Capture(view, output, "alma-finger-drag");
        var cards = All(orbit).Where(child => child.GetType().Name == "CarouselCard").ToArray();
        var current = cards.Single(card => card.AccessibilityLabel == AlmaApp.Items[original].Title);
        Require(current.Layer.Position.X < orbit.Bounds.Width / 2 - 20 && current.Layer.AnimationKeys?.Length is null or 0,
            "Drag geometry follows the finger directly without a competing spring");
        Send(orbit, input, UIGestureRecognizerState.Ended, -orbit.Bounds.Height * .19, -500);
        view.Render();
        Require(program.Model.Alma.Index == (original + 1) % 8 && !program.Model.Alma.Playing,
            "Release commits the next card and pauses auto-play through the app message path");
        Require(cards.Any(card => card.Layer.AnimationForKey("kumiko-orbit-position") is CAKeyFrameAnimation),
            "Release starts a native velocity-carrying spring");
        await Task.Delay(100);
        var moving = current.Layer.PresentationLayer!.Position;
        Send(orbit, input, UIGestureRecognizerState.Began, 0);
        Require(Math.Abs(current.Layer.Position.X - moving.X) < 15, "A new finger interrupts the spring at its presented location");
        Send(orbit, input, UIGestureRecognizerState.Cancelled, 0);
        await Task.Delay(2000);
        var committed = program.Model.Alma.Index;
        Send(orbit, input, UIGestureRecognizerState.Began, 0);
        Send(orbit, input, UIGestureRecognizerState.Changed, orbit.Bounds.Height * .19);
        Send(orbit, input, UIGestureRecognizerState.Cancelled, orbit.Bounds.Height * .19);
        view.Render();
        Require(program.Model.Alma.Index == committed, "Cancelled finger interaction does not change selection");
        await Task.Delay(2000);
        Capture(view, output, "alma-swipe-settled");
        Tap(view, "alma-playback");
        view.Render();
        Send(orbit, input, UIGestureRecognizerState.Began, 0);
        Send(orbit, input, UIGestureRecognizerState.Ended, -orbit.Bounds.Height * .03, -1500);
        view.Render();
        Require(!program.Model.Alma.Playing && program.Model.Alma.Index == (committed + 1) % 8,
            "A short fast flick uses release velocity to advance and pauses active auto-play");
        File.WriteAllText(Path.Combine(output, "swipe-report.json"),
            """{"passed":true,"nativePan":true,"fingerHitTesting":true,"directTracking":true,"velocitySnap":true,"interruption":true,"cancel":true,"pausesAutoPlay":true} """);
    }

    static void Send(UIView orbit, DrivenPan pan, UIGestureRecognizerState state, double translation, double velocity = 0)
    {
        pan.Phase = state;
        pan.Offset = new CGPoint(translation, 0);
        pan.Speed = new CGPoint(velocity, 0);
        orbit.PerformSelector(new Selector("kumikoHandlePan:"), pan);
    }

    sealed class DrivenPan : UIPanGestureRecognizer
    {
        internal UIGestureRecognizerState Phase;
        internal CGPoint Offset;
        internal CGPoint Speed;
        public override UIGestureRecognizerState State
        {
            get => Phase; set => Phase = value;
        }
        public override CGPoint TranslationInView(UIView? view) => Offset;
        public override CGPoint VelocityInView(UIView? view) => Speed;
    }
}
