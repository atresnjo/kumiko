using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace Kumiko.iOS;

internal sealed partial class NativeCircularCarouselView
{
    const double AngleStep = 2 * Math.PI / 9.5;
    readonly record struct Pose(CGPoint Position, double Rotation, double Scale, double Opacity, double Focus);

    Pose At(int index, double position)
    {
        var delta = Relative(index, position, cards.Count);
        var angle = Math.Clamp(delta, -2, 2) * AngleStep;
        var radius = Bounds.Height / 2;
        return new(new CGPoint(Bounds.Width / 2 + radius * Math.Sin(angle), Bounds.Height - radius * Math.Cos(angle)),
            angle, 1 - Math.Min(1, Math.Abs(delta)) * .25, Math.Abs(delta) < 2.5 ? 1 : 0,
            Math.Clamp(1 - Math.Abs(delta) * 2, 0, 1));
    }

    void Apply(CarouselCard card, int index, double position)
    {
        var pose = At(index, position);
        card.Layer.Position = pose.Position;
        card.Layer.Transform = CATransform3D.MakeRotation((nfloat)pose.Rotation, 0, 0, 1)
            .Scale((nfloat)pose.Scale, (nfloat)pose.Scale, 1);
        card.Layer.Opacity = (float)pose.Opacity;
        card.Decoration.Layer.Opacity = (float)pose.Focus;
        card.Decoration.Layer.Transform = CATransform3D.MakeScale((nfloat)Math.Max(.001, pose.Focus),
            (nfloat)Math.Max(.001, pose.Focus), 1);
    }

    // The reference's damping=54, stiffness=300, mass=5.5; native keyframes sample the actual spring along its arc.
    double PositionAt(double seconds)
    {
        if (seconds >= Duration)
            return targetPosition;
        const double decay = 54 / (2 * 5.5);
        var frequency = Math.Sqrt(300 / 5.5 - decay * decay);
        var displacement = fromPosition - targetPosition;
        return targetPosition + Math.Exp(-decay * seconds) * (displacement * Math.Cos(frequency * seconds) +
            (releaseVelocity + decay * displacement) / frequency * Math.Sin(frequency * seconds));
    }

    void Animate(CarouselCard card, int index)
    {
        const int samples = 115;
        var poses = Enumerable.Range(0, samples).Select(i => At(index, PositionAt(Duration * i / (samples - 1)))).ToArray();
        Keyframes(card.Layer, "position", poses.Select(pose => (NSObject)NSValue.FromCGPoint(pose.Position)).ToArray());
        Keyframes(card.Layer, "transform.rotation.z", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Rotation)).ToArray());
        Keyframes(card.Layer, "transform.scale", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Scale)).ToArray());
        Keyframes(card.Layer, "opacity", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Opacity)).ToArray());
        Keyframes(card.Decoration.Layer, "opacity", poses.Select(pose => (NSObject)NSNumber.FromDouble(pose.Focus)).ToArray());
        Keyframes(card.Decoration.Layer, "transform.scale", poses.Select(pose => (NSObject)NSNumber.FromDouble(Math.Max(.001, pose.Focus))).ToArray());
    }

    static void Keyframes(CALayer layer, string path, NSObject[] values)
    {
        try
        {
            using var animation = CAKeyFrameAnimation.FromKeyPath(path);
            animation.Values = values;
            animation.Duration = Duration;
            animation.CalculationMode = CAAnimation.AnimationLinear;
            layer.AddAnimation(animation, "kumiko-orbit-" + path);
        }
        finally
        {
            foreach (var value in values)
                value.Dispose();
        }
    }

    static void Clear(CALayer layer)
    {
        layer.RemoveAnimation("kumiko-orbit-position");
        layer.RemoveAnimation("kumiko-orbit-transform.rotation.z");
        layer.RemoveAnimation("kumiko-orbit-transform.scale");
        layer.RemoveAnimation("kumiko-orbit-opacity");
    }
}
