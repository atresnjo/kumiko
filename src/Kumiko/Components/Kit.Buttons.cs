using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>
    /// A labelled button. <c>Tone.Default</c> with <c>Appearance.Solid</c> is the bordered neutral button;
    /// <c>Tone.Primary</c> is the main call to action.
    /// </summary>
    public Interaction Button(
        string label,
        ButtonProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => ButtonCore(label, label, props, false, key, source, line);

    /// <summary>A square icon-only button. <paramref name="name"/> is its accessible name.</summary>
    public Interaction IconButton(
        string icon,
        string name,
        ButtonProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => ButtonCore(null, name, props with
    {
        Icon = icon
    }, true, key, source, line);

    Interaction ButtonCore(string? label, string name, ButtonProps props, bool square, NodeKey key, string source, int line)
    {
        var t = Theme;
        var colors = props.Colors ?? Colors(props.Tone, t);
        var compact = props.Size is ControlSize.XSmall or ControlSize.Small;
        var height = t.Metrics.Height(props.Size);
        using var box = Box(key, source, line)
            .Row()
            .ItemsCenter()
            .Justify(Justify.Center)
            .Gap(compact ? 4 : 6)
            .Height(height)
            .Rounded(props.Pill ? t.RadiusFull : t.Radius)
            .Semantics(Role.Button, name);
        if (square)
            box.Width(height);
        else
            box.Px(props.Appearance == Appearance.Link ? 2 : t.Metrics.Padding(props.Size));
        if (props.Width is { } width)
            box.Width(width);
        if (props.Selected)
            box.Accessibility(new(Selected: true));
        var hit = box.Control(!props.Disabled);
        var paint = Paint(props.Appearance, props.Tone, colors, t, hit, props.Selected, props.Disabled);
        box.Background(paint.Background);
        if (paint.Border is { } border)
            box.Border(border);
        if (paint.Shadow && t.Shadows)
            box.Shadow(2, 1, Color.Black.Alpha(t.IsDark ? .3f : .06f));
        Ring(box, hit, t);
        if (props.Disabled)
            box.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        var size = compact ? t.FontSize - 2 : t.FontSize;
        var iconSize = compact ? 14 : 16;
        if (props.Icon is { } icon)
            context.Icon(icon, iconSize, paint.Foreground, key: "icon");
        if (label is not null)
        {
            var style = Text(size, paint.Foreground, Weight.Medium);
            if (props.Appearance == Appearance.Link && hit.Hovered && !props.Disabled)
                context.RichText(label, [new(0, label.Length, Underline: true)], style, key: "label");
            else
                context.Text(label, style, key: "label");
        }
        if (props.TrailingIcon is { } trailing)
            context.Icon(trailing, iconSize, paint.Foreground, key: "trailing");
        return hit;
    }

    internal readonly record struct ControlPaint(Color Background, Color? Border, Color Foreground, bool Shadow);

    /// <summary>Resolves the surface for an appearance and interaction state; shared by buttons and toggles.</summary>
    internal static ControlPaint Paint(Appearance appearance, Tone tone, ToneColors c, KitTheme t, Interaction hit, bool selected,
        bool disabled)
    {
        var hovered = hit.Hovered && !disabled;
        var pressed = hit.Pressed && !disabled;
        var neutral = tone == Tone.Default;
        Color State(Color normal, Color hover, Color active) => pressed ? active : hovered || selected ? hover : normal;
        return appearance switch
        {
            Appearance.Solid when neutral => new(State(t.Background, t.Accent, Color.Mix(t.Accent, t.Foreground, .06f)), t.Input,
                t.Foreground, true),
            Appearance.Solid => new(State(c.Background, c.Hover, c.Active), null, c.Foreground, false),
            Appearance.Soft when neutral => new(State(t.Secondary.Background, t.Secondary.Hover, t.Secondary.Active), null,
                t.Secondary.Foreground, false),
            Appearance.Soft => new(State(c.Soft(.12f), c.Soft(.2f), c.Soft(.28f)), null, c.Text, false),
            Appearance.Outline when neutral => new(State(Color.Transparent, t.Accent, Color.Mix(t.Accent, t.Foreground, .06f)), t.Input,
                t.Foreground, false),
            Appearance.Outline => new(State(c.Soft(.06f), c.Soft(.14f), c.Soft(.22f)), c.Background, c.Text, false),
            Appearance.Ghost when neutral => new(State(Color.Transparent, t.Accent, Color.Mix(t.Accent, t.Foreground, .06f)), null,
                hovered || selected ? t.AccentForeground : t.Foreground, false),
            Appearance.Ghost => new(State(Color.Transparent, c.Soft(.1f), c.Soft(.18f)), null, c.Text, false),
            _ => new(Color.Transparent, null, neutral ? hovered ? t.LinkHover : t.Link : c.Text, false)
        };
    }
}
