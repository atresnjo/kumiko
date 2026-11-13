using CoreAnimation;
using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeSwipeDeckView : UIView
{
    readonly Action<Action> guard;
    readonly Action<string> select;
    readonly UIPanGestureRecognizer pan;
    NativeSwipeDeckContent? content;
    NativeSwipeCardView[] cards = [];
    readonly Dictionary<string, Pose> targets = new(StringComparer.Ordinal);
    readonly Dictionary<string, Pose> captured = new(StringComparer.Ordinal);
    CGSize arrangedSize;
    int arrangedIndex = int.MinValue;
    bool dragging;
    bool pendingCommit;
    bool gestureCommit;
    CGPoint dragOrigin;
    CGPoint dragOffset;
    CGPoint releaseVelocity;
    double anchorSign = 1;
    bool crossed;
    double lockedUntil;
    double Threshold => Math.Max(1, Window?.Bounds.Width / 4 ?? Bounds.Width / 4);
    bool CanAnimate => content is { Preview: false, ReduceMotion: false } && Window is { Hidden: false } &&
        !UIAccessibility.IsReduceMotionEnabled;

    internal NativeSwipeDeckView(Action<Action> guard, Action<string> select)
    {
        this.guard = guard;
        this.select = select;
        pan = new UIPanGestureRecognizer(this, new ObjCRuntime.Selector("kumikoHandleDeckPan:"));
        pan.MaximumNumberOfTouches = 1;
        pan.ShouldBegin = recognizer =>
        {
            var allowed = false;
            guard(() =>
            {
                var velocity = ((UIPanGestureRecognizer)recognizer).VelocityInView(this);
                allowed = content is { Preview: false, Disabled: false, Index: >= 0 } && !pendingCommit &&
                    CAAnimation.CurrentMediaTime() >= lockedUntil && Math.Abs(velocity.X) > Math.Abs(velocity.Y);
            });
            return allowed;
        };
        pan.ShouldRecognizeSimultaneously = (_, other) => other.View is UIScrollView;
        AddGestureRecognizer(pan);
    }

    internal void Configure(NativeSwipeDeckContent next)
    {
        CaptureCards();
        content = next;
        pan.Enabled = !next.Preview && !next.Disabled && next.Index >= 0;
        SetNeedsLayout();
    }

    internal void Reconcile()
    {
        if (content is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var children = Subviews;
        cards = children.OfType<NativeSwipeCardView>().ToArray();
        if (cards.Length != content.Ids.Length || !cards.Select(card => card.Id).SequenceEqual(content.Ids))
            throw new InvalidOperationException("A swipe deck requires one direct NativeSwipeCard per ID, in declared order.");
        var resized = arrangedSize != Bounds.Size;
        var changed = arrangedIndex != content.Index;
        var animate = CanAnimate && !resized && arrangedIndex != int.MinValue;
        foreach (var card in cards)
            card.ConfigurePalette(content.Palette, content.Preview);
        if (resized || !CanAnimate)
        {
            dragging = false;
            pendingCommit = false;
            lockedUntil = 0;
            foreach (var card in cards)
                Clear(card.Layer);
        }
        if (changed && !dragging)
        {
            var previous = arrangedIndex;
            for (var i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                var from = captured.GetValueOrDefault(card.Id, targets.GetValueOrDefault(card.Id, StackPose(i, previous)));
                var to = StackPose(i, content.Index);
                if (i > Math.Max(previous, content.Index) && targets.TryGetValue(card.Id, out var dismissed))
                    to = dismissed;
                if (i == previous && content.Index < previous)
                {
                    var sign = content.Direction == NativeSwipeDirection.Right ? 1 : -1;
                    to = new(new CGPoint(sign * Math.Max(Bounds.Width, Window?.Bounds.Width ?? Bounds.Width) * 2,
                        from.Offset.Y + releaseVelocity.Y * .18), sign * anchorSign * Math.PI / 12, 1, 1);
                }
                targets[card.Id] = to;
                Apply(card, to);
                card.Feedback.Hide(animate);
                if (animate && (i <= Math.Max(previous, content.Index) && i >= Math.Min(previous, content.Index) - 2))
                    Animate(card, from, to, returning: content.Index > previous, duration: gestureCommit ? .3 : .5);
            }
            if (animate)
                lockedUntil = CAAnimation.CurrentMediaTime() + (content.Index > previous ? .65 : gestureCommit ? .3 : .5);
            pendingCommit = false;
            gestureCommit = false;
            releaseVelocity = default;
        }
        else if (dragging)
            ApplyDrag();
        else
            for (var i = 0; i < cards.Length; i++)
            {
                var pose = !resized && targets.TryGetValue(cards[i].Id, out var target) ? target : StackPose(i, content.Index);
                targets[cards[i].Id] = pose;
                Apply(cards[i], pose);
            }
        for (var i = 0; i < cards.Length; i++)
        {
            cards[i].AccessibilityElementsHidden = i != content.Index || content.Preview;
            cards[i].UserInteractionEnabled = !content.Preview && i == content.Index;
        }
        // Keep native ordering stable instead of moving every card through the front
        // on each model update, which invalidates the composed channel hierarchies.
        if (!children.TakeLast(cards.Length).SequenceEqual(cards))
            foreach (var card in cards)
                BringSubviewToFront(card);
        arrangedSize = Bounds.Size;
        arrangedIndex = content.Index;
        captured.Clear();
    }

    Pose StackPose(int index, int active)
    {
        if (active == int.MinValue || index > active || index < active - 2)
            return new(default, 0, 1, 0);
        return new(new CGPoint(0, index == active - 1 ? Bounds.Width * .07 : index == active - 2 ? Bounds.Width * .01 : 0),
            0, index == active ? 1 : .95, 1);
    }

    void CaptureCards()
    {
        captured.Clear();
        foreach (var card in cards)
            captured[card.Id] = Presented(card, model: dragging || gestureCommit);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveGestureRecognizer(pan);
            pan.Dispose();
        }
        base.Dispose(disposing);
    }
}
