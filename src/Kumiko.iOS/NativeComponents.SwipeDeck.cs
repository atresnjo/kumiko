using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>The committed horizontal direction of a card dismissal.</summary>
public enum NativeSwipeDirection
{
    Left, Right
}

/// <summary>A stable card identity and its committed swipe direction.</summary>
public sealed record NativeSwipeSelection(string Id, NativeSwipeDirection Direction);

internal sealed record NativeSwipeDeckContent(ImmutableArray<string> Ids, int Index, NativeSwipeDirection Direction,
    NativePalette Palette, bool Preview, bool ReduceMotion, bool Disabled = false);
internal sealed record NativeSwipeCardContent(string Id);

public static partial class NativeComponents
{
    /// <summary>A message-bound stack of arbitrary native card content. Index -1 represents completion.</summary>
    public static Div NativeSwipeDeck<TMessage>(this Ui<TMessage> ui, ImmutableArray<string> ids, int index,
        NativeSwipeDirection direction, Func<NativeSwipeSelection, TMessage> onSwipe, NativePalette palette, bool disabled = false, NodeKey key = default)
    {
        ArgumentNullException.ThrowIfNull(onSwipe);
        var c = ui.Context;
        var deck = c.NativeSwipeDeck(ids, index, direction, palette, key: key);
        deck.Native(new NativeSwipeDeckContent(ids, index, direction, palette, false, c.ReduceMotion, disabled));
        if (RequireRenderer(c).TakeSelection(deck.Id) is { } selection && !disabled && ParseSwipe(selection) is { } swipe)
            c.WithSource(deck.Id, () => ui.Dispatch(onSwipe(swipe)));
        return deck;
    }

    /// <summary>Begins a bounded card stack. Add one NativeSwipeCard for each stable ID in order, then dispose the scope.</summary>
    public static Div NativeSwipeDeck(this Context context, ImmutableArray<string> ids, int index,
        NativeSwipeDirection direction, NativePalette palette, bool preview = false, NodeKey key = default)
    {
        RequireRenderer(context);
        ArgumentNullException.ThrowIfNull(palette);
        if (ids.IsDefaultOrEmpty || ids.Length > 64 || index < -1 || index >= ids.Length ||
            ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || !Enum.IsDefined(direction))
            throw new ArgumentException("Supply 1–64 unique card IDs and an index from -1 through the final card.", nameof(ids));
        return context.Div(key).FillWidth().Grow().Semantics(Role.Group, "Swipe cards")
            .Native(new NativeSwipeDeckContent(ids, index, direction, palette, preview, context.ReduceMotion));
    }

    /// <summary>A card slot inside a NativeSwipeDeck. Its contents use ordinary native controls and Yoga layout.</summary>
    public static Div NativeSwipeCard(this Context context, string id, NodeKey key = default)
    {
        RequireRenderer(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return context.Div(key.Value is null ? id : key).Absolute().SizeFull().Semantics(Role.Group, id)
            .Native(new NativeSwipeCardContent(id));
    }

    internal static NativeSwipeSelection? ParseSwipe(string selection) => selection.Length > 2 && selection[1] == ':' &&
        selection[0] is 'l' or 'r' ? new(selection[2..], selection[0] == 'r' ? NativeSwipeDirection.Right : NativeSwipeDirection.Left) : null;
}
