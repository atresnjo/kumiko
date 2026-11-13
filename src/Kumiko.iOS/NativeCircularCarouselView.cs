using CoreAnimation;
using CoreGraphics;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeCircularCarouselView : UIView
{
    readonly Action<Action> guard;
    readonly Action<string> select;
    readonly UIPanGestureRecognizer pan;
    readonly List<CarouselCard> cards = [];
    NativeCarouselContent? content;
    CGSize arrangedSize;
    double fromPosition;
    double targetPosition;
    double started;
    double releaseVelocity;
    const double Duration = 1.9;

    internal NativeCircularCarouselView(Action<Action> guard, Action<string> select)
    {
        this.guard = guard;
        this.select = select;
        pan = new UIPanGestureRecognizer(this, new ObjCRuntime.Selector("kumikoHandlePan:"));
        pan.MaximumNumberOfTouches = 1;
        pan.ShouldBegin = recognizer =>
        {
            var allowed = false;
            guard(() =>
            {
                var velocity = ((UIPanGestureRecognizer)recognizer).VelocityInView(this);
                allowed = content is { Interactive: true, Preview: false } && Math.Abs(velocity.X) > Math.Abs(velocity.Y);
            });
            return allowed;
        };
        AddGestureRecognizer(pan);
    }

    internal void Configure(NativeCarouselContent next)
    {
        var previous = content;
        var replaced = previous is null || !previous.Items.Select(item => item.Id).SequenceEqual(next.Items.Select(item => item.Id));
        if (replaced)
        {
            foreach (var card in cards)
                card.Dispose();
            cards.Clear();
            foreach (var item in next.Items)
            {
                var card = new CarouselCard(guard);
                cards.Add(card);
                AddSubview(card);
                card.Configure(item, next.Preview);
            }
            fromPosition = targetPosition = next.SelectedIndex;
            started = 0;
        }
        content = next;
        pan.Enabled = next.Interactive && !next.Preview;
        ClipsToBounds = true;
        IsAccessibilityElement = true;
        AccessibilityLabel = next.Items[next.SelectedIndex].Title;
        AccessibilityValue = $"{next.SelectedIndex + 1} of {next.Items.Length}";
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].Configure(next.Items[i], next.Preview);
            cards[i].AccessibilityElementsHidden = i != next.SelectedIndex;
        }
        if (dragging)
        {
            SetNeedsLayout();
            return;
        }
        if (!replaced && previous!.SelectedIndex != next.SelectedIndex &&
            Math.Abs(Relative(next.SelectedIndex, targetPosition, cards.Count)) > .001)
        {
            var origin = CurrentPosition();
            var step = Relative(next.SelectedIndex, previous.SelectedIndex, cards.Count);
            targetPosition += step;
            fromPosition = origin;
            releaseVelocity = 0;
            started = CAAnimation.CurrentMediaTime();
            Arrange(animate: CanAnimate);
        }
        else if (arrangedSize != Bounds.Size || replaced || !CanAnimate)
            Arrange(animate: false);
        SetNeedsLayout();
    }

    bool CanAnimate => content is { Preview: false, ReduceMotion: false } && Window is { Hidden: false } &&
        !UIAccessibility.IsReduceMotionEnabled;

    double CurrentPosition() => dragging ? dragPosition : started <= 0 ? targetPosition :
        PositionAt(Math.Max(0, CAAnimation.CurrentMediaTime() - started));

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            if (arrangedSize != Bounds.Size)
            {
                dragging = false;
                Arrange(animate: false);
            }
        });
    }

    void Arrange(bool animate)
    {
        if (content is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        arrangedSize = Bounds.Size;
        var width = Math.Min(content.Preview ? 200 : 360, Bounds.Width * (content.Preview ? .52 : .6));
        var height = width * .7;
        foreach (var card in cards)
        {
            card.Bounds = new CGRect(0, 0, width, height);
            card.LayoutIfNeeded();
        }
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            Clear(card.Layer);
            Clear(card.Decoration.Layer);
            Apply(card, i, targetPosition);
            if (animate)
                Animate(card, i);
        }
        foreach (var index in Enumerable.Range(0, cards.Count).OrderByDescending(i => Math.Abs(Relative(i, targetPosition, cards.Count))))
            BringSubviewToFront(cards[index]);
        if (!animate)
        {
            fromPosition = targetPosition;
            started = 0;
        }
    }

    static double Relative(double index, double position, int count)
    {
        var delta = index - position;
        return delta - Math.Round(delta / count) * count;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RemoveGestureRecognizer(pan);
            pan.Dispose();
            foreach (var card in cards)
                card.Dispose();
            cards.Clear();
        }
        base.Dispose(disposing);
    }
}
