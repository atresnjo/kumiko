using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>The glyph on a native orb. <see cref="Empty"/> draws an unfilled dashed placeholder instead of a sphere.</summary>
public enum NativeOrbMark
{
    None,
    Check,
    Dot,
    Empty
}

internal sealed record NativeOrbContent(Color Color, NativeOrbMark Mark, float Glow);
internal sealed record NativeDashContent(Color Color);

public static partial class NativeComponents
{
    /// <summary>
    /// A glossy pastel sphere with a soft coloured shadow, used for streaks and small wins. Glow in [0, 1] scales the
    /// shadow; a null accessible name marks the orb decorative.
    /// </summary>
    public static void NativeOrb(this Context context, Color color, float diameter, NativeOrbMark mark = NativeOrbMark.None,
        float glow = .5f, string? accessibleName = null, NodeKey key = default)
    {
        RequireRenderer(context);
        if (!float.IsFinite(diameter) || diameter < 8 || !float.IsFinite(glow) || glow is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(diameter));
        using var orb = context.Div(key).Width(diameter).Height(diameter).Native(new NativeOrbContent(color, mark, glow));
        if (accessibleName is not null)
            orb.Semantics(Role.Image, accessibleName);
    }

    /// <summary>Begins a horizontal dashed rule centred in its box. The caller sizes the returned scope and disposes it.</summary>
    public static Div NativeDashedLine(this Context context, Color color, NodeKey key = default)
    {
        RequireRenderer(context);
        return context.Div(key).Height(2).Native(new NativeDashContent(color));
    }
}
