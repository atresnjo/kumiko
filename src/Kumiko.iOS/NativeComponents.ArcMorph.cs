using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>
/// One routine in an arc-morph carousel. <paramref name="Time"/> renders as dot-matrix digits; when it starts
/// with <paramref name="Value"/>, those glyphs travel into the collapsed orb. <paramref name="Marks"/> label up to
/// three ruler positions. <paramref name="Label"/> names the collapsed orb; <paramref name="Accent"/> colours its ring.
/// </summary>
public sealed record NativeArcCard(string Id, string Category, string Symbol, string Time, string Title, string Detail,
    ImmutableArray<string> Marks, string Value, string Label, Color Tint, Color Accent);

/// <summary>A greeting that scrolls with the page beneath the arc carousel. <paramref name="Accent"/> follows the title in a muted tone.</summary>
public sealed record NativeArcHeading(string Title, string Accent, string Subtitle);

internal sealed record NativeArcMorphContent(ImmutableArray<NativeArcCard> Cards, int SelectedIndex, NativeArcHeading Heading,
    Color Background, bool ReduceMotion);

public static partial class NativeComponents
{
    /// <summary>The expanded height of an arc-morph carousel laid out at <paramref name="width"/> points.</summary>
    public static float ArcMorphHeight(float width) => ArcMorphMetrics.Height(width);

    /// <summary>The page scroll distance at which the arc carousel has docked as a pinned orb menu.</summary>
    public static float ArcMorphCollapseDistance(float width) => ArcMorphMetrics.Collapse(width);

    /// <summary>
    /// An inverted-arc card carousel inside a vertical <c>Scroll()</c> container. Side cards hang and tilt along an arc;
    /// horizontal drags follow the finger and snap with release velocity, while vertical drags scroll the page. Scrolling
    /// morphs the cards into a pinned menu of orbs. Place it as the scroll content's last child, absolutely at the top,
    /// <see cref="ArcMorphHeight"/> tall. Taps on a side card or orb select it; the selected card's play button calls
    /// <paramref name="onPlay"/>. The returned scope owns layout and must be disposed.
    /// </summary>
    public static Div NativeArcMorphCarousel<TMessage>(this Ui<TMessage> ui, ImmutableArray<NativeArcCard> cards, int selectedIndex,
        NativeArcHeading heading, Color background, Func<int, TMessage> onSelect, Func<int, TMessage> onPlay, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(heading);
        ArgumentNullException.ThrowIfNull(onSelect);
        ArgumentNullException.ThrowIfNull(onPlay);
        var c = ui.Context;
        var renderer = RequireRenderer(c);
        if (cards.IsDefault || cards.Length is < 3 or > 12 || selectedIndex < 0 || selectedIndex >= cards.Length ||
            cards.Any(card => card is null || string.IsNullOrWhiteSpace(card.Id) || string.IsNullOrWhiteSpace(card.Category) ||
                string.IsNullOrWhiteSpace(card.Symbol) || string.IsNullOrWhiteSpace(card.Time) || string.IsNullOrWhiteSpace(card.Value) ||
                string.IsNullOrWhiteSpace(card.Label) || card.Title is null || card.Detail is null || card.Marks.IsDefault ||
                card.Marks.Length > 3 || card.Marks.Any(mark => mark is null)) ||
            cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != cards.Length)
            throw new ArgumentException("Supply 3–12 uniquely identified cards, at most three marks each, and a valid selected index.", nameof(cards));
        var carousel = c.Div(key).Semantics(Role.Group, cards[selectedIndex].Category)
            .Native(new NativeArcMorphContent(cards, selectedIndex, heading, background, c.ReduceMotion));
        if (renderer.TakeSelection(carousel.Id) is { } id)
        {
            var play = id.StartsWith(PlayPrefix, StringComparison.Ordinal);
            var target = play ? id[PlayPrefix.Length..] : id;
            var index = Enumerable.Range(0, cards.Length).FirstOrDefault(i => cards[i].Id == target, -1);
            if (index >= 0)
                c.WithSource(carousel.Id, () => ui.Dispatch(play ? onPlay(index) : onSelect(index)));
        }
        return carousel;
    }

    internal const string PlayPrefix = "play:";
}

/// <summary>Shared expanded/collapsed geometry, in points, for the view and its layout helpers.</summary>
internal static class ArcMorphMetrics
{
    internal const float CardTop = 52;
    internal const float OrbCenter = 69;
    internal const float LabelCenter = 116;
    internal const float Docked = 134;

    internal static float Fit(float width) => Math.Clamp((width - 111) / 282, .6f, 1);
    internal static float CardHeight(float width) => 234 * Fit(width);
    internal static float DotsCenter(float width) => CardTop + CardHeight(width) + 18;
    internal static float GreetingTop(float width) => DotsCenter(width) + 26;
    internal static float Height(float width) => GreetingTop(width) + 72;
    internal static float Collapse(float width) => Height(width) - Docked;
    // The reference finishes the shape morph after about 69% of the docking scroll.
    internal static float MorphDistance(float width) => Collapse(width) * .69f;
}
