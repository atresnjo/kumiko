namespace Kumiko.UI;
/// <summary>
/// A fluent layout scope. Always dispose it with using; call Control once after styling for interaction.
/// </summary>
public readonly partial struct Div(Context context, int index) : IDisposable
{
    internal Div Native(object content)
    {
        context.At(index).NativeContent = content;
        return this;
    }

    /// <summary>Stable node identity derived from the parent and key.</summary>
    public ulong Id => context.At(index).Id;
    /// <summary>Last completed frame bounds, useful for drag targets and viewport measurement.</summary>
    public Rect? PreviousRect => context.PreviousRect(index);

    /// <summary>Lays out children horizontally.</summary>
    public Div Row()
    {
        context.At(index).Style.Direction = Direction.Row;
        context.At(index).Style.Reverse = false;
        return this;
    }

    /// <summary>Lays out children vertically (the default).</summary>
    public Div Column()
    {
        context.At(index).Style.Direction = Direction.Column;
        context.At(index).Style.Reverse = false;
        return this;
    }

    /// <summary>Sets spacing between flow children in logical pixels.</summary>
    public Div Gap(float value)
    {
        context.At(index).Style.Gap = value;
        if (context.At(index).Style.Layout.Gap is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                Gap = null
            };
        return this;
    }

    /// <summary>Applies the same inset on every side.</summary>
    public Div Padding(float value) => Padding(Edges.All(value));
    /// <summary>Applies legacy flow-relative content insets; horizontal values mirror with reading direction.</summary>
    public Div Padding(Edges value)
    {
        context.At(index).Style.Padding = value;
        if (context.At(index).Style.Layout.Padding is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                Padding = null
            };
        return this;
    }

    /// <summary>Sets horizontal content padding.</summary>
    public Div Px(float value)
    {
        var s = context.At(index).Style.Padding;
        context.At(index).Style.Padding = s with
        {
            Left = value,
            Right = value
        };
        ref var style = ref context.At(index).Style;
        if (style.Layout.Padding is { } edges)
            style.Layout = style.Layout with
            {
                Padding = edges with
                {
                    Left = null,
                    Right = null,
                    Start = null,
                    End = null
                }
            };
        return this;
    }

    /// <summary>Sets vertical content padding.</summary>
    public Div Py(float value)
    {
        var s = context.At(index).Style.Padding;
        context.At(index).Style.Padding = s with
        {
            Top = value,
            Bottom = value
        };
        ref var style = ref context.At(index).Style;
        if (style.Layout.Padding is { } edges)
            style.Layout = style.Layout with
            {
                Padding = edges with
                {
                    Top = null,
                    Bottom = null
                }
            };
        return this;
    }

    /// <summary>Sets width. Percent uses parent content width; pixel lengths can animate.</summary>
    public Div Width(Length value, Motion animate = Motion.None)
    {
        context.At(index).Style.Width = value.Kind == LengthKind.Pixels ? Length.Px(Animate(value.Value, animate, MotionChannel.Width)) : value;
        return this;
    }

    /// <summary>Fills available horizontal space.</summary>
    public Div FillWidth() => Width(Length.Fill);
    /// <summary>Sets height. Percent uses parent content height; pixel lengths can animate.</summary>
    public Div Height(Length value, Motion animate = Motion.None)
    {
        context.At(index).Style.Height = value.Kind == LengthKind.Pixels ? Length.Px(Animate(value.Value, animate, MotionChannel.Height)) : value;
        return this;
    }

    /// <summary>Fills available vertical space.</summary>
    public Div FillHeight() => Height(Length.Fill);
    /// <summary>Fills both available dimensions.</summary>
    public Div SizeFull() => Width(Length.Fill).Height(Length.Fill);
    /// <summary>Sets the minimum layout width.</summary>
    public Div MinWidth(float value)
    {
        ref var s = ref context.At(index).Style;
        s.MinWidth = value;
        if (value == 0 && ReferenceEquals(s.Layout, LayoutOptions.Default))
            s.Layout = MinWidthZero;
        else if (value == 0 ? s.Layout.MinWidth != Length.Px(0) : s.Layout.MinWidth is not null)
            s.Layout = s.Layout with
            {
                MinWidth = value == 0 ? Length.Px(0) : null
            };
        return this;
    }

    // Shared options for the common explicit zero minimum, which rows of a virtual list set every frame.
    static readonly LayoutOptions MinWidthZero = LayoutOptions.Default with { MinWidth = Length.Px(0) };
    static readonly LayoutOptions MinHeightZero = LayoutOptions.Default with { MinHeight = Length.Px(0) };

    /// <summary>Sets the minimum layout height.</summary>
    public Div MinHeight(float value)
    {
        ref var s = ref context.At(index).Style;
        s.MinHeight = value;
        if (value == 0 && ReferenceEquals(s.Layout, LayoutOptions.Default))
            s.Layout = MinHeightZero;
        else if (value == 0 ? s.Layout.MinHeight != Length.Px(0) : s.Layout.MinHeight is not null)
            s.Layout = s.Layout with
            {
                MinHeight = value == 0 ? Length.Px(0) : null
            };
        return this;
    }

    /// <summary>Sets the maximum layout width.</summary>
    public Div MaxWidth(float value)
    {
        context.At(index).Style.MaxWidth = value;
        if (context.At(index).Style.Layout.MaxWidth is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                MaxWidth = null
            };
        return this;
    }

    /// <summary>Sets the maximum layout height.</summary>
    public Div MaxHeight(float value)
    {
        context.At(index).Style.MaxHeight = value;
        if (context.At(index).Style.Layout.MaxHeight is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                MaxHeight = null
            };
        return this;
    }

    /// <summary>
    /// Shares remaining main-axis space by weight. Use Length.Percent for proportional dimensions.
    /// </summary>
    public Div Grow(float value = 1)
    {
        NonNegative(value);
        context.At(index).Style.Grow = value;
        return this;
    }

    /// <summary>
    /// Shares available space by weight with no intrinsic main-axis basis, preserving room for fixed
    /// siblings. Configure the parent's Row/Column direction before adding children.
    /// </summary>
    public Div Flex(float weight = 1)
    {
        if (!float.IsFinite(weight) || weight <= 0)
            throw new ArgumentOutOfRangeException(nameof(weight));
        var parent = context.At(index).Parent;
        return (parent >= 0 && context.At(parent).Style.Direction == Direction.Row ? FillWidth() : FillHeight()).Grow(weight);
    }

    /// <summary>Aligns children on the cross axis.</summary>
    public Div Items(Align value)
    {
        context.At(index).Style.AlignItems = value;
        return this;
    }

    /// <summary>Aligns children on the main axis.</summary>
    public Div Justify(Justify value)
    {
        context.At(index).Style.Justify = value;
        return this;
    }

    /// <summary>Centers children on both axes.</summary>
    public Div Center() => Items(Align.Center).Justify(UI.Justify.Center);
    /// <summary>Centers children on the cross axis.</summary>
    public Div ItemsCenter() => Items(Align.Center);
    /// <summary>Sets a background fill.</summary>
    public Div Background(Color value)
    {
        context.At(index).Style.Background = value;
        return this;
    }

    /// <summary>Sets the background fill, or removes it with null.</summary>
    public Div Background(Color? value)
    {
        context.At(index).Style.Background = value;
        return this;
    }

    /// <summary>Sets border color and logical-pixel width.</summary>
    public Div Border(Color value, float width = 1)
    {
        context.At(index).Style.Border = value;
        context.At(index).Style.BorderWidth = width;
        return this;
    }

    /// <summary>Sets border color and width, or removes the border with null.</summary>
    public Div Border(Color? value, float width = 1)
    {
        context.At(index).Style.Border = value;
        context.At(index).Style.BorderWidth = value is null ? 1 : width;
        return this;
    }

    /// <summary>Sets the corner radius; prefer a theme radius token.</summary>
    public Div Rounded(float value)
    {
        context.At(index).Style.Radius = value;
        return this;
    }

    /// <summary>Adds a shadow with blur and vertical displacement.</summary>
    public Div Shadow(float blur = 12, float y = 4, Color? color = null)
    {
        context.At(index).Style.Shadow = new(color ?? Color.Black.Alpha(.18f), blur, y);
        return this;
    }

    /// <summary>Applies a reusable shadow value, or removes the shadow with null.</summary>
    public Div Shadow(Shadow? value)
    {
        context.At(index).Style.Shadow = value;
        return this;
    }

    /// <summary>Adds a backdrop-refracting glass material.</summary>
    public Div Glass(GlassStyle value = default)
    {
        context.At(index).Style.Glass = value;
        return this;
    }

    /// <summary>Applies a glass material, or removes the material with null.</summary>
    public Div Glass(GlassStyle? value)
    {
        context.At(index).Style.Glass = value;
        return this;
    }

    /// <summary>Constrains and clips vertical scrolling; optionally follows appended content at the end.</summary>
    public Div Scroll(bool followEnd = false)
    {
        context.At(index).Style.Scroll = true;
        context.At(index).Style.FollowEnd = followEnd;
        return this;
    }

    /// <summary>Enables horizontal scrolling and row layout.</summary>
    public Div ScrollX()
    {
        context.At(index).Style.Scroll = true;
        context.At(index).Style.ScrollHorizontal = true;
        context.At(index).Style.Direction = Direction.Row;
        context.At(index).Style.Reverse = false;
        return this;
    }

    /// <summary>Clips descendants to this box, including rounded corners.</summary>
    public Div Clip()
    {
        context.At(index).Style.Clip = true;
        return this;
    }

    /// <summary>Removes this node from flow and positions it relative to parent content.</summary>
    public Div Absolute()
    {
        context.At(index).Style.Absolute = true;
        if (context.At(index).Style.Layout.Position is not null)
            context.At(index).Style.Layout = context.At(index).Style.Layout with
            {
                Position = null
            };
        return this;
    }

    /// <summary>Offsets layout position; node-owned X/Y channels support named motion presets.</summary>
    public Div Offset(float x, float y, Motion animate = Motion.None)
    {
        context.At(index).Style.Offset = new(Animate(x, animate, MotionChannel.OffsetX), Animate(y, animate, MotionChannel.OffsetY));
        return this;
    }

    /// <summary>
    /// Turns this node's painted image <paramref name="degrees"/> clockwise about its center, together with its
    /// descendants and their clips. Layout, hit testing and semantics keep the upright box.
    /// </summary>
    public Div Rotate(float degrees)
    {
        context.At(index).Rotation = float.IsFinite(degrees) ? degrees : 0;
        return this;
    }

    /// <summary>
    /// Scales this node's painted image about its center, together with its descendants, for example to tuck a card
    /// behind the one in front of it. Layout, hit testing and semantics keep the unscaled box; animate the factor with
    /// <see cref="Animate"/>. A factor of one, or of zero or less, leaves it unscaled.
    /// </summary>
    public Div Scale(float factor)
    {
        context.At(index).Scale = float.IsFinite(factor) && factor > 0 ? factor : 0;
        return this;
    }

    /// <summary>
    /// Animates this node in from <paramref name="from"/> each time it appears, for example a page fading in or a
    /// dialog popping open. Only its painted image moves; layout, hit testing and semantics use its resting place.
    /// </summary>
    public Div Enter(Transition from)
    {
        if (!float.IsFinite(from.Delay) || from.Delay < 0)
            throw new ArgumentOutOfRangeException(nameof(from));
        context.At(index).EnterFrom = from;
        return this;
    }

    /// <summary>
    /// When this node stops being built, its last painted image animates to <paramref name="to"/> and then disappears,
    /// so a closed dialog or drawer can leave without the app keeping it open. The departing image is not
    /// interactive and is not in the semantic tree. A nested node with its own exit animates inside its parent's.
    /// </summary>
    public Div Exit(Transition to)
    {
        if (!float.IsFinite(to.Delay) || to.Delay < 0)
            throw new ArgumentOutOfRangeException(nameof(to));
        context.At(index).ExitTo = to;
        return this;
    }

    /// <summary>
    /// Makes nodes built with the same <paramref name="id"/> one visual element. When it is rebuilt under a different
    /// parent, such as a tab underline moving to the newly selected tab, it glides and resizes from where it was
    /// drawn. Layout changes under the same parent, such as a window resize, apply at once. Use an id unique in the
    /// window; Reduce Motion moves it immediately.
    /// </summary>
    public Div SharedLayout(string id, Motion animate = Motion.Gentle)
    {
        ref var node = ref context.At(index);
        node.SharedLayout = id;
        node.SharedMotion = animate;
        return this;
    }

    /// <summary>Animates a number owned by this node. Use independent channels for multiple values.</summary>
    public float Animate(
        float target,
        Motion motion = Motion.Standard,
        MotionChannel channel = MotionChannel.Value
    ) => motion == Motion.None ? target : context.AnimateNode(Id, channel, target, motion);
    /// <summary>
    /// Assigns an accessibility role, explicit name/value and checked state. Name icon-only controls.
    /// </summary>
    public Div Semantics(Role role, string? name = null, string? value = null, bool isChecked = false)
    {
        ref var node = ref context.At(index);
        node.Role = role;
        node.Name = name;
        node.Value = value;
        node.Checked = isChecked;
        return this;
    }

    /// <summary>
    /// Sets this subtree's reading direction, for example LeftToRight for a chart, phone number or media
    /// controls inside a right-to-left window.
    /// </summary>
    public Div Flow(FlowDirection value)
    {
        context.At(index).Style.Flow = value;
        return this;
    }

    /// <summary>Sets opacity for this subtree, clamped from zero to one.</summary>
    public Div Opacity(float value)
    {
        context.At(index).Style.Opacity = Math.Clamp(value, 0, 1);
        return this;
    }

    /// <summary>Enables text selection in descendants.</summary>
    public Div Selectable()
    {
        context.At(index).Style.Selectable = true;
        return this;
    }

    /// <summary>Registers interaction once per build pass and returns its result. Call after styling.</summary>
    public Interaction Control(bool enabled = true) => context.Interact(index, enabled);
    internal void SetCaret(Rect rect) => context.At(index).TextCaret = rect;
    /// <summary>Includes/excludes an interactive element from sequential Tab navigation.</summary>
    public Div TabStop(bool enabled)
    {
        context.At(index).Style.SkipTab = !enabled;
        return this;
    }

    public Div Accessibility(AccessibleProperties properties, Func<AccessibilityRequest, bool>? handler = null)
    {
        context.At(index).Accessible = properties;
        if (handler is not null)
            context.At(index).AccessibilityHandler = handler;
        return this;
    }
    /// <summary>Closes this scope in reverse construction order.</summary>
    public void Dispose() => context.End(index);
}
