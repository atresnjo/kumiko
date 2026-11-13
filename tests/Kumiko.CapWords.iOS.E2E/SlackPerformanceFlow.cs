using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using ObjCRuntime;
using UIKit;
using static CapWordsFlow;

internal static class SlackPerformanceFlow
{
    // Failure signals: long callback gaps during model rebuilds, expensive per-finger
    // callbacks, presentation pauses, lost commits/Undo, or unbounded native view growth.
    internal static async Task Run(UIView parent, CGRect frame, string output)
    {
        var paced = Environment.GetEnvironmentVariable("KUMIKO_SLACK_PACED") == "1";
        using var program = AnimationGalleryApp.CreateProgram();
        using var view = new NativeView(program) { Frame = frame };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        parent.AddSubview(view);
        view.Render();
        Tap(view, "open-slack");
        view.Render();
        await Task.Delay(2000);
        var deck = Find<UIView>(view, "slack-deck");
        var initialCount = All(view).Count();
        using var input = new DrivenPan();
        using var probe = new FramePacingProbe { Card = Find<UIView>(view, "channel-5") };
        await Task.Delay(1000);
        probe.Phase = "idle-baseline";
        await Task.Delay(3000);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            var direction = cycle % 2 == 0 ? 1 : -1;
            probe.Card = Find<UIView>(view, "channel-5");
            input.Start = new CGPoint(100, cycle % 2 == 0 ? 100 : 10000);
            probe.Phase = "continuous-drag";
            Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
            var started = CAAnimation.CurrentMediaTime();
            probe.Drive = now =>
            {
                var t = now - started;
                Send(deck, input, UIGestureRecognizerState.Changed, Math.Sin(t * Math.PI) * 145, Math.Sin(t * Math.PI * 2) * 25);
            };
            await Task.Delay(2000);
            probe.Drive = null;
            probe.Phase = "cancel-return";
            Send(deck, input, UIGestureRecognizerState.Changed, 65, 20);
            Send(deck, input, UIGestureRecognizerState.Cancelled, 65, 20);
            await Task.Delay(700);
            probe.Phase = paced ? "swipe-approach" : "swipe-commit";
            Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
            if (paced)
            {
                var approachStarted = CAAnimation.CurrentMediaTime();
                probe.Drive = now => Send(deck, input, UIGestureRecognizerState.Changed,
                    direction * 145 * Math.Clamp((now - approachStarted) / .25, 0, 1), 35);
                await Task.Delay(250);
                probe.Drive = null;
            }
            probe.Phase = "swipe-commit";
            Send(deck, input, UIGestureRecognizerState.Ended, direction * 145, 35, direction * 600, 400);
            await Task.Delay(450);
            Require(program.Model.Slack.Index == 3, "Performance swipe commits exactly one card");
            probe.Phase = "undo";
            Tap(view, "slack-undo");
            await Task.Delay(850);
            Require(program.Model.Slack.Index == 4 && !program.Model.Slack.Busy, "Performance Undo restores the card and settles");
            probe.Phase = "button-commit";
            Tap(view, direction > 0 ? "slack-read" : "slack-unread");
            await Task.Delay(600);
            Require(program.Model.Slack.Index == 3, "Performance footer action commits exactly one card");
            probe.Phase = "undo";
            Tap(view, "slack-undo");
            await Task.Delay(850);
        }
        probe.Phase = "idle-after-interactions";
        await Task.Delay(2000);
        Require(program.Model.Slack.Index == 4 && All(view).Count() == initialCount, "Performance repetitions keep native view count bounded");
        Require(callbackError is null, "No errors occurred during the frame-pacing run");
        probe.Save(output, (int)view.Window!.WindowScene!.Screen.MaximumFramesPerSecond, initialCount);
        Capture(view, output, "slack-performance-final");
        view.RemoveFromSuperview();
    }

    static void Send(UIView deck, DrivenPan input, UIGestureRecognizerState state, double x, double y, double vx = 0, double vy = 0)
    {
        input.Phase = state;
        input.Offset = new CGPoint(x, y);
        input.Speed = new CGPoint(vx, vy);
        deck.PerformSelector(new Selector("kumikoHandleDeckPan:"), input);
    }

    sealed class DrivenPan : UIPanGestureRecognizer
    {
        internal UIGestureRecognizerState Phase;
        internal CGPoint Offset;
        internal CGPoint Speed;
        internal CGPoint Start;
        public override UIGestureRecognizerState State
        {
            get => Phase;
            set => Phase = value;
        }
        public override CGPoint TranslationInView(UIView? view) => Offset;
        public override CGPoint VelocityInView(UIView? view) => Speed;
        public override CGPoint LocationInView(UIView? view) => Start;
    }
}
