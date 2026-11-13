namespace Kumiko.UI;

public readonly partial struct Div
{
    static LengthEdges SetEdge(LengthEdges edges, LayoutEdge edge, Length value) => edge switch
    {
        LayoutEdge.Top => edges with { Top = value },
        LayoutEdge.Right => edges with { Right = value },
        LayoutEdge.Bottom => edges with { Bottom = value },
        LayoutEdge.Left => edges with { Left = value },
        LayoutEdge.Start => edges with { Start = value },
        LayoutEdge.End => edges with { End = value },
        LayoutEdge.Horizontal => edges with { Left = value, Right = value, Start = null, End = null },
        LayoutEdge.Vertical => edges with { Top = value, Bottom = value },
        LayoutEdge.All => LengthEdges.All(value),
        _ => throw new ArgumentOutOfRangeException(nameof(edge))
    };

    static void ValidateEnum<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    static void ValidateLength(Length value, bool auto, bool negative, bool pixelsOnly = false)
    {
        if (!float.IsFinite(value.Value) || (!negative && value.Value < 0) ||
            value.Kind != LengthKind.Pixels && (pixelsOnly || value.Kind != LengthKind.Percent && (!auto || value.Kind != LengthKind.Auto)))
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    static void ValidateEdges(LengthEdges value, bool auto, bool negative, bool pixelsOnly = false)
    {
        if (value.Top is { } top)
            ValidateLength(top, auto, negative, pixelsOnly);
        if (value.Right is { } right)
            ValidateLength(right, auto, negative, pixelsOnly);
        if (value.Bottom is { } bottom)
            ValidateLength(bottom, auto, negative, pixelsOnly);
        if (value.Left is { } left)
            ValidateLength(left, auto, negative, pixelsOnly);
        if (value.Start is { } start)
            ValidateLength(start, auto, negative, pixelsOnly);
        if (value.End is { } end)
            ValidateLength(end, auto, negative, pixelsOnly);
    }

    static void ValidateOptions(LayoutOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateEnum(value.Display);
        ValidateEnum(value.BoxSizing);
        if (value.Position is { } position)
            ValidateEnum(position);
        if (value.FlexValue is { } flex && !float.IsFinite(flex))
            throw new ArgumentOutOfRangeException(nameof(value));
        if (value.Margin is { } margin)
            ValidateEdges(margin, true, true);
        if (value.Padding is { } padding)
            ValidateEdges(padding, false, false);
        if (value.Insets is { } insets)
            ValidateEdges(insets, true, true);
        if (value.Border is { } border)
            ValidateEdges(border, false, false, true);
        ValidateOptional(value.Gap, false);
        ValidateOptional(value.RowGap, false);
        ValidateOptional(value.ColumnGap, false);
        ValidateOptional(value.MinWidth, true);
        ValidateOptional(value.MinHeight, true);
        ValidateOptional(value.MaxWidth, true);
        ValidateOptional(value.MaxHeight, true);
    }

    static void ValidateOptional(Length? value, bool auto)
    {
        if (value is { } length)
            ValidateLength(length, auto, false);
    }
}
