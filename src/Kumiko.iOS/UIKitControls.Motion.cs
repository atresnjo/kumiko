using CoreAnimation;
using CoreGraphics;
using Foundation;
using Kumiko.UI;
using UIKit;

namespace Kumiko.iOS;

internal sealed partial class UIKitControls
{
    readonly HashSet<ulong> currentMotionIds = new();
    readonly record struct SharedOrigin(CGRect Frame, ulong Owner, Type Kind);
    CGSize motionSize;

    Dictionary<string, SharedOrigin> PrepareMotion(IReadOnlyList<NativeControlNode> controls)
    {
        var result = new Dictionary<string, SharedOrigin>(StringComparer.Ordinal);
        // Resizing cancels old geometry, including an in-flight shared transition.
        var resized = motionSize != root.Bounds.Size;
        var reduceMotion = UIAccessibility.IsReduceMotionEnabled || controls.Any(node => node.ReduceMotion);
        motionSize = root.Bounds.Size;
        foreach (var previous in current.Values)
        {
            if (!entries.TryGetValue(previous.Id, out var entry))
                continue;
            if (resized || reduceMotion)
                ClearMotion(entry.View.Layer);
            if (previous.SharedLayout is not { } tag || resized)
                continue;
            var layer = entry.View.Layer.PresentationLayer ?? entry.View.Layer;
            // Convert from the current presentation tree, so a rapid reversal starts where it is visible.
            var frame = layer.ConvertRectToLayer(layer.Bounds, root.Layer.PresentationLayer ?? root.Layer);
            result.Add(tag, new(frame, previous.SharedOwner, previous.Content.GetType()));
        }
        return result;
    }

    void PlaceMotion(UIView view, NativeControlNode node, bool created, Dictionary<string, SharedOrigin> origins, bool allowMotion)
    {
        if (node.ReduceMotion || !allowMotion)
        {
            ClearMotion(view.Layer);
            return;
        }
        // Ordinary controls do not need native layer access when motion is enabled.
        if (node.SharedLayout is null && (!created || node.EnterFrom is null))
            return;
        if (node.SharedLayout is { } tag && origins.TryGetValue(tag, out var source) &&
            source.Owner != node.SharedOwner && source.Kind == node.Content.GetType())
        {
            ClearMotion(view.Layer);
            var target = view.ConvertRectToView(view.Bounds, root);
            if (target.Width <= 0 || target.Height <= 0 || source.Frame.Width <= 0 || source.Frame.Height <= 0)
                return;
            var from = CATransform3D.MakeScale(source.Frame.Width / target.Width, source.Frame.Height / target.Height, 1);
            using var transform = NSValue.FromCATransform3D(from);
            using var identity = NSValue.FromCATransform3D(CATransform3D.Identity);
            var center = view.Superview!.ConvertPointFromView(new CGPoint(source.Frame.GetMidX(), source.Frame.GetMidY()), root);
            using var position = NSValue.FromCGPoint(center);
            using var destination = NSValue.FromCGPoint(view.Layer.Position);
            Spring(view.Layer, "transform", transform, identity, Duration(node.SharedMotion));
            Spring(view.Layer, "position", position, destination, Duration(node.SharedMotion));
        }
        else if (created && node.EnterFrom is { } enter && Duration(enter.Motion) > 0)
        {
            var dx = enter.Relative ? enter.X * view.Bounds.Width : enter.X;
            var dy = enter.Relative ? enter.Y * view.Bounds.Height : enter.Y;
            var scale = enter.Scale > 0 ? enter.Scale : 1;
            var transform = CATransform3D.MakeScale(scale, scale, 1);
            transform.M41 = dx;
            transform.M42 = dy;
            using var start = NSValue.FromCATransform3D(transform);
            using var end = NSValue.FromCATransform3D(CATransform3D.Identity);
            Basic(view.Layer, "transform", start, end, enter);
            using var opacity = NSNumber.FromFloat(Math.Clamp(enter.Opacity, 0, 1));
            using var opaque = NSNumber.FromFloat(1);
            Basic(view.Layer, "opacity", opacity, opaque, enter);
        }
    }

    static void ClearMotion(CALayer layer)
    {
        layer.RemoveAnimation("kumiko-shared-transform");
        layer.RemoveAnimation("kumiko-shared-position");
        layer.RemoveAnimation("kumiko-enter-transform");
        layer.RemoveAnimation("kumiko-enter-opacity");
    }

    static void Spring(CALayer layer, string path, NSObject from, NSObject to, double duration)
    {
        if (duration <= 0)
            return;
        using var animation = new CASpringAnimation { KeyPath = path };
        animation.Mass = 1;
        animation.Stiffness = 180;
        animation.Damping = 22;
        animation.InitialVelocity = 0;
        // With mass=1 and damping=22, the envelope exp(-11t) falls below .0001 at .84s.
        // Scale time to the preset without changing damping ratio or overshoot.
        var speed = .84 / duration;
        animation.Stiffness *= (nfloat)(speed * speed);
        animation.Damping *= (nfloat)speed;
        animation.From = from;
        animation.To = to;
        animation.Duration = duration;
        layer.AddAnimation(animation, "kumiko-shared-" + path);
    }

    static void Basic(CALayer layer, string path, NSObject from, NSObject to, Transition enter)
    {
        using var animation = CABasicAnimation.FromKeyPath(path);
        animation.From = from;
        animation.To = to;
        animation.Duration = Duration(enter.Motion);
        animation.BeginTime = layer.ConvertTimeFromLayer(CAAnimation.CurrentMediaTime(), null) + enter.Delay;
        animation.FillMode = CAFillMode.Backwards;
        animation.TimingFunction = CAMediaTimingFunction.FromName(CAMediaTimingFunction.EaseOut);
        layer.AddAnimation(animation, "kumiko-enter-" + path);
    }

    static double Duration(Motion motion) => motion switch
    {
        Motion.None => 0,
        Motion.Quick => .2,
        Motion.Standard => .3,
        Motion.Gentle => .55,
        _ => .7
    };

    void FinishMotion(IReadOnlyList<NativeControlNode> controls)
    {
        currentMotionIds.Clear();
        foreach (var node in controls)
            currentMotionIds.Add(node.Id);
    }
}
