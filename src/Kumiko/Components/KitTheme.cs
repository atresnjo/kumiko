using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>
/// A filled color role: solid background, readable foreground on it, and a text color readable on the page.
/// Hover and active states derive from the pair unless set explicitly.
/// </summary>
public sealed record ToneColors(Color Background, Color Foreground, Color Text)
{
    Color? hover, active;
    public Color Hover
    {
        get => hover ?? Color.Mix(Background, Foreground, .1f); init => hover = value;
    }
    public Color Active
    {
        get => active ?? Color.Mix(Background, Foreground, .18f); init => active = value;
    }
    /// <summary>Translucent tint used by outline buttons, tags and alerts; composes over any surface.</summary>
    public Color Soft(float strength = .1f) => Background.Alpha(strength);
}

/// <summary>
/// Semantic tokens for Kumiko.Components, after gpui-kit's shadcn-derived default theme. Customize with a
/// with expression; assign <see cref="Theme"/> to a window or context so built-in widgets match.
/// </summary>
public sealed partial record KitTheme
{
    public bool IsDark
    {
        get; init;
    }
    public Color Background { get; init; } = Color.White;
    public Color Foreground { get; init; } = Palette.Neutral950;
    public Color Card { get; init; } = Color.White;
    public Color CardForeground { get; init; } = Palette.Neutral950;
    public Color Popover { get; init; } = Color.White;
    public Color PopoverForeground { get; init; } = Palette.Neutral950;
    public Color Muted { get; init; } = Palette.Neutral100;
    public Color MutedForeground { get; init; } = Palette.Neutral500;
    /// <summary>Hover and highlighted-item surface (shadcn accent).</summary>
    public Color Accent { get; init; } = Palette.Neutral100;
    public Color AccentForeground { get; init; } = Palette.Neutral900;
    public Color Border { get; init; } = Palette.Neutral200;
    public Color Input { get; init; } = Palette.Neutral200;
    public Color Ring { get; init; } = Palette.Neutral400;
    /// <summary>Modal backdrop.</summary>
    public Color Overlay { get; init; } = Color.Black.Alpha(.32f);
    public Color Link { get; init; } = Palette.Neutral950;
    public Color LinkHover { get; init; } = Palette.Neutral700;
    public Color Selection { get; init; } = Color.Hex(0x55a0fc);

    public ToneColors Primary
    {
        get; init;
    } = new(Palette.Neutral900, Palette.Neutral50, Palette.Neutral900)
    {
        Hover = Palette.Neutral800,
        Active = Palette.Neutral950
    };
    public ToneColors Secondary
    {
        get; init;
    } = new(Palette.Neutral100, Palette.Neutral900, Palette.Neutral900)
    {
        Hover = Palette.Neutral200,
        Active = Palette.Neutral300
    };
    public ToneColors Danger { get; init; } = new(Color.Hex(0xdc2626), Palette.Neutral50, Color.Hex(0xb91c1c));
    public ToneColors Success { get; init; } = new(Color.Hex(0x15803d), Palette.Neutral50, Color.Hex(0x166534));
    public ToneColors Warning { get; init; } = new(Color.Hex(0xeab308), Color.Hex(0x422006), Color.Hex(0x854d0e));
    public ToneColors Info { get; init; } = new(Color.Hex(0x0e7490), Palette.Neutral50, Color.Hex(0x155e75));

    public Color Switch { get; init; } = Palette.Neutral300;
    public Color SwitchThumb { get; init; } = Color.White;
    public Color SliderBar { get; init; } = Palette.Neutral900;
    public Color SliderThumb { get; init; } = Color.White;
    public Color ProgressBar { get; init; } = Palette.Neutral900;
    public Color Skeleton { get; init; } = Palette.Neutral100;
    public Color TabBar { get; init; } = Palette.Neutral100;
    public Color TabActive { get; init; } = Color.White;
    public Color TabForeground { get; init; } = Palette.Neutral700;
    public Color TabActiveForeground { get; init; } = Palette.Neutral900;
    public Color Sidebar { get; init; } = Palette.Neutral50;
    public Color SidebarForeground { get; init; } = Palette.Neutral900;
    public Color SidebarAccent { get; init; } = Palette.Neutral200;
    public Color SidebarBorder { get; init; } = Palette.Neutral200;
    public Color ListHover { get; init; } = Palette.Neutral100;
    public Color Star { get; init; } = Color.Hex(0xf59e0b);

    /// <summary>Base corner radius (gpui-kit radius); small controls use half, surfaces use RadiusLarge.</summary>
    public float Radius { get; init; } = 6;
    float? radiusLarge;
    /// <summary>Dialogs, cards and notifications; follows Radius (+2, or square) unless set.</summary>
    public float RadiusLarge
    {
        get => radiusLarge ?? (Radius <= 0 ? 0 : Radius + 2); init => radiusLarge = value;
    }
    /// <summary>Control text size (text-sm).</summary>
    public float FontSize { get; init; } = 14;
    /// <summary>Draws elevation shadows on popovers, dialogs and raised controls.</summary>
    public bool Shadows { get; init; } = true;
    public string? Font
    {
        get; init;
    }
    public string? MonoFont
    {
        get; init;
    }
    public KitMetrics Metrics { get; init; } = new();

    /// <summary>Corner radius for a pill or circle, or square when the theme radius is zero.</summary>
    public float RadiusFull => Radius <= 0 ? 0 : 999;
    public float RadiusSmall => Radius / 2;

    /// <summary>gpui-kit Default Light (shadcn neutral).</summary>
    public static KitTheme Light { get; } = new();

    /// <summary>The kit form of <see cref="UI.Theme.HighContrast"/>.</summary>
    public static KitTheme HighContrast => Of(UI.Theme.HighContrast);

    /// <summary>The kit form of <see cref="UI.Theme.HighContrastDark"/>.</summary>
    public static KitTheme HighContrastDark => Of(UI.Theme.HighContrastDark);

    /// <summary>gpui-kit Default Dark (shadcn neutral).</summary>
    public static KitTheme Dark
    {
        get;
    } = new()
    {
        IsDark = true,
        Background = Palette.Neutral950,
        Foreground = Palette.Neutral50,
        Card = Palette.Neutral950,
        CardForeground = Palette.Neutral50,
        Popover = Color.Hex(0x111111),
        PopoverForeground = Palette.Neutral50,
        Muted = Palette.Neutral800,
        MutedForeground = Palette.Neutral400,
        Accent = Palette.Neutral800,
        AccentForeground = Palette.Neutral50,
        Border = Palette.Neutral800,
        Input = Color.Hex(0x2f2f2f),
        Ring = Palette.Neutral500,
        Overlay = Color.Black.Alpha(.6f),
        Link = Palette.Neutral50,
        LinkHover = Color.White,
        Selection = Color.Hex(0x1d4ed8),
        Primary = new(Palette.Neutral50, Palette.Neutral900, Palette.Neutral50)
        {
            Hover = Palette.Neutral100,
            Active = Palette.Neutral200
        },
        Secondary = new(Palette.Neutral800, Palette.Neutral50, Palette.Neutral50)
        {
            Hover = Color.Hex(0x303030),
            Active = Color.Hex(0x212121)
        },
        Danger = new(Color.Hex(0xf87171), Color.Hex(0x450a0a), Color.Hex(0xf87171)),
        Success = new(Color.Hex(0x4ade80), Color.Hex(0x052e16), Color.Hex(0x4ade80)),
        Warning = new(Color.Hex(0xfacc15), Color.Hex(0x422006), Color.Hex(0xfacc15)),
        Info = new(Color.Hex(0x22d3ee), Color.Hex(0x083344), Color.Hex(0x22d3ee)),
        Switch = Palette.Neutral700,
        SwitchThumb = Palette.Neutral50,
        SliderBar = Palette.Neutral50,
        SliderThumb = Palette.Neutral950,
        ProgressBar = Palette.Neutral100,
        Skeleton = Palette.Neutral900,
        TabBar = Palette.Neutral900,
        TabActive = Palette.Neutral950,
        TabForeground = Palette.Neutral300,
        TabActiveForeground = Palette.Neutral50,
        Sidebar = Palette.Neutral950,
        SidebarForeground = Palette.Neutral100,
        SidebarAccent = Palette.Neutral800,
        SidebarBorder = Palette.Neutral800,
        ListHover = Palette.Neutral900,
        Star = Color.Hex(0xfbbf24)
    };

    /// <summary>
    /// A primary role for a brand color, with a foreground chosen for contrast. Use with a with expression:
    /// <c>KitTheme.Light with { Primary = KitTheme.Brand(Color.Hex(0x7c3aed), false) }</c>.
    /// </summary>
    public static ToneColors Brand(Color color, bool dark)
    {
        var foreground = Contrast(color, Color.White) >= Contrast(color, Palette.Neutral950) ? Color.White : Palette.Neutral950;
        return new(color, foreground, dark ? Color.Mix(color, Color.White, .25f) : color)
        {
            Hover = Color.Mix(color, dark ? Color.White : Color.Black, .1f),
            Active = Color.Mix(color, dark ? Color.White : Color.Black, .2f)
        };
    }

    /// <summary>WCAG contrast ratio between two opaque colors.</summary>
    public static float Contrast(Color a, Color b)
    {
        var (x, y) = (Luminance(a), Luminance(b));
        return (MathF.Max(x, y) + .05f) / (MathF.Min(x, y) + .05f);
    }

    internal static float Luminance(Color c)
    {
        static float Channel(float v) => v <= .03928f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
        return .2126f * Channel(c.R) + .7152f * Channel(c.G) + .0722f * Channel(c.B);
    }
}

/// <summary>Control dimensions for each <see cref="ControlSize"/>, after gpui-kit's sizing scale.</summary>
public sealed record KitMetrics(
    float HeightXSmall = 20,
    float HeightSmall = 24,
    float HeightMedium = 32,
    float HeightLarge = 40,
    float PaddingXSmall = 4,
    float PaddingSmall = 8,
    float PaddingMedium = 12,
    float PaddingLarge = 16,
    float FocusWidth = 2,
    float DisabledOpacity = .5f,
    float DialogWidth = 440,
    float SidebarWidth = 232
)
{
    public float Height(ControlSize size) => size switch
    {
        ControlSize.XSmall => HeightXSmall,
        ControlSize.Small => HeightSmall,
        ControlSize.Large => HeightLarge,
        _ => HeightMedium
    };

    public float Padding(ControlSize size) => size switch
    {
        ControlSize.XSmall => PaddingXSmall,
        ControlSize.Small => PaddingSmall,
        ControlSize.Large => PaddingLarge,
        _ => PaddingMedium
    };
}

/// <summary>Tailwind neutral and accent swatches used by the default themes. Static members only.</summary>
public readonly struct Palette
{
    public static Color Neutral50 => Color.Hex(0xfafafa);
    public static Color Neutral100 => Color.Hex(0xf5f5f5);
    public static Color Neutral200 => Color.Hex(0xe5e5e5);
    public static Color Neutral300 => Color.Hex(0xd4d4d4);
    public static Color Neutral400 => Color.Hex(0xa3a3a3);
    public static Color Neutral500 => Color.Hex(0x737373);
    public static Color Neutral600 => Color.Hex(0x525252);
    public static Color Neutral700 => Color.Hex(0x404040);
    public static Color Neutral800 => Color.Hex(0x262626);
    public static Color Neutral900 => Color.Hex(0x171717);
    public static Color Neutral950 => Color.Hex(0x0a0a0a);
    public static Color Blue => Color.Hex(0x2563eb);
    public static Color Violet => Color.Hex(0x7c3aed);
    public static Color Green => Color.Hex(0x16a34a);
    public static Color Orange => Color.Hex(0xea580c);
    public static Color Rose => Color.Hex(0xe11d48);
}
