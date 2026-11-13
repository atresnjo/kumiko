namespace Kumiko.UI;

/// <summary>
/// The look a node enters from or exits to: its opacity, offset and scale. The node animates between that look and
/// its laid-out place with <paramref name="Motion"/>'s duration, easing out. When <paramref name="Relative"/> is
/// set, <paramref name="X"/> and <paramref name="Y"/> are fractions of the node's own size, so 1 is one full width
/// to the right. A <paramref name="Scale"/> of zero or less means unscaled.
/// </summary>
public readonly record struct Transition(
    float Opacity = 0,
    float X = 0,
    float Y = 0,
    float Scale = 1,
    bool Relative = false,
    Motion Motion = Motion.Standard
)
{
    /// <summary>Seconds to wait before starting; use staggered values for sequential arrivals.</summary>
    public float Delay
    {
        get;
        init;
    }

    /// <summary>Fades between transparent and opaque.</summary>
    public static Transition Fade => new(Opacity: 0);
    /// <summary>Fades in while rising 8 points into place, as content appearing.</summary>
    public static Transition Rise => new(Y: 8);
    /// <summary>Fades in while growing from 96%, as a dialog or popover.</summary>
    public static Transition Pop => new(Scale: .96f);
    /// <summary>Slides one full width in from the right edge, as a right drawer.</summary>
    public static Transition FromRight => new(Opacity: 1, X: 1, Relative: true, Motion: Motion.Gentle);
    /// <summary>Slides one full width in from the left edge.</summary>
    public static Transition FromLeft => new(Opacity: 1, X: -1, Relative: true, Motion: Motion.Gentle);
    /// <summary>Slides one full height up from the bottom edge, as a sheet.</summary>
    public static Transition FromBottom => new(Opacity: 1, Y: 1, Relative: true, Motion: Motion.Gentle);
    /// <summary>Slides one full height down from the top edge.</summary>
    public static Transition FromTop => new(Opacity: 1, Y: -1, Relative: true, Motion: Motion.Gentle);
}
