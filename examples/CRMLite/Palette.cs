using Kumiko.Components;
using Kumiko.UI;

namespace CRMLite;

/// <summary>
/// Neutral surfaces with a primary accent (blue by default, chosen in settings). Every text pair meets WCAG AA.
/// </summary>
public sealed partial record Palette(bool Dark, Accent Tint = Accent.Blue)
{
    public Color Page => Color.Hex(Dark ? 0x18181bu : 0xffffffu);
    public Color Sidebar => Color.Hex(Dark ? 0x111113u : 0xf7f7f8u);
    public Color Lane => Color.Hex(Dark ? 0x1e1e22u : 0xf6f6f7u);
    public Color Card => Color.Hex(Dark ? 0x232327u : 0xffffffu);
    public Color Raised => Color.Hex(Dark ? 0x26262bu : 0xffffffu);
    public Color Border => Color.Hex(Dark ? 0x2c2c31u : 0xe7e7e9u);
    public Color Line => Color.Hex(Dark ? 0x232328u : 0xf0f0f1u);
    public Color Text => Color.Hex(Dark ? 0xededefu : 0x1c1c1fu);
    public Color Secondary => Color.Hex(Dark ? 0xa8a9b1u : 0x55575fu);
    public Color Faint => Color.Hex(Dark ? 0x8e8f98u : 0x686a72u);
    /// <summary>The primary fill; white text on it meets 4.5:1 in both themes.</summary>
    public Color Accent => Tint switch
    {
        CRMLite.Accent.Violet => Hex(0x7c5ce8, 0x6d4ae6),
        CRMLite.Accent.Green => Hex(0x16803c, 0x15803d),
        CRMLite.Accent.Orange => Hex(0xc2440f, 0xc2410c),
        CRMLite.Accent.Rose => Hex(0xc0306c, 0xbe185d),
        _ => Hex(0x5068e8, 0x4359e0)
    };

    /// <summary>Accent-colored text on page and sidebar surfaces.</summary>
    public Color AccentSoft => Tint switch
    {
        CRMLite.Accent.Violet => Hex(0xb7a6ff, 0x5b3bc4),
        CRMLite.Accent.Green => Hex(0x6fd59a, 0x166534),
        CRMLite.Accent.Orange => Hex(0xffa877, 0x9a3412),
        CRMLite.Accent.Rose => Hex(0xff8fb7, 0x9d174d),
        _ => Hex(0x9aa9ff, 0x3448c9)
    };
    public Color Hover => Text.Alpha(Dark ? .055f : .035f);
    public Color Menu => Color.Hex(Dark ? 0x2a2a30u : 0xf1f1f3u);
    public Color Stale => Color.Hex(Dark ? 0xf0904au : 0xb45309u);
    public Color Flame => Color.Hex(Dark ? 0xff9a5cu : 0xe2702au);
    public Color Star => Color.Hex(Dark ? 0xfbbf24u : 0xf2a516u);
    public Color Danger => Color.Hex(Dark ? 0xf87171u : 0xc62828u);
    /// <summary>The agent's mark and its "Suggested" and "Added by agent" tags.</summary>
    public Color Spark => Color.Hex(Dark ? 0xe8906fu : 0xd97757u);
    /// <summary>Fills behind a white check or exclamation mark on a finished or failed plan step.</summary>
    public Color Success => Color.Hex(Dark ? 0x2f9e4fu : 0x1a7f37u);
    public Color Alarm => Color.Hex(Dark ? 0xdc2626u : 0xc62828u);
    public Color NoteFill => Color.Hex(Dark ? 0x2a2516u : 0xfdf6e3u);
    public Color NoteBorder => Color.Hex(Dark ? 0x4a3f1fu : 0xf1e2b8u);
    public Color NoteText => Color.Hex(Dark ? 0xe8e1cfu : 0x3d3a33u);
    public Color Scrim => Color.Black.Alpha(Dark ? .55f : .28f);
    public Color Shade => Color.Black.Alpha(Dark ? .45f : .07f);

    /// <summary>Dot, tag fill and tag text for a stage.</summary>
    public (Color Dot, Color Fill, Color Text) Of(Stage stage) => stage switch
    {
        Stage.Qualified => (Hex(0x3cc2b6, 0x2aa198), Hex(0x173330, 0xe1f3f1), Hex(0x7fdcd2, 0x1a6660)),
        Stage.Demo => (Hex(0xa596ff, 0x8b7cf6), Hex(0x2a2645, 0xeeecfd), Hex(0xc4baff, 0x5444c4)),
        Stage.Proposal => (Hex(0xf0b429, 0xd4960f), Hex(0x3a2f12, 0xfbf1db), Hex(0xf5cf72, 0x825800)),
        Stage.Negotiation => (Hex(0xff8a5c, 0xe0663a), Hex(0x3d2219, 0xfdebe4), Hex(0xffb393, 0xa13f1b)),
        Stage.Won => (Hex(0x6cc75f, 0x52a447), Hex(0x1d3319, 0xe6f3e3), Hex(0x9fe093, 0x35712b)),
        _ => (Hex(0x8a8f98, 0x9aa0a6), Hex(0x2a2c30, 0xf0f1f3), Hex(0xc4c7cc, 0x585c63))
    };

    Color Hex(uint dark, uint light) => Color.Hex(Dark ? dark : light);

    /// <summary>A soft avatar: a tint of the person's color with dark initials.</summary>
    public (Color Fill, Color Text) Avatar(uint color) => Dark
        ? (Color.Mix(Page, Color.Hex(color), .32f), Color.Mix(Color.Hex(color), Color.White, .7f))
        : (Color.Mix(Page, Color.Hex(color), .2f), Color.Mix(Color.Hex(color), Color.Black, .5f));

    /// <summary>The kit theme, so text fields, menus and popovers share these surfaces.</summary>
    public KitTheme Kit => (Dark ? KitTheme.Dark : KitTheme.Light) with
    {
        Background = Page,
        Foreground = Text,
        Card = Page,
        CardForeground = Text,
        Popover = Raised,
        PopoverForeground = Text,
        Muted = Lane,
        MutedForeground = Faint,
        Accent = Menu,
        AccentForeground = Text,
        Border = Border,
        Input = Color.Hex(Dark ? 0x3a3a40u : 0xdcdce0u),
        Ring = Accent.Alpha(.55f),
        Overlay = Scrim,
        Primary = KitTheme.Brand(Accent, Dark),
        Danger = (Dark ? KitTheme.Dark : KitTheme.Light).Danger with
        {
            Text = Danger
        },
        Sidebar = Sidebar,
        SidebarForeground = Text,
        SidebarAccent = Page,
        SidebarBorder = Border,
        Switch = Color.Hex(Dark ? 0x3a3a40u : 0xd4d4d8u),
        Radius = Corners.Control,
        FontSize = TextSize.Body,
        Metrics = new(HeightMedium: 30)
    };
}
