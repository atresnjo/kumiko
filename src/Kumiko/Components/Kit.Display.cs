using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>A compact status label. Soft and Outline appearances read as gpui-kit tags.</summary>
    public void Badge(
        string text,
        BadgeProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var colors = props.Colors ?? Colors(props.Tone, t);
        var paint = Paint(props.Appearance, props.Tone, colors, t, default, false, false);
        var (px, py, size) = props.Size switch
        {
            ControlSize.XSmall or ControlSize.Small => (6f, 1f, t.FontSize - 3),
            ControlSize.Large => (10f, 3f, t.FontSize),
            _ => (8f, 2f, t.FontSize - 2)
        };
        using var box = Box(key, source, line)
            .Row()
            .ItemsCenter()
            .Gap(4)
            .Px(px)
            .Py(py)
            .Rounded(props.Pill ? t.RadiusFull : props.Size is ControlSize.Small or ControlSize.XSmall ? t.RadiusSmall : t.Radius)
            .Background(paint.Background);
        if (paint.Border is { } border)
            box.Border(border);
        else if (props.Appearance is Appearance.Soft or Appearance.Outline)
            box.Border(colors.Soft(.3f));
        if (props.Icon is { } icon)
            context.Icon(icon, size, paint.Foreground, key: "icon");
        context.Text(text, Text(size, paint.Foreground, Weight.Medium), key: "text");
    }

    /// <summary>A keyboard key cap, e.g. <c>Kbd("⌘K")</c>.</summary>
    public void Kbd(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        using var box = Box(key, source, line)
            .Row()
            .Center()
            .MinWidth(20)
            .Px(5)
            .Py(1)
            .Rounded(t.RadiusSmall)
            .Background(t.Muted)
            .Border(t.Border);
        context.Text(text, new(Size: t.FontSize - 2, Weight: Weight.Medium, Color: Color.Mix(t.MutedForeground, t.Foreground, .35f), Mono: true), key: "text");
    }

    /// <summary>A circular avatar showing an image or the initials of <paramref name="name"/>.</summary>
    public void Avatar(
        string name,
        AvatarProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var size = AvatarSize(props.Size);
        using var box = Box(key, source, line)
            .Width(size)
            .Height(size)
            .Center()
            .Rounded(t.RadiusFull)
            .Clip()
            .Semantics(Role.Image, name);
        if (props.Image is { } image)
        {
            context.Image(image, size, size, t.RadiusFull, key: "image");
            return;
        }
        var background = props.Color ?? Hue(name, t.IsDark);
        box.Background(background);
        var foreground = KitTheme.Contrast(background, Color.White) >= 4.5f ? Color.White : Palette.Neutral950;
        context.Text(Initials(name), Text(MathF.Round(size * .4f), foreground, Weight.Semibold), key: "initials");
    }

    /// <summary>Overlapping avatars with a +N counter after <paramref name="max"/> entries.</summary>
    public void AvatarGroup(
        IReadOnlyList<string> names,
        int max = 4,
        ControlSize size = ControlSize.Medium,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var diameter = AvatarSize(size) + 4;
        var step = MathF.Round(diameter * .72f);
        var shown = Math.Min(names.Count, Math.Max(1, max));
        var extra = names.Count - shown;
        var count = shown + (extra > 0 ? 1 : 0);
        using var group = Box(key, source, line)
            .Width(count == 0 ? 0 : step * (count - 1) + diameter)
            .Height(diameter)
            .Semantics(Role.Group, $"{names.Count} people");
        for (int i = 0; i < count; i++)
        {
            using var ring = context.Div(key: i)
                .Absolute()
                .Offset(step * i, 0)
                .Width(diameter)
                .Height(diameter)
                .Padding(2)
                .Rounded(t.RadiusFull)
                .Background(t.Background);
            if (i < shown)
            {
                Avatar(names[i], new(size), key: "avatar");
                continue;
            }
            using (context.Div(key: "more").SizeFull().Center().Rounded(t.RadiusFull).Background(t.Muted))
                context.Text($"+{extra}", Text(MathF.Round(AvatarSize(size) * .36f), t.Foreground, Weight.Medium), key: "count");
        }
    }

    /// <summary>A 1px rule; pass a label for a centered caption ("or continue with").</summary>
    public void Separator(
        string? label = null,
        Orientation orientation = Orientation.Horizontal,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        if (orientation == Orientation.Vertical)
        {
            using (Box(key, source, line).Width(1).FillHeight().Background(t.Border))
                return;
        }
        if (label is null)
        {
            using (Box(key, source, line).FillWidth().Height(1).Background(t.Border))
                return;
        }
        using var row = Box(key, source, line).Row().FillWidth().ItemsCenter().Gap(8);
        using (context.Div(key: "before").Flex().Height(1).Background(t.Border))
        {
        }
        context.Text(label, Text(t.FontSize - 2, t.MutedForeground), key: "label");
        using (context.Div(key: "after").Flex().Height(1).Background(t.Border))
        {
        }
    }

    /// <summary>A static loading placeholder. Use <paramref name="circle"/> for avatars.</summary>
    public void Skeleton(
        Length width,
        float height = 16,
        bool circle = false,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        using (Box(key, source, line).Width(width).Height(height).Rounded(circle ? t.RadiusFull : t.Radius).Background(t.Skeleton))
        {
        }
    }

    /// <summary>A centered placeholder for an empty list or search.</summary>
    public void Empty(
        string title,
        string? description = null,
        string icon = "doc",
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        using var box = Box(key, source, line).FillWidth().Column().Center().Gap(8).Padding(24);
        using (context.Div(key: "icon").Width(44).Height(44).Center().Rounded(t.RadiusLarge).Background(t.Muted))
            context.Icon(icon, 20, t.MutedForeground, key: "glyph");
        context.Text(title, Text(t.FontSize + 2, t.Foreground, Weight.Semibold), key: "title");
        if (description is not null)
            context.Text(description, Text(t.FontSize, t.MutedForeground), key: "description");
    }

    float AvatarSize(ControlSize size) => size switch
    {
        ControlSize.XSmall => 20,
        ControlSize.Small => 24,
        ControlSize.Large => 48,
        _ => 32
    };

    static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return "?";
        var first = char.ToUpperInvariant(parts[0][0]);
        return parts.Length == 1 ? first.ToString() : $"{first}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <summary>A stable per-name color; string.GetHashCode is randomized per process, so FNV-1a is used.</summary>
    static Color Hue(string name, bool dark)
    {
        uint hash = 2166136261;
        foreach (var ch in name)
            hash = (hash ^ ch) * 16777619;
        var hue = hash % 360 / 360f;
        return FromHsl(hue, dark ? .45f : .5f, dark ? .36f : .4f);
    }

    static Color FromHsl(float h, float s, float l)
    {
        float Channel(float n)
        {
            var k = (n + h * 12) % 12;
            var a = s * MathF.Min(l, 1 - l);
            return l - a * MathF.Max(-1, MathF.Min(MathF.Min(k - 3, 9 - k), 1));
        }
        return new(Channel(0), Channel(8), Channel(4));
    }
}
