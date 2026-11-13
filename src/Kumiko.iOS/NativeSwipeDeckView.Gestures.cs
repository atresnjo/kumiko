using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeSwipeDeckView
{
    [Export("kumikoHandleDeckPan:")]
    void HandlePan(UIPanGestureRecognizer gesture) => guard(() =>
    {
        if (content is not { Preview: false, Disabled: false, Index: >= 0 } || pendingCommit)
            return;
        var card = cards[content.Index];
        switch (gesture.State)
        {
            case UIGestureRecognizerState.Began:
                if (CAAnimation.CurrentMediaTime() < lockedUntil)
                    return;
                dragOrigin = Presented(card).Offset;
                var location = gesture.LocationInView(Window);
                anchorSign = location.Y > (Window?.Bounds.Height ?? Bounds.Height) / 2 ? -1 : 1;
                dragging = true;
                crossed = false;
                foreach (var item in cards)
                    Clear(item.Layer);
                UpdateDrag(gesture);
                break;
            case UIGestureRecognizerState.Changed when dragging:
                UpdateDrag(gesture);
                break;
            case UIGestureRecognizerState.Ended when dragging:
                UpdateDrag(gesture);
                releaseVelocity = gesture.VelocityInView(this);
                dragging = false;
                if (Math.Abs(dragOffset.X) > Threshold)
                {
                    gestureCommit = true;
                    pendingCommit = true;
                    select((dragOffset.X > 0 ? "r:" : "l:") + card.Id);
                }
                else
                    ReturnToStack();
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed when dragging:
                dragging = false;
                releaseVelocity = default;
                ReturnToStack();
                break;
        }
    });

    void UpdateDrag(UIPanGestureRecognizer gesture)
    {
        var delta = gesture.TranslationInView(this);
        dragOffset = new CGPoint(dragOrigin.X + delta.X, dragOrigin.Y + delta.Y);
        var threshold = Math.Abs(dragOffset.X) > Threshold;
        if (threshold && !crossed && Window is { Hidden: false })
        {
            using var feedback = OperatingSystem.IsIOSVersionAtLeast(17, 5) || OperatingSystem.IsMacCatalystVersionAtLeast(17, 5)
                ? UIImpactFeedbackGenerator.GetFeedbackGenerator(UIImpactFeedbackStyle.Light, this)
                : new UIImpactFeedbackGenerator(UIImpactFeedbackStyle.Light);
            feedback.ImpactOccurred();
        }
        crossed = threshold;
        ApplyDrag();
    }

    void ApplyDrag()
    {
        if (content is null)
            return;
        var progress = Math.Clamp(Math.Abs(dragOffset.X) / Threshold, 0, 1);
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        for (var i = 0; i < cards.Length; i++)
        {
            var from = StackPose(i, content.Index);
            var to = StackPose(i, content.Index - 1);
            var pose = i == content.Index ? new Pose(dragOffset, dragOffset.X / Threshold * anchorSign * 4 * Math.PI / 180, 1, 1) :
                new Pose(new CGPoint(0, from.Offset.Y + (to.Offset.Y - from.Offset.Y) * progress), 0,
                    from.Scale + (to.Scale - from.Scale) * progress, from.Opacity);
            Apply(cards[i], pose);
            cards[i].Feedback.Update(i == content.Index ? progress : 0, dragOffset.X > 0);
        }
        CATransaction.Commit();
    }

    void ReturnToStack()
    {
        for (var i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var from = Presented(card, model: true);
            var to = StackPose(i, content!.Index);
            targets[card.Id] = to;
            Apply(card, to);
            if (CanAnimate)
                Animate(card, from, to, returning: true, duration: .65);
            card.Feedback.Hide(CanAnimate);
        }
        releaseVelocity = default;
    }
}
