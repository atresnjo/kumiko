namespace Kumiko.UI;

public readonly partial struct Div
{
    /// <summary>Applies reusable typed layout options without CSS parsing or engine objects.</summary>
    public Div Layout(LayoutOptions value)
    {
        ValidateOptions(value);
        if (context.At(index).Style.Layout.Position is not null)
            context.At(index).Style.Absolute = false;
        context.At(index).Style.Layout = value;
        SetDisplayHidden(value.Display);
        if (value.Position is { } position)
            context.At(index).Style.Absolute = position == PositionMode.Absolute;
        return this;
    }

    /// <summary>Sets automatic, pixel or percentage outer spacing on all physical edges.</summary>
    public Div Margin(Length value) => Margin(LengthEdges.All(value));
    /// <summary>Overrides selected outer edges. Left/Right are physical; Start/End follow the node's reading direction.</summary>
    public Div Margin(LengthEdges value)
    {
        ValidateEdges(value, auto: true, negative: true);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Margin = value
        };
        return this;
    }
    /// <summary>Sets one outer edge or edge group; Auto absorbs available free space.</summary>
    public Div Margin(LayoutEdge edge, Length value)
    {
        var edges = context.At(index).Style.Layout.Margin ?? default;
        return Margin(SetEdge(edges, edge, value));
    }
    /// <summary>Sets horizontal automatic, pixel or percentage margins.</summary>
    public Div Mx(Length value) => Margin(LayoutEdge.Horizontal, value);
    /// <summary>Sets vertical automatic, pixel or percentage margins.</summary>
    public Div My(Length value) => Margin(LayoutEdge.Vertical, value);

    /// <summary>Sets pixel or percentage content padding on every physical edge.</summary>
    public Div Padding(Length value) => Padding(LengthEdges.All(value));
    /// <summary>Overrides physical Left/Right or logical Start/End padding. Percent padding uses containing width.</summary>
    public Div Padding(LengthEdges value)
    {
        ValidateEdges(value, auto: false, negative: false);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Padding = value
        };
        return this;
    }
    /// <summary>Sets one padding edge or edge group.</summary>
    public Div Padding(LayoutEdge edge, Length value)
    {
        var edges = context.At(index).Style.Layout.Padding ?? default;
        return Padding(SetEdge(edges, edge, value));
    }
    /// <summary>Sets horizontal pixel or percentage padding.</summary>
    public Div Px(Length value) => Padding(LayoutEdge.Horizontal, value);
    /// <summary>Sets vertical pixel or percentage padding.</summary>
    public Div Py(Length value) => Padding(LayoutEdge.Vertical, value);

    /// <summary>Sets pixel or percentage spacing on both axes.</summary>
    public Div Gap(Length value)
    {
        ValidateLength(value, auto: false, negative: false);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Gap = value
        };
        return this;
    }
    /// <summary>Sets vertical pixel or percentage spacing.</summary>
    public Div RowGap(Length value)
    {
        ValidateLength(value, auto: false, negative: false);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            RowGap = value
        };
        return this;
    }
    /// <summary>Sets horizontal pixel or percentage spacing.</summary>
    public Div ColumnGap(Length value)
    {
        ValidateLength(value, auto: false, negative: false);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            ColumnGap = value
        };
        return this;
    }

    /// <summary>Sets the minimum width in pixels or percent. Auto removes the bound.</summary>
    public Div MinWidth(Length value) => Bound(value, true, true);
    /// <summary>Sets the minimum height in pixels or percent. Auto removes the bound.</summary>
    public Div MinHeight(Length value) => Bound(value, false, true);
    /// <summary>Sets the maximum width in pixels or percent. Auto removes the bound.</summary>
    public Div MaxWidth(Length value) => Bound(value, true, false);
    /// <summary>Sets the maximum height in pixels or percent. Auto removes the bound.</summary>
    public Div MaxHeight(Length value) => Bound(value, false, false);
    Div Bound(Length value, bool width, bool minimum)
    {
        ValidateLength(value, auto: true, negative: false);
        ref var s = ref context.At(index).Style;
        s.Layout = (width, minimum) switch
        {
            (true, true) => s.Layout with { MinWidth = value },
            (false, true) => s.Layout with { MinHeight = value },
            (true, false) => s.Layout with { MaxWidth = value },
            _ => s.Layout with { MaxHeight = value }
        };
        return this;
    }

    /// <summary>Sets layout participation. Hidden nodes retain their keyed application state.</summary>
    public Div Display(DisplayMode value)
    {
        ValidateEnum(value);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Display = value
        };
        SetDisplayHidden(value);
        return this;
    }
    void SetDisplayHidden(DisplayMode value)
    {
        ref var node = ref context.At(index);
        node.LayoutHidden = value == DisplayMode.None || node.Parent >= 0 && context.At(node.Parent).LayoutHidden;
    }

    /// <summary>Sets whether dimensions include padding and explicitly authored layout borders.</summary>
    public Div BoxSizing(BoxSizing value)
    {
        ValidateEnum(value);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            BoxSizing = value
        };
        return this;
    }
    /// <summary>Sets static, relative or absolute Yoga positioning. Offset remains an independent translation.</summary>
    public Div Position(PositionMode value)
    {
        ValidateEnum(value);
        ref var s = ref context.At(index).Style;
        s.Absolute = value == PositionMode.Absolute;
        s.Layout = s.Layout with
        {
            Position = value
        };
        return this;
    }
    /// <summary>Sets all Yoga insets. Opposing absolute insets stretch an automatic dimension.</summary>
    public Div Inset(Length value) => Inset(LengthEdges.All(value));
    /// <summary>Sets physical Left/Right or logical Start/End insets. Static boxes ignore them; Auto restores automatic positioning.</summary>
    public Div Inset(LengthEdges value)
    {
        ValidateEdges(value, auto: true, negative: true);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Insets = value
        };
        return this;
    }
    /// <summary>Overrides one Yoga inset edge or edge group.</summary>
    public Div Inset(LayoutEdge edge, Length value)
    {
        var edges = context.At(index).Style.Layout.Insets ?? default;
        return Inset(SetEdge(edges, edge, value));
    }
    /// <summary>Sets visible, clipped or vertical scrolling overflow.</summary>
    public Div Overflow(OverflowMode value)
    {
        ValidateEnum(value);
        ref var s = ref context.At(index).Style;
        s.Clip = value == OverflowMode.Hidden;
        s.Scroll = value == OverflowMode.Scroll;
        s.ScrollHorizontal = false;
        return this;
    }

    /// <summary>Sets flow-relative layout border widths independently of the painted Border. Widths consume content space.</summary>
    public Div LayoutBorder(Edges value)
    {
        NonNegative(value.Top);
        NonNegative(value.Right);
        NonNegative(value.Bottom);
        NonNegative(value.Left);
        ref var s = ref context.At(index).Style;
        s.FlowBorder = value;
        s.Layout = s.Layout with
        {
            Border = null
        };
        return this;
    }
    /// <summary>Sets physical/logical layout border widths in pixels; use Border to paint an outline.</summary>
    public Div LayoutBorder(LengthEdges value)
    {
        ValidateEdges(value, auto: false, negative: false, pixelsOnly: true);
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            Border = value
        };
        return this;
    }
    /// <summary>Sets one physical/logical layout border width or edge group in pixels.</summary>
    public Div LayoutBorder(LayoutEdge edge, float value)
    {
        NonNegative(value);
        var edges = context.At(index).Style.Layout.Border ?? default;
        return LayoutBorder(SetEdge(edges, edge, Length.Px(value)));
    }
    /// <summary>Sets Yoga's signed flex shorthand. Null restores Kumiko's individual grow/shrink/basis defaults.</summary>
    public Div FlexValue(float? value)
    {
        if (value is { } factor && !float.IsFinite(factor))
            throw new ArgumentOutOfRangeException(nameof(value));
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            FlexValue = value
        };
        return this;
    }
    /// <summary>Marks this child as the baseline reference for its container.</summary>
    public Div ReferenceBaseline(bool value = true)
    {
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            ReferenceBaseline = value
        };
        return this;
    }
    /// <summary>Establishes an absolute containing block even when this box uses static positioning.</summary>
    public Div ContainingBlock(bool value = true)
    {
        ref var s = ref context.At(index).Style;
        s.Layout = s.Layout with
        {
            ContainingBlock = value
        };
        return this;
    }
}
