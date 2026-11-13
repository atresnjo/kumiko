using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>A native subtree's presentation, independent of its logical Yoga frame.</summary>
public readonly record struct NativePose(float X = 0, float Y = 0, float ScaleX = 1,
    float ScaleY = 1, float Opacity = 1, bool Interactive = true, float FadeDelay = 0)
{
    public NativePose() : this(0, 0, 1, 1, 1, true, 0)
    {
    }
}

/// <summary>Unit-mass Core Animation spring parameters and the retained animation duration in seconds.</summary>
public readonly record struct NativeSpring(float Stiffness, float Damping, float Duration);

internal sealed record NativePresentationContent(NativePose Pose, NativeSpring Spring, bool ReduceMotion);

public static partial class NativeComponents
{
    /// <summary>
    /// Begins a native presentation scope. Retargets from the shown pose; fade timing is 180ms.
    /// Children retain normal layout and transformed native hit testing. Dispose the returned scope.
    /// </summary>
    public static Div NativePresentation(this Context context, NativePose pose, NativeSpring spring, NodeKey key = default)
    {
        RequireRenderer(context);
        if (!float.IsFinite(pose.X) || !float.IsFinite(pose.Y) || !float.IsFinite(pose.ScaleX) ||
            !float.IsFinite(pose.ScaleY) || pose.ScaleX <= 0 || pose.ScaleY <= 0 ||
            !float.IsFinite(pose.Opacity) || pose.Opacity is < 0 or > 1 ||
            !float.IsFinite(pose.FadeDelay) || pose.FadeDelay is < 0 or >= .18f ||
            !float.IsFinite(spring.Stiffness) || spring.Stiffness <= 0 ||
            !float.IsFinite(spring.Damping) || spring.Damping <= 0 ||
            !float.IsFinite(spring.Duration) || spring.Duration is <= 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(pose));
        return context.Div(key).Native(new NativePresentationContent(pose, spring, context.ReduceMotion));
    }
}
