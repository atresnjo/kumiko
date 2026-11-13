using CoreAnimation;
using CoreGraphics;
using Kumiko.iOS;
using ObjCRuntime;
using UIKit;
using static CapWordsFlow;

internal static class SlackLiveFlow
{
    internal static async Task Run(UIView parent, CGRect frame, string output)
    {
        using var program = AnimationGalleryApp.CreateProgram();
        using var view = new NativeView(program) { Frame = frame };
        Exception? callbackError = null;
        view.UnhandledException += error => callbackError = error;
        parent.AddSubview(view);
        view.Render();
        Tap(view, "open-slack");
        view.Render();
        await Task.Delay(100);
        var deck = Find<UIView>(view, "slack-deck");
        var card = Find<UIView>(view, "channel-5");
        var next = Find<UIView>(view, "channel-4");
        var pan = deck.GestureRecognizers!.OfType<UIPanGestureRecognizer>().Single();
        using var input = new DrivenPan();
        input.Speed = new CGPoint(0, 500);
        Require(!pan.ShouldBegin!(input), "Vertical card scrolling is not captured as a horizontal deck gesture");
        input.Speed = new CGPoint(500, 0);
        Require(pan.ShouldBegin!(input), "Horizontal movement begins the native deck gesture");
        var messages = All(card).OfType<UIScrollView>().Single();
        Require(pan.ShouldRecognizeSimultaneously!(pan, messages.PanGestureRecognizer), "The deck permits its native message scroller to recognize simultaneously");
        foreach (var key in new[] { "slack-back", "slack-read", "slack-unread" })
        {
            var button = Find<UIButton>(view, key);
            var point = button.ConvertPointToView(new CGPoint(button.Bounds.Width / 2, button.Bounds.Height / 2), view);
            var hit = view.HitTest(point, null);
            Require(hit is not null && (hit == button || hit.IsDescendantOfView(button)), $"The swipe deck preserves {key} native hit testing");
        }
        messages.ContentOffset = new CGPoint(0, 140);
        Require(messages.ContentOffset.Y == 140 && messages.ContentSize.Height > messages.Bounds.Height,
            "Each channel keeps its own native vertical message scroll");
        messages.ContentOffset = default;
        var center = card.Layer.Position;
        input.Start = new CGPoint(100, 100);
        Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
        Send(deck, input, UIGestureRecognizerState.Changed, 130, 35);
        Require(Math.Abs(card.Layer.Position.X - center.X - 130) < 1 && Math.Abs(card.Layer.Position.Y - center.Y - 35) < 1 && card.Layer.Transform.M12 > 0,
            "Top-anchored dragging tracks both axes and rotates with the finger");
        Require(next.Layer.Transform.M11 > .99, "The underlying card grows into place as the threshold is crossed");
        Capture(view, output, "slack-drag-right");
        Send(deck, input, UIGestureRecognizerState.Cancelled, 130, 35);
        Require(program.Model.Slack.Index == 4, "Cancelled swipes preserve the selected card and count");
        await Task.Delay(70);
        var shown = card.Layer.PresentationLayer!.Position;
        input.Start = new CGPoint(100, 10000);
        Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
        Require(Math.Abs(card.Layer.Position.X - shown.X) < 12 && Math.Abs(card.Layer.Position.Y - shown.Y) < 12,
            "A new finger interrupts the return spring at its presentation position");
        Send(deck, input, UIGestureRecognizerState.Changed, 40, 15);
        Require(card.Layer.Transform.M12 < 0, "Grabbing below center reverses the card's tilt");
        Send(deck, input, UIGestureRecognizerState.Cancelled, 40, 15);
        await Task.Delay(700);
        input.Start = new CGPoint(100, 100);
        Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
        Send(deck, input, UIGestureRecognizerState.Ended, 140, 40, 500, 600);
        view.Render();
        await Task.Delay(25);
        Require(program.Model.Slack.Index == 3 && program.Model.Slack.Dispositions[4] == "read", "Right swipe commits the exact channel as Read");
        var flip = All(Find<UIView>(view, "slack-counter")).OfType<UILabel>().First(label => label.Text == "4");
        Require(flip.Layer.AnimationForKey("kumiko-counter-flip") is CASpringAnimation, "The counter uses its direction-aware native flip spring");
        await Task.Delay(75);
        shown = card.Layer.PresentationLayer!.Position;
        Require(shown.X > center.X + 140 && shown.Y > center.Y + 40, "Dismissal carries horizontal motion and vertical release momentum");
        Capture(view, output, "slack-fling", presentation: true);
        Tap(view, "slack-undo");
        view.Render();
        await Task.Delay(10);
        var undo = card.Layer.AnimationForKey("kumiko-deck-position") as CAKeyFrameAnimation;
        Require(undo?.Values?.FirstOrDefault() is Foundation.NSValue start && Math.Abs(start.CGPointValue.X - shown.X) < 35,
            "Undo reverses an in-flight dismissal from its displayed position");
        Require(program.Model.Slack.Index == 4 && program.Model.Slack.Dispositions[4] == "", "Undo restores the dismissed channel during motion");
        await Task.Delay(720);
        Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
        Send(deck, input, UIGestureRecognizerState.Ended, -140, -20, -500, -150);
        view.Render();
        await Task.Delay(350);
        Require(program.Model.Slack.Index == 3 && program.Model.Slack.Dispositions[4] == "unread", "Left swipe commits Keep Unread without conflating status with deck progress");
        Send(deck, input, UIGestureRecognizerState.Began, 0, 0);
        Send(deck, input, UIGestureRecognizerState.Changed, 45, 10);
        Send(deck, input, UIGestureRecognizerState.Ended, 45, 10, 1500, 0);
        view.Render();
        Require(program.Model.Slack.Index == 3, "A below-threshold release returns without consuming a card");
        await Task.Delay(50);
        view.Frame = new CGRect(frame.X, frame.Y, frame.Width - 20, frame.Height);
        view.Render();
        Require(deck.Subviews.Where(child => child.GetType().Name == "NativeSwipeCardView").All(child => child.Layer.AnimationKeys?.Length is null or 0),
            "Resize cancels obsolete deck motion while preserving the bounded card set");
        Capture(view, output, "slack-live");
        Tap(view, "slack-back");
        view.Render();
        Require(Has(view, "open-slack") && Has(view, "open-capwords") && Has(view, "open-alma"), "Live gestures return to the three-study gallery");
        Require(callbackError is null, "Deck gesture and animation callbacks remain guarded");
        view.RemoveFromSuperview();
        File.WriteAllText(Path.Combine(output, "slack-live-report.json"),
            """{"passed":true,"nativePan":true,"scrollArbitration":true,"twoAxisTracking":true,"anchorRotation":true,"stackProgress":true,"cancel":true,"springInterruption":true,"rightRead":true,"leftUnread":true,"verticalMomentum":true,"counterFlip":true,"inFlightUndo":true,"shortReturn":true,"resize":true,"gallery":true} """);
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
            get => Phase; set => Phase = value;
        }
        public override CGPoint TranslationInView(UIView? view) => Offset;
        public override CGPoint VelocityInView(UIView? view) => Speed;
        public override CGPoint LocationInView(UIView? view) => Start;
    }
}
