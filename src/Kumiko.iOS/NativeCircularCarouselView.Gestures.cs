using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeCircularCarouselView
{
    bool dragging;
    double dragOrigin;
    double dragPosition;
    double DragDistance => Math.Max(80, Bounds.Height / 2 * AngleStep);

    // The full-canvas artwork sits above the page, but only its central band captures touches.
    // Header and footer controls retain their native hit targets.
    public override bool PointInside(CGPoint point, UIEvent? uievent) =>
        content is { Interactive: true, Preview: false } && point.Y >= Bounds.Height * .26 &&
        point.Y <= Bounds.Height * .81 && base.PointInside(point, uievent);

    [Export("kumikoHandlePan:")]
    void HandlePan(UIPanGestureRecognizer gesture) => guard(() =>
    {
        if (content is not { Interactive: true, Preview: false })
            return;
        switch (gesture.State)
        {
            case UIGestureRecognizerState.Began:
                dragOrigin = PresentedPosition();
                dragPosition = dragOrigin;
                dragging = true;
                started = 0;
                foreach (var card in cards)
                {
                    Clear(card.Layer);
                    Clear(card.Decoration.Layer);
                }
                FollowFinger(gesture);
                break;
            case UIGestureRecognizerState.Changed when dragging:
                FollowFinger(gesture);
                break;
            case UIGestureRecognizerState.Ended when dragging:
                FollowFinger(gesture);
                Snap(gesture.VelocityInView(this).X, cancelled: false);
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed when dragging:
                Snap(0, cancelled: true);
                break;
        }
    });

    double PresentedPosition()
    {
        var expected = CurrentPosition();
        var index = ((int)Math.Round(expected) % cards.Count + cards.Count) % cards.Count;
        if (cards[index].Layer.PresentationLayer is not { } shown || started <= 0)
            return expected;
        var angle = Math.Atan2(shown.Position.X - Bounds.Width / 2, Bounds.Height - shown.Position.Y);
        var position = index - angle / AngleStep;
        return expected + Relative(position, expected, cards.Count);
    }

    void FollowFinger(UIPanGestureRecognizer gesture)
    {
        dragPosition = dragOrigin - Math.Clamp(gesture.TranslationInView(this).X / DragDistance, -2, 2);
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        for (var i = 0; i < cards.Count; i++)
            Apply(cards[i], i, dragPosition);
        foreach (var index in Enumerable.Range(0, cards.Count).OrderByDescending(i => Math.Abs(Relative(i, dragPosition, cards.Count))))
            BringSubviewToFront(cards[index]);
        CATransaction.Commit();
    }

    void Snap(double velocity, bool cancelled)
    {
        dragging = false;
        fromPosition = dragPosition;
        releaseVelocity = cancelled ? 0 : Math.Clamp(-velocity / DragDistance, -4, 4);
        targetPosition = cancelled ? dragOrigin + Relative(content!.SelectedIndex, dragOrigin, cards.Count) :
            Math.Round(dragPosition + releaseVelocity * .18);
        started = CAAnimation.CurrentMediaTime();
        Arrange(animate: CanAnimate);
        if (!cancelled)
        {
            var index = ((int)targetPosition % cards.Count + cards.Count) % cards.Count;
            if (index != content!.SelectedIndex && Window is { Hidden: false })
            {
                using var feedback = new UISelectionFeedbackGenerator();
                feedback.SelectionChanged();
            }
            select(content!.Items[index].Id);
        }
    }
}
