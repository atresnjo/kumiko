using CoreAnimation;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Kumiko.iOS;

internal sealed class NativePresentationView(Action<Action> guard) : UIView
{
    internal UIView ContentView { get; } = new();
    NativePresentationContent? content;
    CGSize arrangedSize;

    internal void Configure(NativePresentationContent next)
    {
        if (ContentView.Superview is null)
            AddSubview(ContentView);
        var previous = content;
        content = next;
        ContentView.UserInteractionEnabled = next.Pose.Interactive && next.Pose.Opacity > 0;
        ContentView.AccessibilityElementsHidden = !ContentView.UserInteractionEnabled;
        if (previous?.Pose == next.Pose && !next.ReduceMotion)
            return;
        Apply(previous is not null && CanAnimate);
    }

    bool CanAnimate => content is { ReduceMotion: false } && !UIAccessibility.IsReduceMotionEnabled && Window is { Hidden: false };

    void Apply(bool animate)
    {
        if (content is not { } c)
            return;
        var layer = ContentView.Layer;
        var shown = layer.PresentationLayer;
        var from = shown?.Transform ?? layer.Transform;
        var opacity = shown?.Opacity ?? layer.Opacity;
        layer.RemoveAllAnimations();
        CATransaction.Begin();
        CATransaction.DisableActions = true;
        var transform = CATransform3D.MakeScale(c.Pose.ScaleX, c.Pose.ScaleY, 1);
        transform.M41 = c.Pose.X;
        transform.M42 = c.Pose.Y;
        layer.Transform = transform;
        layer.Opacity = c.Pose.Opacity;
        CATransaction.Commit();
        if (!animate)
            return;
        if (!from.Equals(transform))
        {
            using var spring = new CASpringAnimation
            {
                KeyPath = "transform",
                Mass = 1,
                Stiffness = c.Spring.Stiffness,
                Damping = c.Spring.Damping,
                Duration = c.Spring.Duration
            };
            using var start = NSValue.FromCATransform3D(from);
            using var end = NSValue.FromCATransform3D(transform);
            spring.From = start;
            spring.To = end;
            layer.AddAnimation(spring, "kumiko-presentation-transform");
        }
        if (Math.Abs(opacity - c.Pose.Opacity) > .001)
        {
            using var fade = CABasicAnimation.FromKeyPath("opacity");
            using var start = NSNumber.FromFloat(opacity);
            using var end = NSNumber.FromFloat(c.Pose.Opacity);
            fade.From = start;
            fade.To = end;
            fade.Duration = .18 - c.Pose.FadeDelay;
            fade.BeginTime = CAAnimation.CurrentMediaTime() + c.Pose.FadeDelay;
            fade.FillMode = CAFillMode.Backwards;
            fade.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseInEaseOut);
            layer.AddAnimation(fade, "kumiko-presentation-opacity");
        }
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();
        guard(() =>
        {
            if (arrangedSize == Bounds.Size)
                return;
            arrangedSize = Bounds.Size;
            CATransaction.Begin();
            CATransaction.DisableActions = true;
            ContentView.Layer.Bounds = Bounds;
            ContentView.Layer.Position = new CGPoint(Bounds.GetMidX(), Bounds.GetMidY());
            CATransaction.Commit();
            Apply(false);
        });
    }

    public override UIView? HitTest(CGPoint point, UIEvent? uievent)
    {
        if (Hidden || !UserInteractionEnabled || Alpha < .01 || content is not { Pose.Interactive: true } ||
            (ContentView.Layer.PresentationLayer?.Opacity ?? ContentView.Layer.Opacity) < .999)
            return null;
        var hit = ContentView.HitTest(ContentView.ConvertPointFromView(point, this), uievent);
        return hit == ContentView ? null : hit;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ContentView.Dispose();
        base.Dispose(disposing);
    }
}
