using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace Kumiko.iOS;

internal sealed partial class NativeSwipeDeckView
{
    readonly record struct Pose(CGPoint Offset, double Rotation, double Scale, float Opacity);

    Pose Presented(NativeSwipeCardView card, bool model = false)
    {
        var layer = model ? card.Layer : card.Layer.PresentationLayer ?? card.Layer;
        var transform = layer.Transform;
        return new(new CGPoint(layer.Position.X - Bounds.Width / 2, layer.Position.Y - Bounds.Height / 2),
            Math.Atan2(transform.M12, transform.M11), Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12), layer.Opacity);
    }

    void Apply(NativeSwipeCardView card, Pose pose)
    {
        card.Layer.Position = new CGPoint(Bounds.Width / 2 + pose.Offset.X, Bounds.Height / 2 + pose.Offset.Y);
        card.Layer.Transform = CATransform3D.MakeRotation((nfloat)pose.Rotation, 0, 0, 1)
            .Scale((nfloat)pose.Scale, (nfloat)pose.Scale, 1);
        card.Layer.Opacity = pose.Opacity;
    }

    void Animate(NativeSwipeCardView card, Pose from, Pose to, bool returning, double duration)
    {
        Clear(card.Layer);
        var undo = returning && content!.Index > arrangedIndex;
        if (returning)
            duration = .65;
        const int count = 61;
        var poses = Enumerable.Range(0, count).Select(i =>
        {
            var t = duration * i / (count - 1);
            var fraction = (double)i / (count - 1);
            var progress = returning ? 1 - Math.Exp(-30 * t) * (1 + 30 * t) : 1 - Math.Pow(1 - fraction, 3);
            if (undo)
            {
                // Undo uses the reference's overdamped stiffness=1000, damping=90, mass=1.
                var root = Math.Sqrt(90 * 90 - 4 * 1000);
                var slow = (-90 + root) / 2;
                var fast = (-90 - root) / 2;
                progress = 1 + (fast * Math.Exp(slow * t) - slow * Math.Exp(fast * t)) / (slow - fast);
            }
            if (i == count - 1)
                progress = 1;
            var x = from.Offset.X + (to.Offset.X - from.Offset.X) * progress;
            var y = from.Offset.Y + (to.Offset.Y - from.Offset.Y) * progress;
            if (returning && Math.Abs(from.Offset.X) > .01 && i != count - 1)
            {
                x += releaseVelocity.X * t * Math.Exp(-30 * t);
                y += releaseVelocity.Y * t * Math.Exp(-30 * t);
            }
            else if (gestureCommit && Math.Abs(to.Offset.X) > Bounds.Width && i != count - 1)
                y = from.Offset.Y + releaseVelocity.Y * .18 * (1 - Math.Exp(-t / .18)) / (1 - Math.Exp(-duration / .18));
            return new Pose(new CGPoint(x, y), from.Rotation + (to.Rotation - from.Rotation) * progress,
                from.Scale + (to.Scale - from.Scale) * progress, (float)(from.Opacity + (to.Opacity - from.Opacity) * progress));
        }).ToArray();
        // Constant channels already have the target model value. Avoid allocating and
        // submitting hundreds of identical keyframes for the stack's untouched channels.
        if (poses.Any(pose => pose.Offset != to.Offset))
            Frames(card.Layer, "position", poses.Select(pose => (NSObject)NSValue.FromCGPoint(
                new CGPoint(Bounds.Width / 2 + pose.Offset.X, Bounds.Height / 2 + pose.Offset.Y))).ToArray(), duration);
        if (from.Rotation != to.Rotation)
            Frames(card.Layer, "transform.rotation.z", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Rotation)).ToArray(), duration);
        if (from.Scale != to.Scale)
            Frames(card.Layer, "transform.scale", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Scale)).ToArray(), duration);
        if (from.Opacity != to.Opacity)
            Frames(card.Layer, "opacity", poses.Select(pose => (NSObject)NSNumber.FromFloat(pose.Opacity)).ToArray(), duration);
    }

    static void Frames(CALayer layer, string path, NSObject[] values, double duration)
    {
        try
        {
            using var animation = CAKeyFrameAnimation.FromKeyPath(path);
            animation.Values = values;
            animation.Duration = duration;
            animation.CalculationMode = CAAnimation.AnimationLinear;
            layer.AddAnimation(animation, "kumiko-deck-" + path);
        }
        finally
        {
            foreach (var value in values)
                value.Dispose();
        }
    }

    static void Clear(CALayer layer)
    {
        foreach (var path in new[] { "position", "transform.rotation.z", "transform.scale", "opacity" })
            layer.RemoveAnimation("kumiko-deck-" + path);
    }
}
