namespace Kumiko.UI;

/// <summary>A row a <see cref="Context.VirtualList"/> asks its renderer to build.</summary>
/// <param name="Index">The row's index into the list's offsets.</param>
/// <param name="ScrollX">The list's horizontal offset; shift scrolling content left by this amount.</param>
/// <param name="Pinned">True when this is the section header pinned to the top of the viewport.</param>
public readonly record struct VirtualRow(int Index, float ScrollX, bool Pinned);

/// <summary>How a <see cref="VirtualReveal"/> brings its row into view.</summary>
public enum VirtualRevealMode
{
    /// <summary>Scrolls the least distance at once, as a moving cursor does; a visible row stays put.</summary>
    Nearest,
    /// <summary>Glides until the row sits Margin below the top, as a jump to a section does.</summary>
    Top
}

/// <summary>Scrolls <paramref name="Row"/> into view once for each distinct <paramref name="Request"/>.</summary>
public readonly record struct VirtualReveal(int Row, long Request, VirtualRevealMode Mode = VirtualRevealMode.Nearest, float Margin = 0);

/// <summary>Options for <see cref="Context.VirtualList"/>.</summary>
public sealed record VirtualListOptions
{
    /// <summary>Rows built above and below the viewport.</summary>
    public int Overscan { get; init; } = 4;
    /// <summary>Maps a row to the header row of its section, or -1. That header stays pinned at the top while its
    /// section scrolls, and the next header pushes it away.</summary>
    public Func<int, int>? StickyHeader
    {
        get; init;
    }
    /// <summary>Width of the widest row. When it exceeds the viewport the list also scrolls horizontally and each
    /// <see cref="VirtualRow.ScrollX"/> reports the offset, so rows can keep gutters in place.</summary>
    public float ContentWidth
    {
        get; init;
    }
    public VirtualReveal? Reveal
    {
        get; init;
    }
}

/// <summary>The rows and viewport a <see cref="Context.VirtualList"/> used for this build.</summary>
/// <param name="First">The first row intersecting the viewport.</param>
/// <param name="Last">One past the last row intersecting the viewport.</param>
/// <param name="Offset">The vertical scroll offset.</param>
/// <param name="Viewport">The viewport height from the latest layout.</param>
/// <param name="ViewportWidth">The viewport width from the latest layout.</param>
/// <param name="ScrollX">The horizontal scroll offset.</param>
public readonly record struct VirtualListView(int First, int Last, float Offset, float Viewport, float ViewportWidth, float ScrollX);

public sealed partial class Context
{
    /// <summary>Horizontal offsets of virtual lists, keyed by the list node.</summary>
    internal readonly Dictionary<ulong, ScrollState> CrossScrolls = new();

    /// <summary>
    /// Builds only the visible rows of a list whose rows have individual heights. Row i spans
    /// offsets[i]..offsets[i + 1], so offsets has one more entry than there are rows and never decreases.
    /// rowKey supplies stable domain identity. Hit testing, focus and semantics follow the built rows.
    /// </summary>
    public VirtualListView VirtualList(string key, IReadOnlyList<float> offsets, Func<int, ulong> rowKey, Action<Context, VirtualRow> row,
        VirtualListOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(rowKey);
        ArgumentNullException.ThrowIfNull(row);
        if (offsets.Count == 0)
            throw new ArgumentException("Offsets needs a leading 0 and one end per row.", nameof(offsets));
        options ??= DefaultVirtualList;
        int count = offsets.Count - 1;
        float total = offsets[count];
        using var list = Div(key).SizeFull().Scroll().Clip().Semantics(Role.List);
        var rect = list.PreviousRect;
        float height = rect?.Height ?? Size.Height, width = rect?.Width ?? Size.Width;
        if (!Scrolls.TryGetValue(list.Id, out var state))
            Scrolls[list.Id] = state = new();
        state.Max = Math.Max(0, total - height);
        state.Offset = Math.Clamp(state.Offset, 0, state.Max);
        float scrollX = Cross(list.Id, options.ContentWidth, width);
        var sticky = options.StickyHeader;
        if (options.Reveal is { } reveal && reveal.Row >= 0 && reveal.Row < count)
            RevealVirtual(list.Id, state, offsets, reveal, height, sticky);

        int first = Row(offsets, state.Offset);
        int last = Math.Min(count, Row(offsets, state.Offset + height) + 1);
        while (last > first && offsets[last - 1] >= state.Offset + height)
            last--;
        int from = Math.Max(0, first - options.Overscan), to = Math.Min(count, last + options.Overscan);
        int pinned = first < count && sticky is not null ? sticky(first) : -1;
        using (Div(key: "content").Width(Length.Fill).Height(total))
        {
            for (int i = from; i < to; i++)
            {
                if (i == pinned)
                    continue;
                using var item = Div(key: rowKey(i)).Semantics(Role.ListItem).Absolute().Offset(0, offsets[i])
                    .Width(Length.Fill).Height(offsets[i + 1] - offsets[i]);
                row(this, new(i, scrollX, false));
            }
            if (pinned >= 0 && pinned < count)
            {
                float size = offsets[pinned + 1] - offsets[pinned];
                float y = Math.Max(offsets[pinned], state.Offset);
                for (int i = first + 1; i < to; i++)
                {
                    if (sticky!(i) != i)
                        continue;
                    y = Math.Min(y, offsets[i] - size);
                    break;
                }
                using var item = Div(key: rowKey(pinned)).Semantics(Role.ListItem).Absolute().Offset(0, y)
                    .Width(Length.Fill).Height(size);
                row(this, new(pinned, scrollX, true));
            }
        }
        return new(first, last, state.Offset, height, width, scrollX);
    }

    static readonly VirtualListOptions DefaultVirtualList = new();

    /// <summary>The first row whose end lies below <paramref name="y"/>.</summary>
    static int Row(IReadOnlyList<float> offsets, float y)
    {
        int low = 0, high = offsets.Count - 2;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (offsets[middle + 1] <= y)
                low = middle + 1;
            else
                high = middle;
        }
        return Math.Max(0, low);
    }

    float Cross(ulong id, float contentWidth, float width)
    {
        if (contentWidth <= width)
        {
            CrossScrolls.Remove(id);
            return 0;
        }
        if (!CrossScrolls.TryGetValue(id, out var cross))
            CrossScrolls[id] = cross = new()
            {
                Horizontal = true,
                Follow = false
            };
        cross.Max = contentWidth - width;
        cross.Offset = Math.Clamp(cross.Offset, 0, cross.Max);
        return cross.Offset;
    }

    void RevealVirtual(ulong id, ScrollState state, IReadOnlyList<float> offsets, VirtualReveal reveal, float height, Func<int, int>? sticky)
    {
        if (revealed.TryGetValue(id, out var last) && last == reveal.Request)
            return;
        revealed[id] = reveal.Request;
        float top = offsets[reveal.Row], bottom = offsets[reveal.Row + 1];
        // A pinned header covers the top of the viewport; rows of its section are revealed below it.
        int header = sticky?.Invoke(reveal.Row) ?? -1;
        float cover = header >= 0 && header != reveal.Row ? offsets[header + 1] - offsets[header] : 0;
        if (reveal.Mode == VirtualRevealMode.Top)
        {
            var target = top - reveal.Margin - cover;
            Glide(state, target, ReduceMotion ? 0 : .22f + Math.Min(.2f, Math.Abs(target - state.Offset) / 8000), true);
            Animating |= !float.IsNaN(state.Target);
            return;
        }
        float offset = state.Offset;
        if (top - reveal.Margin - cover < offset)
            offset = top - reveal.Margin - cover;
        else if (bottom + reveal.Margin > offset + height)
            offset = bottom + reveal.Margin - height;
        state.Target = float.NaN;
        state.Offset = Math.Clamp(offset, 0, state.Max);
        state.Follow = false;
    }
}
