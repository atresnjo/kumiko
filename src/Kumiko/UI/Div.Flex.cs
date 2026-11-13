namespace Kumiko.UI;

public readonly partial struct Div
{
    /// <summary>Lays out a row from its end, without changing node identity or semantic order.</summary>
    public Div RowReverse()
    {
        Row();
        context.At(index).Style.Reverse = true;
        return this;
    }

    /// <summary>Lays out a column from bottom to top.</summary>
    public Div ColumnReverse()
    {
        Column();
        context.At(index).Style.Reverse = true;
        return this;
    }

    /// <summary>Wraps flow children into lines. Columns need a constrained height to wrap.</summary>
    public Div Wrap(FlexWrap value = FlexWrap.Wrap)
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.Wrap = value;
        return this;
    }

    /// <summary>Distributes wrapped lines within available cross-axis space.</summary>
    public Div AlignContent(ContentAlign value)
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.AlignContent = value;
        return this;
    }

    /// <summary>Overrides parent alignment for this child. Null restores inheritance.</summary>
    public Div AlignSelf(Align? value)
    {
        if (value is { } alignment && !Enum.IsDefined(alignment))
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.AlignSelf = value;
        return this;
    }

    /// <summary>Sets outer spacing in logical pixels. Margins do not collapse; negative values overlap.</summary>
    public Div Margin(float value) => Margin(Edges.All(value));

    /// <summary>Sets independent outer spacing. Horizontal edges follow the subtree's reading direction.</summary>
    public Div Margin(Edges value)
    {
        if (!float.IsFinite(value.Top) || !float.IsFinite(value.Right) ||
            !float.IsFinite(value.Bottom) || !float.IsFinite(value.Left))
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.Margin = value;
        if (context.At(index).Style.Layout.Margin is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                Margin = null
            };
        return this;
    }

    /// <summary>Sets horizontal outer spacing.</summary>
    public Div Mx(float value)
    {
        var margin = context.At(index).Style.Margin;
        context.At(index).Style.Margin = margin with
        {
            Left = value,
            Right = value
        };
        ref var s = ref context.At(index).Style;
        if (s.Layout.Margin is { } edges)
            s.Layout = s.Layout with
            {
                Margin = edges with
                {
                    Left = null,
                    Right = null,
                    Start = null,
                    End = null
                }
            };
        return this;
    }

    /// <summary>Sets vertical outer spacing.</summary>
    public Div My(float value)
    {
        var margin = context.At(index).Style.Margin;
        context.At(index).Style.Margin = margin with
        {
            Top = value,
            Bottom = value
        };
        ref var s = ref context.At(index).Style;
        if (s.Layout.Margin is { } edges)
            s.Layout = s.Layout with
            {
                Margin = edges with
                {
                    Top = null,
                    Bottom = null
                }
            };
        return this;
    }

    /// <summary>Sets the main-axis size before flex distribution. Auto uses the dimension or intrinsic size.</summary>
    public Div Basis(Length value)
    {
        if (value.Kind is not (LengthKind.Auto or LengthKind.Pixels or LengthKind.Percent) ||
            !float.IsFinite(value.Value) || value.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.Basis = value;
        return this;
    }

    /// <summary>Sets shrink weight, scaled by basis. Zero prevents shrink; null restores legacy text reflow.</summary>
    public Div Shrink(float? value = 1)
    {
        if (value is { } weight)
            NonNegative(weight);
        context.At(index).Style.Shrink = value;
        return this;
    }

    /// <summary>Sets width divided by height for an automatic dimension. Explicit two-axis sizes win.</summary>
    public Div AspectRatio(float value)
    {
        if (!float.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        context.At(index).Style.AspectRatio = value;
        return this;
    }

    /// <summary>Sets width divided by height, or restores automatic sizing with null.</summary>
    public Div AspectRatio(float? value)
    {
        if (value is { } ratio)
            return AspectRatio(ratio);
        context.At(index).Style.AspectRatio = 0;
        return this;
    }

    /// <summary>Overrides Gap for vertical spacing between rows or column items.</summary>
    public Div RowGap(float value)
    {
        NonNegative(value);
        context.At(index).Style.RowGap = value;
        if (context.At(index).Style.Layout.RowGap is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                RowGap = null
            };
        return this;
    }

    /// <summary>Overrides Gap for horizontal spacing between columns or row items.</summary>
    public Div ColumnGap(float value)
    {
        NonNegative(value);
        context.At(index).Style.ColumnGap = value;
        if (context.At(index).Style.Layout.ColumnGap is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                ColumnGap = null
            };
        return this;
    }

    static void NonNegative(float value)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
