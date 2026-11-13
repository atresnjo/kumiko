using Kumiko.UI;

namespace DiskMap;

/// <summary>Colours of a tile in one state: fill, outline, name and secondary text.</summary>
internal readonly record struct TileColors(Color Fill, Color Edge, Color Text, Color Detail);

/// <summary>
/// Exact colours for one appearance. Every top-level item of the open folder gets one of ten hues; what lies
/// inside it shares the hue, a step brighter per level, so a folder's contents read as one block.
/// </summary>
internal sealed record Palette(
    bool Dark,
    Color Canvas,
    Color Surface,
    Color Raised,
    Color Well,
    Color Border,
    Color Text,
    Color Muted,
    Color Faint,
    Color Accent,
    Color AccentText,
    Color AccentSoft,
    Color Hover,
    Color Mark,
    Color Danger)
{
    /// <summary>The fill of destructive buttons and highlights, dark enough for white text in both appearances.</summary>
    public Color DangerFill => Color.Hex(0xc8312a);

    public static Palette DarkMode
    {
        get;
    } = new(
        Dark: true,
        Canvas: Color.Hex(0x0a0c10),
        Surface: Color.Hex(0x101318),
        Raised: Color.Hex(0x161a21),
        Well: Color.Hex(0x0d1014),
        Border: Color.Hex(0x222731),
        Text: Color.Hex(0xe9edf3),
        Muted: Color.Hex(0x9aa3b2),
        Faint: Color.Hex(0x7d8696),
        Accent: Color.Hex(0x8ea2ff),
        AccentText: Color.Hex(0x0a0c10),
        AccentSoft: Color.Hex(0x1c2340),
        Hover: Color.Hex(0x1a1f28),
        Mark: Color.Hex(0xffc14d),
        Danger: Color.Hex(0xff7a70));

    public static Palette LightMode
    {
        get;
    } = new(
        Dark: false,
        Canvas: Color.Hex(0xf4f5f8),
        Surface: Color.Hex(0xffffff),
        Raised: Color.Hex(0xffffff),
        Well: Color.Hex(0xeceef3),
        Border: Color.Hex(0xdde1e8),
        Text: Color.Hex(0x161a22),
        Muted: Color.Hex(0x545d6c),
        Faint: Color.Hex(0x667080),
        Accent: Color.Hex(0x3d5afe),
        AccentText: Color.Hex(0xffffff),
        AccentSoft: Color.Hex(0xe4e9ff),
        Hover: Color.Hex(0xf0f2f6),
        Mark: Color.Hex(0x8a5200),
        Danger: Color.Hex(0xc8312a));

    /// <summary>Coral, amber, green, teal, sky, indigo, violet, pink, sand and slate.</summary>
    public static Color Hue(int index) => (index < 0 ? 9 : index % 10) switch
    {
        0 => Color.Hex(0xff7a6b),
        1 => Color.Hex(0xf7b23b),
        2 => Color.Hex(0x7bd88f),
        3 => Color.Hex(0x3fcfc4),
        4 => Color.Hex(0x58aefc),
        5 => Color.Hex(0x8b8cff),
        6 => Color.Hex(0xc084fc),
        7 => Color.Hex(0xf472b6),
        8 => Color.Hex(0xd8b48a),
        _ => Color.Hex(0x93a4b8)
    };

    /// <summary>A neutral for merged small items.</summary>
    public Color RestHue => Dark ? Color.Hex(0x5d6676) : Color.Hex(0x9aa3b2);

    /// <summary>
    /// Colours of a tile. Frames are a quiet tint with a coloured header; leaves are stronger, files a little less
    /// saturated than folders. Text is mixed toward white (dark) or near-black (light) for at least 4.5:1 contrast.
    /// </summary>
    public TileColors Tile(Color hue, int depth, bool frame, bool file, bool hovered, bool dimmed)
    {
        if (file)
            hue = Color.Mix(hue, Dark ? Color.Hex(0x8a93a3) : Color.Hex(0x9aa3b2), .2f);
        float strength = frame ? (Dark ? .13f : .14f) + depth * .04f : (Dark ? .26f : .30f) + depth * .06f;
        if (hovered)
            strength += Dark ? .10f : .08f;
        var fill = Color.Mix(Canvas, hue, dimmed ? strength * .3f : strength);
        var edge = Color.Mix(Canvas, hue, dimmed ? .2f : hovered ? .85f : frame ? .38f : .5f);
        var text = Dark ? Color.Mix(hue, Color.White, .78f) : Color.Mix(hue, Color.Hex(0x0b0d12), .82f);
        var detail = Dark ? Color.Mix(hue, Color.White, .45f) : Color.Mix(hue, Color.Hex(0x0b0d12), .66f);
        if (dimmed)
        {
            text = Faint;
            detail = Faint;
        }
        return new(fill, edge, text, detail);
    }
}
