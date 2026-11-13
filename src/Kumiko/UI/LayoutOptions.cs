namespace Kumiko.UI;

/// <summary>Physical or reading-direction-relative layout edges.</summary>
public enum LayoutEdge
{
    Top,
    Right,
    Bottom,
    Left,
    Start,
    End,
    Horizontal,
    Vertical,
    All
}

/// <summary>Optional edge overrides. Left/Right are physical; Start/End follow effective reading direction. Null preserves the fallback; Auto requests an automatic value.</summary>
public readonly record struct LengthEdges(
    Length? Top = null,
    Length? Right = null,
    Length? Bottom = null,
    Length? Left = null,
    Length? Start = null,
    Length? End = null)
{
    public static LengthEdges All(Length value) => new(value, value, value, value);
    public static LengthEdges XY(Length x, Length y) => new(y, x, y, x);
}

/// <summary>Whether a node creates a flex box, is hidden, or contributes only its children to layout.</summary>
public enum DisplayMode
{
    Flex,
    None,
    Contents
}

/// <summary>Whether authored dimensions include padding and layout borders.</summary>
public enum BoxSizing
{
    BorderBox,
    ContentBox
}

/// <summary>Yoga positioning. Static boxes do not establish an absolute containing block.</summary>
public enum PositionMode
{
    Relative,
    Static,
    Absolute
}

/// <summary>Whether overflowing children remain visible, are clipped, or scroll vertically.</summary>
public enum OverflowMode
{
    Visible,
    Hidden,
    Scroll
}

/// <summary>
/// Immutable additional Yoga style values. Reuse a value across views with Div.Layout; individual fluent
/// setters override its fields. Null values retain Kumiko's existing pixel/automatic defaults.
/// </summary>
public sealed record LayoutOptions(
    LengthEdges? Margin = null,
    LengthEdges? Padding = null,
    LengthEdges? Insets = null,
    LengthEdges? Border = null,
    Length? Gap = null,
    Length? RowGap = null,
    Length? ColumnGap = null,
    Length? MinWidth = null,
    Length? MinHeight = null,
    Length? MaxWidth = null,
    Length? MaxHeight = null,
    DisplayMode Display = DisplayMode.Flex,
    PositionMode? Position = null,
    BoxSizing BoxSizing = BoxSizing.BorderBox,
    float? FlexValue = null,
    bool ReferenceBaseline = false,
    bool ContainingBlock = false)
{
    public static LayoutOptions Default { get; } = new();
}
