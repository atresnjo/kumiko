namespace Kumiko.UI;

/// <summary>What stays in a dragged element's place while it is held.</summary>
public enum DragPlaceholder
{
    /// <summary>A soft rounded box the size of the element.</summary>
    Ghost,
    /// <summary>Nothing: the place stays empty.</summary>
    Hidden,
    /// <summary>The element stays visible as well.</summary>
    Keep
}

/// <summary>
/// How a dragged element looks while held. It tilts <paramref name="Angle"/> degrees clockwise (negative tilts
/// counterclockwise) and leans <paramref name="Sway"/> more degrees per 100 points a second of sideways motion, never
/// past <paramref name="MaxAngle"/>. <paramref name="Scale"/> and <paramref name="Opacity"/> apply while held and
/// <paramref name="Shadow"/>, from 0 to 1, sets the lifted shadow's strength. <paramref name="Lift"/> times the pick-up
/// and <paramref name="Settle"/> the glide into its new place, or back home when the drag is cancelled. Start from a
/// preset such as <see cref="Tilted"/>; a scale or opacity of zero or less means unchanged.
/// </summary>
public readonly record struct DragStyle(
    float Angle = 2,
    float Sway = 1.5f,
    float MaxAngle = 8,
    float Scale = 1.03f,
    float Opacity = 1,
    float Shadow = 1,
    DragPlaceholder Placeholder = DragPlaceholder.Ghost,
    Color? PlaceholderColor = null,
    Motion Lift = Motion.Quick,
    Motion Settle = Motion.Standard
)
{
    /// <summary>A slight clockwise tilt that leans into the motion, as a card picked up from a board.</summary>
    public static DragStyle Tilted => new(Angle: 2);
    /// <summary>Lifts and grows a little without turning.</summary>
    public static DragStyle Flat => new(Angle: 0, Sway: 0, Scale: 1.02f);
    /// <summary>A bold counterclockwise tilt with a wide sway.</summary>
    public static DragStyle Playful => new(Angle: -4, Sway: 3, MaxAngle: 16, Scale: 1.06f);
}
