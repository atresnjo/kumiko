using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class NativeArcMorphView
{
    // A slightly underdamped settle: about 0.3s with a barely visible overshoot, as in the reference swipes.
    const double Response = .34;
    const double Damping = .86;
    readonly UIPanGestureRecognizer pan;
    readonly UITapGestureRecognizer tap;
    CADisplayLink? link;
    bool dragging;
    double dragOrigin;
    double dragPosition;
    double position;
    double target;
    double springFrom;
    double springVelocity;
    double springStart;
    double velocity;

    [Export("kumikoHandleArcPan:")]
    void HandlePan(UIPanGestureRecognizer gesture) => guard(() =>
    {
        if (content is null)
            return;
        switch (gesture.State)
        {
            case UIGestureRecognizerState.Began:
                StopSpring();
                dragOrigin = position;
                dragging = true;
                Follow(gesture);
                break;
            case UIGestureRecognizerState.Changed when dragging:
                Follow(gesture);
                break;
            case UIGestureRecognizerState.Ended when dragging:
                Follow(gesture);
                Release(gesture.VelocityInView(this).X, cancelled: false);
                break;
            case UIGestureRecognizerState.Cancelled or UIGestureRecognizerState.Failed when dragging:
                Release(0, cancelled: true);
                break;
        }
    });

    [Export("kumikoHandleArcTap:")]
    void HandleTap(UITapGestureRecognizer gesture) => guard(() =>
    {
        if (content is null || gesture.State != UIGestureRecognizerState.Ended || dragging)
            return;
        var point = gesture.LocationInView(this);
        var index = CardAt(point);
        if (index < 0)
            return;
        if (index != (int)Math.Round(target))
        {
            Commit(index, velocity);
            return;
        }
        if (Progress < .4 && cards[index].PlayContains(cards[index].ConvertPointFromView(point, this)))
            select(NativeComponents.PlayPrefix + content.Cards[index].Id);
    });

    void Follow(UIPanGestureRecognizer gesture)
    {
        var raw = dragOrigin - gesture.TranslationInView(this).X / Spacing(Progress);
        var last = cards.Count - 1;
        dragPosition = raw < 0 ? -Rubber(-raw) : raw > last ? last + Rubber(raw - last) : raw;
        Render();
    }

    // Overdrag resistance approaches the iOS scroll-view feel: about half the travel at first, bounded later.
    static double Rubber(double overshoot) => .55 * overshoot / (1 + .9 * overshoot);

    void Release(double fingerVelocity, bool cancelled)
    {
        dragging = false;
        position = dragPosition;
        var release = cancelled ? 0 : Math.Clamp(-fingerVelocity / Spacing(Progress), -8, 8);
        var next = cancelled ? (int)Math.Round(target) :
            Math.Clamp((int)Math.Round(dragPosition + release * .18), 0, cards.Count - 1);
        Commit(next, release);
    }

    void Commit(int index, double initialVelocity)
    {
        var previous = (int)Math.Round(target);
        SpringTo(index, initialVelocity);
        if (index != previous && Window is { Hidden: false })
        {
            using var feedback = new UISelectionFeedbackGenerator();
            feedback.SelectionChanged();
        }
        if (index != content!.SelectedIndex)
            select(content.Cards[index].Id);
    }

    void SpringTo(int index, double initialVelocity)
    {
        target = index;
        if (!CanAnimate)
        {
            StopSpring();
            position = target;
            Render();
            return;
        }
        springFrom = position;
        springVelocity = initialVelocity;
        springStart = CAAnimation.CurrentMediaTime();
        if (link is null)
        {
            link = CADisplayLink.Create(() => guard(Tick));
            link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        }
        Render();
    }

    void Tick()
    {
        var elapsed = CAAnimation.CurrentMediaTime() - springStart;
        var omega = 2 * Math.PI / Response;
        var damped = omega * Math.Sqrt(1 - Damping * Damping);
        var a = springFrom - target;
        var b = (springVelocity + Damping * omega * a) / damped;
        var decay = Math.Exp(-Damping * omega * elapsed);
        var cos = Math.Cos(damped * elapsed);
        var sin = Math.Sin(damped * elapsed);
        var displacement = decay * (a * cos + b * sin);
        velocity = decay * ((b * damped - Damping * omega * a) * cos - (a * damped + Damping * omega * b) * sin);
        position = target + displacement;
        if (elapsed > 2 || Math.Abs(displacement) < .0004 && Math.Abs(velocity) < .01)
        {
            position = target;
            StopSpring();
        }
        Render();
    }

    void StopSpring()
    {
        velocity = 0;
        if (link is null)
            return;
        link.Invalidate();
        link.Dispose();
        link = null;
    }

    /// <summary>The frontmost visible card or orb under a point. Docked orbs keep at least a 48pt target.</summary>
    int CardAt(CGPoint point)
    {
        var at = dragging ? dragPosition : position;
        var docked = Progress > .5;
        foreach (var index in Enumerable.Range(0, cards.Count).OrderBy(i => Math.Abs(i - at)))
        {
            var card = cards[index];
            if (card.Alpha < .1)
                continue;
            var local = card.ConvertPointFromView(point, this);
            var bounds = card.Bounds;
            if (docked)
            {
                var dx = local.X - bounds.Width / 2;
                var dy = local.Y - bounds.Height / 2;
                var reach = Math.Max(bounds.Width / 2, 24);
                if (dx * dx + dy * dy <= reach * reach)
                    return index;
            }
            else if (bounds.Contains(local))
                return index;
        }
        return -1;
    }

    // Only cards, orbs and the docked menu band take touches; the rest of the frame passes to page content.
    public override UIView? HitTest(CGPoint point, UIEvent? uievent)
    {
        UIView? hit = null;
        guard(() =>
        {
            if (content is null || Hidden || Alpha < .01 || !UserInteractionEnabled)
                return;
            if (CardAt(point) >= 0)
            {
                hit = this;
                return;
            }
            var band = container.ConvertPointFromView(point, this);
            if (Progress > .5 && band.Y >= 36 && band.Y <= ArcMorphMetrics.Docked && band.X >= 0 && band.X <= Bounds.Width)
                hit = this;
        });
        return hit;
    }

    public override bool PointInside(CGPoint point, UIEvent? uievent) => HitTest(point, uievent) is not null;
}
