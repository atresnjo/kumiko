using Kumiko.UI;

namespace Kumiko.iOS;

/// <summary>Colors shared by native surfaces, actions, tabs, dials and timelines.</summary>
public sealed record NativePalette(Color Background, Color Surface, Color Raised, Color Text,
    Color Muted, Color Accent, Color Warm, Color Border)
{
    public static NativePalette FocusDark => new(Color.Hex(0x101615), Color.Hex(0x191f1f),
        Color.Hex(0x252e2b), Color.Hex(0xe4eee7), Color.Hex(0xa2b0aa),
        Color.Hex(0xb9e3cc), Color.Hex(0xe9bd78), Color.Hex(0x35423d));
}

/// <summary>A native SF Symbol and stable identity for one bottom navigation destination.</summary>
public sealed record NativeTabItem(string Id, string Label, string Symbol)
{
    /// <summary>Short badge text, such as an unread count; null shows no badge.</summary>
    public string? Badge
    {
        get; init;
    }
}

/// <summary>One normalized, colored interval in a native activity strip.</summary>
public sealed record NativeTimelineSegment(float Start, float End, Color Color);

public enum NativeActionStyle
{
    Plain,
    Tonal,
    Mint,
    Glow
}

/// <summary>Native action appearance. Compact visuals retain a minimum 44-point hit target.</summary>
public readonly record struct NativeActionOptions(string? Symbol = null,
    NativeActionStyle Style = NativeActionStyle.Tonal, bool Disabled = false,
    bool Compact = false, bool Chevron = false, bool Accent = false, float? Width = null, float Height = 48, bool FillWidth = false);

/// <summary>
/// A native container's fill, border, radius and optional ambient top illumination. A positive elevation casts a
/// soft shadow of that blur radius and leaves child views unclipped.
/// </summary>
public readonly record struct NativeSurfaceOptions(Color Background, Color? Border = null,
    float Radius = 0, Color? Glow = null, float Elevation = 0);

internal sealed record NativeSurfaceContent(NativeSurfaceOptions Options);
internal sealed record NativeLabelContent(string Text, TextStyle Style, NativeFontDesign Design = NativeFontDesign.Default,
    bool Heading = false);
internal sealed record NativeActionContent(string Label, NativeActionOptions Options, NativePalette Palette);
internal sealed record NativeDialContent(string Value, string Status, string Detail, float Progress, NativePalette Palette);
internal sealed record NativeTimelineContent(System.Collections.Immutable.ImmutableArray<NativeTimelineSegment> Segments,
    float Marker, NativePalette Palette);
internal sealed record NativeSheetContent(NativePalette? Palette, float Height);
/// <summary>A tab bar in a palette, or with the system material and <paramref name="Tint"/> when the palette is null.</summary>
internal sealed record NativeTabBarContent(System.Collections.Immutable.ImmutableArray<NativeTabItem> Items,
    string SelectedId, NativePalette? Palette, Color? Tint = null);
