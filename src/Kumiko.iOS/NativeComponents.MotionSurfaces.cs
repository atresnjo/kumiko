using Kumiko.UI;

namespace Kumiko.iOS;

internal sealed record NativeGradientContent(Color Start, Color End, bool Diagonal);
internal sealed record NativeCounterContent(int Value, string Suffix, TextStyle Style, bool ReduceMotion);

public static partial class NativeComponents
{
    /// <summary>Begins a native two-color gradient surface that can contain normal native content.</summary>
    public static Div NativeLinearGradient(this Context context, Color start, Color end, bool diagonal = false, NodeKey key = default)
    {
        RequireRenderer(context);
        return context.Div(key).Native(new NativeGradientContent(start, end, diagonal));
    }

    /// <summary>A direction-aware native flip counter. Reduced Motion and detached hosts update immediately.</summary>
    public static void NativeFlipCounter(this Context context, int value, string suffix, TextStyle style, NodeKey key = default)
    {
        RequireRenderer(context);
        ArgumentNullException.ThrowIfNull(suffix);
        using var counter = context.Div(key).Width(120).Height(44).Semantics(Role.Text, $"{value} {suffix}")
            .Native(new NativeCounterContent(value, suffix, style, context.ReduceMotion));
    }
}
