using System.Numerics;
using System.Runtime.InteropServices;

namespace Kumiko.UI;

public readonly record struct Point(float X = 0, float Y = 0);
public readonly record struct Size(float Width, float Height);
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Point p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;
    public Rect Inset(Edges e) => new(X + e.Left, Y + e.Top, Math.Max(0, Width - e.Horizontal), Math.Max(0, Height - e.Vertical));
    public Rect Intersect(Rect b)
    {
        var x = Math.Max(X, b.X);
        var y = Math.Max(Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(Right, b.Right) - x), Math.Max(0, Math.Min(Bottom, b.Bottom) - y));
    }

    public static Rect Infinite => new(-1e6f, -1e6f, 2e6f, 2e6f);
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Color(float R, float G, float B, float A = 1)
{
    public static Color Hex(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    public Color Alpha(float alpha) => this with
    {
        A = A * alpha
    };
    public static Color Mix(
        Color a,
        Color b,
        float t
    ) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    public Vector4 Vector => new(R, G, B, A);
    public static Color White => new(1, 1, 1);
    public static Color Black => new(0, 0, 0);
    public static Color Transparent => new(0, 0, 0, 0);
}

public readonly record struct Edges(float Top, float Right, float Bottom, float Left)
{
    public static Edges All(float value) => new(value, value, value, value);
    public static Edges XY(float x, float y) => new(y, x, y, x);
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}

/// <summary>How layout resolves a dimension.</summary>
public enum LengthKind
{
    Auto,
    Pixels,
    Fill,
    Percent
}

/// <summary>An automatic, fixed-pixel, filling or parent-relative dimension.</summary>
public readonly record struct Length(LengthKind Kind, float Value = 0)
{
    public static Length Auto => default;
    public static Length Fill => new(LengthKind.Fill);

    public static Length Px(float value) => new(LengthKind.Pixels, Math.Max(0, value));
    /// <summary>A percentage of the parent's content size; 25 means one quarter.</summary>
    public static Length Percent(float value)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new(LengthKind.Percent, value);
    }

    public static implicit operator Length(float value) => Px(value);
}

public enum Direction
{
    Column,
    Row
}

/// <summary>
/// Horizontal reading direction of a subtree. Right-to-left mirrors rows, shorthand spacing, Offset
/// translations, alignment and scrollbars; typed physical edges stay fixed. Text shaping is unaffected.
/// Inherit follows the parent; the window follows its culture unless <see cref="Context.FlowDirection"/> is set.
/// </summary>
public enum FlowDirection
{
    Inherit,
    LeftToRight,
    RightToLeft
}

public enum Align
{
    Start,
    Center,
    End,
    Stretch,
    Baseline
}

public enum Justify
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

/// <summary>Whether overflowing flow children form additional lines.</summary>
public enum FlexWrap
{
    NoWrap,
    Wrap,
    WrapReverse
}

/// <summary>Distribution of flex lines along the cross axis of a wrapping container.</summary>
public enum ContentAlign
{
    Start,
    Center,
    End,
    Stretch,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum Weight
{
    Regular,
    Medium,
    Semibold,
    Bold
}

public readonly record struct Shadow(Color Color, float Blur = 12, float Y = 4);
public readonly record struct GlassStyle(bool Clear = false, Color Tint = default);
/// <summary>How text is painted when its allocated width is smaller than its content.</summary>
public enum TextOverflow
{
    Visible,
    Clip,
    Ellipsis
}
/// <summary>
/// Font size, weight, foreground and shaping options. Prefer Theme.Typography roles for consistent type.
/// </summary>
public readonly record struct TextStyle(
    float Size = 14,
    Weight Weight = Weight.Regular,
    Color? Color = null,
    bool Wrap = false,
    bool Mono = false,
    bool Italic = false,
    float Leading = 1
)
{
    /// <summary>Defaults to Visible. Ellipsis requires Wrap=false and preserves source text.</summary>
    public TextOverflow Overflow
    {
        get; init;
    }
}
/// <summary>A styled UTF-16 range within rich text.</summary>
public readonly record struct TextSpan(
    int Start,
    int End,
    Color? Color = null,
    Weight? Weight = null,
    bool Italic = false,
    bool Mono = false,
    Color? Background = null,
    bool Underline = false,
    bool Strike = false
)
{
    internal Kumiko.RichText.RichAttachment? Attachment
    {
        get; init;
    }
}
/// <summary>
/// Layout and paint properties in logical pixels. Use Div to configure a scoped node; percent lengths resolve
/// against parent content.
/// </summary>
public struct Style
{
    LayoutOptions? layout;
    public LayoutOptions Layout
    {
        readonly get => layout ?? LayoutOptions.Default;
        set => layout = value;
    }
    public Direction Direction;
    public float Gap;
    public Edges Padding;
    public Edges Margin;
    internal Edges FlowBorder;
    public Length Basis;
    public float? Shrink;
    public float AspectRatio;
    public Align? AlignSelf;
    public FlexWrap Wrap;
    public ContentAlign AlignContent;
    public bool Reverse;
    public float? RowGap, ColumnGap;
    public Length Width, Height;
    public float MinWidth, MinHeight, MaxWidth, MaxHeight, Grow;
    public Align AlignItems;
    public Justify Justify;
    public Color? Background, Border;
    public float BorderWidth, Radius, Opacity;
    public Shadow? Shadow;
    public GlassStyle? Glass;
    public bool Scroll, ScrollHorizontal, Absolute, Clip, FollowEnd, Selectable, Control, SkipTab;
    public Point Offset;
    public FlowDirection Flow;
    public Style()
    {
        MaxWidth = MaxHeight = float.PositiveInfinity;
        BorderWidth = Opacity = 1;
    }
}
