using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>A stable item in a native circular carousel. Motif selects one of three decorative color arrangements.</summary>
public sealed record NativeCarouselItem(string Id, string Title, string Emoji, int Motif = 0);

internal sealed record NativeCarouselContent(ImmutableArray<NativeCarouselItem> Items, int SelectedIndex, bool Preview, bool ReduceMotion,
    bool Interactive = false);

public static partial class NativeComponents
{
    /// <summary>A finger-driven circular carousel. A completed swipe sends its snapped index through the app's messages.</summary>
    public static Div NativeCircularCarousel<TMessage>(this Ui<TMessage> ui, ImmutableArray<NativeCarouselItem> items,
        int selectedIndex, Func<int, TMessage> onSelect, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(onSelect);
        var c = ui.Context;
        var carousel = c.NativeCircularCarousel(items, selectedIndex, key: key);
        carousel.Native(new NativeCarouselContent(items, selectedIndex, false, c.ReduceMotion, Interactive: true));
        if (RequireRenderer(c).TakeSelection(carousel.Id) is { } id)
        {
            var selected = Enumerable.Range(0, items.Length).FirstOrDefault(index => items[index].Id == id, -1);
            if (selected >= 0)
                c.WithSource(carousel.Id, () => ui.Dispatch(onSelect(selected)));
        }
        return carousel;
    }

    /// <summary>
    /// A bounded, model-driven circular card carousel. UIKit composites its spring path; the app owns advancing/pausing.
    /// The returned scope owns layout and must be disposed. Preview uses smaller cards for a gallery thumbnail.
    /// </summary>
    public static Div NativeCircularCarousel(this Context context, ImmutableArray<NativeCarouselItem> items,
        int selectedIndex, bool preview = false, NodeKey key = default)
    {
        RequireRenderer(context);
        if (items.IsDefault || items.Length is < 5 or > 32 || selectedIndex < 0 || selectedIndex >= items.Length ||
            items.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title) ||
                string.IsNullOrWhiteSpace(item.Emoji) || item.Motif is < 0 or > 2) ||
            items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Supply 5–32 uniquely identified cards and a valid selected index.", nameof(items));
        return context.Div(key).FillWidth().FillHeight().Semantics(Role.Group, items[selectedIndex].Title)
            .Native(new NativeCarouselContent(items, selectedIndex, preview, context.ReduceMotion));
    }
}
