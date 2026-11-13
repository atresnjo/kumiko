using System.Runtime.CompilerServices;

namespace Kumiko.UI;
/// <summary>Visual emphasis for a button; enabled state is independent.</summary>
public enum ButtonVariant
{
    Default,
    Primary,
    Ghost,
    Danger,
    Secondary,
    Outline
}

/// <summary>Button appearance, enabled state and optional fixed width.</summary>
public readonly record struct ButtonOptions(ButtonVariant Variant = ButtonVariant.Default, bool Disabled = false, float? Width = null);
/// <summary>Slider range and logical-pixel width.</summary>
public readonly record struct SliderOptions(float Min = 0, float Max = 1, float Width = 200);
public sealed partial class Context
{
    /// <summary>
    /// Adds text using theme foreground by default. Set Wrap for constrained text and key for repeated
    /// siblings.
    /// </summary>
    public void Text(
        string text,
        TextStyle style = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        if (style.Wrap && style.Overflow == TextOverflow.Ellipsis)
            throw new ArgumentException("Ellipsis requires unwrapped text (Wrap=false).", nameof(style));
        if (style.Size <= 0)
            style = Theme.Typography.Body with
            {
                Overflow = style.Overflow
            };
        Add(new Style(), ContentKind.Text, text, style, key: key, source: source, line: line);
    }

    /// <summary>Adds styled spans over text. Span offsets use UTF-16 character indices.</summary>
    public void RichText(
        string text,
        TextSpan[] spans,
        TextStyle style = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        Text(text, style, key, source, line);
        Tree[Count - 1].Spans = spans;
    }

    /// <summary>Adds bold heading text with an accessibility heading role.</summary>
    public void Heading(string text, float size = 0, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        Text(text, Theme.Typography.Heading with
        {
            Size = size > 0 ? size : Theme.Typography.Heading.Size
        }, key, source, line);
        Tree[Count - 1].Role = Role.Heading;
    }

    /// <summary>Adds caption text using the theme caption scale and muted foreground.</summary>
    public void Label(
        string text,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Text(text, Theme.Typography.Caption with
    {
        Color = Theme.Muted
    }, key, source, line);
    /// <summary>
    /// Adds a platform symbol or registered SVG icon with Role.Image. accessibleName labels meaningful imagery; omit it for
    /// decoration. Label the enclosing semantic control separately when it is interactive.
    /// </summary>
    public void Icon(
        string name,
        float size = 20,
        Color? color = null,
        NodeKey key = default,
        string? accessibleName = null,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        if (IsRightToLeft())
            name = MirroredSymbol(name);
        Add(
            new Style { Width = size, Height = size },
            ContentKind.Icon,
            name,
            new(Size: size, Color: color ?? Theme.Text),
            key: key,
            source: source,
            line: line
        );
        Tree[Count - 1].Role = Role.Image;
        Tree[Count - 1].Name = accessibleName;
    }

    /// <summary>Directional symbols point toward the reading direction in right-to-left content.</summary>
    static string MirroredSymbol(string name) => name switch
    {
        "chevron.left" => "chevron.right",
        "chevron.right" => "chevron.left",
        "arrow.left" => "arrow.right",
        "arrow.right" => "arrow.left",
        _ => name
    };

    /// <summary>Adds a decoded image at a fixed size with optional rounded corners.</summary>
    public void Image(
        Rendering.Image image,
        float width,
        float height,
        float radius = 0,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Add(
        new Style { Width = width, Height = height, Radius = radius },
        ContentKind.Image,
        image: image,
        key: key,
        source: source,
        line: line
    );
    /// <summary>Consumes remaining main-axis space.</summary>
    public void Spacer(
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Add(new Style { Grow = 1 }, key: key, source: source, line: line);
    /// <summary>Adds a full-width theme border separator.</summary>
    public void Divider(
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Add(
        new Style { Width = Length.Fill, Height = 1, Background = Theme.Border },
        key: key,
        source: source,
        line: line
    );
    /// <summary>Begins a themed card scope. The caller must dispose with using.</summary>
    public Div Card(
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => Div(key, source, line)
        .Padding(Theme.Space.S4)
        .Gap(Theme.Space.S3)
        .Background(Theme.Surface)
        .Border(Theme.Border)
        .Rounded(Theme.Radius.Lg);
    /// <summary>
    /// Adds a named semantic button and returns current interaction. Built-in widgets set Changed on input.
    /// </summary>
    public Interaction Button(
        string label,
        ButtonOptions options = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        if (ControlRenderer is not null)
            return NativeButton(label, options, key, source, line);
        using var box = Div(key, source, line)
            .Row()
            .ItemsCenter()
            .Justify(Justify.Center)
            .Px(Theme.Space.S4)
            .Py(Theme.Space.S2)
            .MinHeight(Theme.Controls.Height)
            .Rounded(Theme.Radius.Md)
            .Semantics(Role.Button, label);
        if (options.Width is { } w)
            box.Width(w);
        var interaction = box.Control(!options.Disabled);
        var background = options.Variant switch
        {
            ButtonVariant.Primary => Theme.Accent,
            ButtonVariant.Danger => Theme.Danger,
            ButtonVariant.Secondary => Theme.Secondary,
            ButtonVariant.Ghost => Color.Transparent,
            _ => Theme.Surface
        };
        if (interaction.Hovered && !options.Disabled)
            background = Color.Mix(background, Theme.Text, .08f);
        if (interaction.Pressed)
            background = Color.Mix(background, Theme.Text, .08f);
        box.Background(background);
        if (options.Variant is ButtonVariant.Default or ButtonVariant.Outline)
            box.Border(Theme.Border);
        if (interaction.Focused)
            box.Border(Theme.FocusRing, Theme.Controls.FocusWidth);
        if (options.Disabled)
            box.Opacity(Theme.Controls.DisabledOpacity);
        if (interaction.Hovered)
            Cursor = Cursor.Hand;
        Text(
            label,
            Theme.Typography.Label with
            {
                Weight = Weight.Medium,
                Color = options.Variant == ButtonVariant.Danger ? Theme.DangerText : options.Variant == ButtonVariant.Primary ? Theme.AccentText : options.Variant == ButtonVariant.Secondary ? Theme.SecondaryText : Theme.Text
            }
        );
        return interaction;
    }

    /// <summary>Adds a named checkbox, updates the referenced value and reports whether it changed.</summary>
    internal bool Checkbox(string label, ref bool value, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        if (ControlRenderer is not null)
            return NativeCheckbox(label, ref value, key, source, line);
        using var row = Div(key, source, line).Row().ItemsCenter().Gap(Theme.Space.S2).Semantics(Role.Checkbox, label, isChecked: value);
        var hit = row.Control();
        if (hit.Clicked)
        {
            value = !value;
            Changed = true;
        }

        using (var square = Div()
            .Width(18)
            .Height(18)
            .Rounded(Theme.Radius.Sm)
            .Background(value ? Theme.Accent : Theme.Surface)
            .Border(hit.Focused ? Theme.Accent : Theme.Border))
            if (value)
                Text("✓", new(Size: 15, Color: Theme.AccentText));
        Text(label);
        if (hit.Hovered)
            Cursor = Cursor.Hand;
        return hit.Clicked;
    }

    /// <summary>Adds a named switch, updates the referenced value and reports whether it changed.</summary>
    internal bool Toggle(string label, ref bool value, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        using var row = Div(key, source, line).Row().ItemsCenter().Gap(Theme.Space.S2).Semantics(Role.Switch, label, isChecked: value);
        var hit = row.Control();
        if (hit.Clicked)
        {
            value = !value;
            Changed = true;
        }

        using (var track = Div().Width(38).Height(22).Rounded(Theme.Radius.Pill).Padding(2).Background(value ? Theme.Accent : Theme.Track))
        using (Div().Width(18).Height(18).Rounded(Theme.Radius.Pill).Background(Theme.Knob).Offset(value ? 16 : 0, 0))
        {
        }

        Text(label);
        return hit.Clicked;
    }

    /// <summary>
    /// Adds a slider that responds to pointer and arrow keys. Supply a semantic name for nonvisual use.
    /// </summary>
    internal bool Slider(
        ref float value,
        SliderOptions options = default,
        NodeKey key = default,
        string name = "",
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        if (options.Width <= 0)
            options = new(0, 1, 200);
        using var box = Div(key, source, line)
            .Width(options.Width)
            .Height(24)
            .Semantics(Role.Slider, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var hit = box.Control();
        box.Accessibility(new(Minimum: options.Min, Maximum: options.Max, Number: value, Step: (options.Max - options.Min) / 100),
            request => QueueAccessibility(box.Id, request));
        bool changed = false;
        var range = options.Max - options.Min;
        if (TakeAccessibility(box.Id, out var accessible))
        {
            value = Math.Clamp((float)accessible.Number, options.Min, options.Max);
            Changed = changed = true;
        }
        if ((hit.Pressed || (hit.Clicked && Input.Key == UI.Key.None)) && hit.Rect is { } r && range > 0)
        {
            var along = IsRightToLeft() ? r.Right - Input.Pointer.X - 8 : Input.Pointer.X - r.X - 8;
            var next = options.Min + Math.Clamp(along / Math.Max(1, r.Width - 16), 0, 1) * range;
            if (next != value)
            {
                value = next;
                Changed = changed = true;
            }
        }

        if (hit.Focused && Input.Key is UI.Key.Left or UI.Key.Right)
        {
            value = Math.Clamp(value + (Logical(Input.Key) == UI.Key.Right ? 1 : -1) * range / 100, options.Min, options.Max);
            Changed = changed = true;
        }

        var fraction = range > 0 ? Math.Clamp((value - options.Min) / range, 0, 1) : 0;
        using (Div().Absolute().Offset(8, 10).Width(options.Width - 16).Height(4).Rounded(Theme.Radius.Pill).Background(Theme.Track))
        {
        }

        using (Div().Absolute().Offset(8, 10).Width((options.Width - 16) * fraction).Height(4).Rounded(Theme.Radius.Pill).Background(Theme.Accent))
        {
        }

        using (Div()
            .Absolute()
            .Offset((options.Width - 16) * fraction, 4)
            .Width(16)
            .Height(16)
            .Rounded(Theme.Radius.Pill)
            .Background(Theme.Knob)
            .Border(Theme.Border))
        {
        }

        return changed;
    }

    /// <summary>Adds a clipped percentage fill with a fraction clamped between zero and one.</summary>
    public void Progress(
        float fraction,
        float width = 200,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        using var track = Div(key, source, line).Width(width).Height(6).Rounded(Theme.Radius.Pill).Background(Theme.Track);
        using (Div().Width(Length.Percent(100 * Math.Clamp(fraction, 0, 1))).Height(6).Rounded(Theme.Radius.Pill).Background(Theme.Accent))
        {
        }
    }

    /// <summary>Adds keyed buttons and updates the selected label index.</summary>
    public bool Segmented(
        string[] labels,
        ref int selected,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        using var row = Div(key, source, line).Row().Gap(Theme.Controls.SegmentGap).Padding(Theme.Controls.SegmentPadding).Rounded(Theme.Radius.Md).Background(Theme.Track);
        bool changed = false;
        for (int i = 0; i < labels.Length; i++)
        {
            if (Button(labels[i], new(i == selected ? ButtonVariant.Default : ButtonVariant.Ghost), key: labels[i]).Clicked)
            {
                selected = i;
                Changed = changed = true;
            }
        }

        return changed;
    }

    /// <summary>Adds a small tinted text badge.</summary>
    public void Badge(string text, Color? color = null, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var tint = color ?? Theme.Accent;
        using var box = Div(key, source, line).Px(Theme.Space.S2).Py(Theme.Controls.BadgePaddingY).Rounded(Theme.Radius.Pill).Background(tint.Alpha(.12f));
        Text(text, Theme.Typography.Badge with
        {
            Weight = Weight.Semibold,
            Color = tint
        });
    }

    /// <summary>Adds a keyboard shortcut label.</summary>
    public void Kbd(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        using var box = Div(key, source, line).Px(Theme.Controls.KeyboardPaddingX).Py(Theme.Controls.KeyboardPaddingY).Rounded(Theme.Radius.Sm).Background(Theme.Surface).Border(Theme.Border);
        Text(text, Theme.Typography.Keyboard with
        {
            Color = Theme.Muted
        });
    }

    /// <summary>
    /// Builds only visible fixed-height rows. Supply rowKey with stable domain IDs for mutable lists;
    /// revealRow scrolls a selected index into view.
    /// </summary>
    public void UniformList(
        string key,
        int count,
        float rowHeight,
        Action<Context, int> row,
        int overscan = 2,
        int? revealRow = null,
        Func<int, ulong>? rowKey = null
    ) => VirtualRows(key, count, rowHeight, null, row, null, overscan, revealRow, rowKey);

    /// <summary>
    /// A <see cref="UniformList(string, int, float, Action{Context, int}, int, int?, Func{int, ulong}?)"/> whose
    /// header and footer scroll with its rows, such as details above a file list. Only rows are virtual.
    /// </summary>
    public void UniformList(
        string key,
        int count,
        float rowHeight,
        Action<Context> header,
        Action<Context, int> row,
        Action<Context>? footer = null,
        int overscan = 2,
        int? revealRow = null,
        Func<int, ulong>? rowKey = null
    ) => VirtualRows(key, count, rowHeight, header, row, footer, overscan, revealRow, rowKey);

    void VirtualRows(
        string key,
        int count,
        float rowHeight,
        Action<Context>? header,
        Action<Context, int> row,
        Action<Context>? footer,
        int overscan,
        int? revealRow,
        Func<int, ulong>? rowKey
    )
    {
        if (count < 0 || rowHeight <= 0 || !float.IsFinite(rowHeight))
            throw new ArgumentOutOfRangeException(nameof(rowHeight));
        var framed = header is not null || footer is not null;
        using var list = Div(key).SizeFull().Scroll().Clip();
        if (!framed)
            list.Semantics(Role.List);
        Tree[stack[^1]].MissingRowKey = rowKey is null;
        var rect = list.PreviousRect;
        var height = rect?.Height ?? Size.Height;
        if (!Scrolls.TryGetValue(list.Id, out var state))
            Scrolls[list.Id] = state = new();
        if (header is not null)
            using (Div(key: "header").Width(Length.Fill))
            {
                listHeaders.Add((stack[^1], state));
                header(this);
            }
        else
            state.Header = 0;
        if (revealRow is { } selected)
        {
            float y = state.Header + selected * rowHeight;
            if (y < state.Offset)
                state.Offset = y;
            else if (y + rowHeight > state.Offset + height)
                state.Offset = Math.Max(0, y + rowHeight - height);
        }

        var start = state.Offset - state.Header;
        int first = Math.Max(0, (int)Math.Floor(start / rowHeight) - overscan);
        int last = Math.Min(count, (int)Math.Ceiling((start + height) / rowHeight) + overscan);
        using (var content = Div(key: "content").Width(Length.Fill).Height(count * rowHeight))
        {
            if (framed)
                content.Semantics(Role.List);
            for (int i = first; i < last; i++)
            {
                using var item = Div(key: rowKey?.Invoke(i) ?? (ulong)i)
                    .Semantics(Role.ListItem)
                    .Absolute()
                    .Offset(0, i * rowHeight)
                    .Width(Length.Fill)
                    .Height(rowHeight);
                row(this, i);
            }
        }
        if (footer is not null)
            using (Div(key: "footer").Width(Length.Fill))
                footer(this);
    }

    /// <summary>
    /// Rows below a list header start where the header ends, known only after layout. When a header's height
    /// differs from the one its rows were placed with, the frame builds again with the measured height.
    /// </summary>
    bool SettleListHeaders()
    {
        var changed = false;
        foreach (var (index, state) in listHeaders)
        {
            var measured = Tree[index].Rect.Height;
            if (Math.Abs(measured - state.Header) > .5f)
            {
                state.Header = measured;
                changed = true;
            }
        }
        listHeaders.Clear();
        return changed;
    }
}
