namespace Kumiko.UI;
/// <summary>Default spacing scale in logical pixels. Theme.Space can override the scale.</summary>
public static class Space
{
    public const float S0 = 0, S1 = 4, S2 = 8, S3 = 12, S4 = 16, S5 = 24, S6 = 32, S7 = 48;
}

/// <summary>Default corner radius scale. Theme.Radius can override the scale.</summary>
public static class Radius
{
    public const float None = 0, Sm = 4, Md = 8, Lg = 12, Xl = 20, Pill = 999;
}

/// <summary>Default typography roles. Override per-theme using Theme.Typography.</summary>
public static class Typography
{
    public static TextStyle Caption => new(Size: 12);
    public static TextStyle Body => new(Size: 14);
    public static TextStyle Label => new(Size: 14, Weight: Weight.Medium);
    public static TextStyle Title => new(Size: 20, Weight: Weight.Semibold);
    public static TextStyle Heading => new(Size: 28, Weight: Weight.Bold);
}

public sealed record SpaceScale(
    float S0 = Space.S0,
    float S1 = Space.S1,
    float S2 = Space.S2,
    float S3 = Space.S3,
    float S4 = Space.S4,
    float S5 = Space.S5,
    float S6 = Space.S6,
    float S7 = Space.S7
);
public sealed record RadiusScale(
    float None = Radius.None,
    float Sm = Radius.Sm,
    float Md = Radius.Md,
    float Lg = Radius.Lg,
    float Xl = Radius.Xl,
    float Pill = Radius.Pill
);
public sealed record TypographyScale
{
    public TextStyle Caption { get; init; } = Typography.Caption;
    public TextStyle Body { get; init; } = Typography.Body;
    public TextStyle Label { get; init; } = Typography.Label;
    public TextStyle Title { get; init; } = Typography.Title;
    public TextStyle Heading { get; init; } = Typography.Heading;
    public TextStyle Badge { get; init; } = new(Size: 11);
    public TextStyle Keyboard { get; init; } = new(Size: 12, Mono: true);
}

/// <summary>Shared dimensions for native buttons, fields, tables and popups.</summary>
public sealed record ControlMetrics(
    float Height = 36,
    float RowHeight = 40,
    float HeaderHeight = 40,
    float MenuItemHeight = 32,
    float PopupWidth = 240,
    float PopupGap = 4,
    float FocusWidth = 2,
    float DisabledOpacity = .5f,
    float SegmentGap = 3,
    float SegmentPadding = 3,
    float BadgePaddingY = 3,
    float KeyboardPaddingX = 6,
    float KeyboardPaddingY = 2
);
