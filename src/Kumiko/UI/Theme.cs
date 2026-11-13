namespace Kumiko.UI;
/// <summary>
/// Immutable colors and design token scales shared by widgets and custom views. Use with expressions to
/// customize.
/// </summary>
public sealed record Theme
{
    public SpaceScale Space { get; init; } = new();
    public RadiusScale Radius { get; init; } = new();
    public TypographyScale Typography { get; init; } = new();
    public ControlMetrics Controls { get; init; } = new(DisabledOpacity: .4f);
    Color? popover, popoverText, selection, selectionText, focusRing, inputBorder, dangerText, errorSurface, secondary, secondaryText;
    public Color Popover
    {
        get => popover ?? Surface; init => popover = value;
    }
    public Color PopoverText
    {
        get => popoverText ?? Text; init => popoverText = value;
    }
    public Color Selection
    {
        get => selection ?? SurfaceHover; init => selection = value;
    }
    public Color SelectionText
    {
        get => selectionText ?? Text; init => selectionText = value;
    }
    public Color FocusRing
    {
        get => focusRing ?? Accent; init => focusRing = value;
    }
    public Color InputBorder
    {
        get => inputBorder ?? Border; init => inputBorder = value;
    }
    public Color DangerText
    {
        get => dangerText ?? Color.White; init => dangerText = value;
    }
    public Color ErrorSurface
    {
        get => errorSurface ?? Color.Mix(Background, Danger, .08f); init => errorSurface = value;
    }
    public Color Secondary
    {
        get => secondary ?? SurfaceHover; init => secondary = value;
    }
    public Color SecondaryText
    {
        get => secondaryText ?? Text; init => secondaryText = value;
    }
    public bool Dark
    {
        get; init;
    }
    public Color Background { get; init; } = Color.Hex(0xf5f5f7);
    public Color Surface { get; init; } = Color.White;
    public Color SurfaceHover { get; init; } = Color.Hex(0xececf0);
    public Color Text { get; init; } = Color.Hex(0x1d1d1f);
    public Color Muted { get; init; } = Color.Hex(0x6e6e73);
    public Color Border { get; init; } = Color.Hex(0xd2d2d7);
    public Color Accent { get; init; } = Color.Hex(0x0a84ff);
    public Color AccentHover { get; init; } = Color.Hex(0x0071e3);
    public Color AccentText { get; init; } = Color.White;
    public Color Track { get; init; } = Color.Hex(0xd2d2d7);
    public Color Knob { get; init; } = Color.White;
    public Color Danger { get; init; } = Color.Hex(0xff453a);
    public Color Success { get; init; } = Color.Hex(0x30d158);
    public string? Font
    {
        get; init;
    }
    public string? MonoFont
    {
        get; init;
    }
    public static Theme Light { get; } = new();
    /// <summary>Neutral shadcn-style semantic palette for native controls.</summary>
    public static Theme ShadcnLight
    {
        get;
    } = new()
    {
        Background = Color.White,
        Surface = Color.White,
        SurfaceHover = Color.Hex(0xf4f4f5),
        Text = Color.Hex(0x18181b),
        Muted = Color.Hex(0x71717a),
        Border = Color.Hex(0xe4e4e7),
        Accent = Color.Hex(0x18181b),
        AccentHover = Color.Hex(0x27272a),
        AccentText = Color.Hex(0xfafafa),
        Track = Color.Hex(0xe4e4e7),
        Danger = Color.Hex(0xdc2626),
        Controls = new(),
        FocusRing = Color.Hex(0x71717a),
        Radius = new(Sm: 4, Md: 6, Lg: 8, Xl: 12)
    };
    public static Theme ShadcnDark
    {
        get;
    } = ShadcnLight with
    {
        Dark = true,
        Background = Color.Hex(0x09090b),
        Surface = Color.Hex(0x09090b),
        SurfaceHover = Color.Hex(0x27272a),
        Text = Color.Hex(0xfafafa),
        Muted = Color.Hex(0xa1a1aa),
        Border = Color.Hex(0x3f3f46),
        Accent = Color.Hex(0xfafafa),
        AccentHover = Color.Hex(0xe4e4e7),
        AccentText = Color.Hex(0x18181b),
        Track = Color.Hex(0x3f3f46),
        Knob = Color.Hex(0x18181b),
        Danger = Color.Hex(0xf87171),
        DangerText = Color.Hex(0x450a0a),
        FocusRing = Color.Hex(0x71717a)
    };
    public static Theme DarkTheme
    {
        get;
    } = new()
    {
        Dark = true,
        Background = Color.Hex(0x1c1c1e),
        Surface = Color.Hex(0x2c2c2e),
        SurfaceHover = Color.Hex(0x3a3a3c),
        Text = Color.Hex(0xf5f5f7),
        Muted = Color.Hex(0x98989d),
        Border = Color.Hex(0x48484a),
        Track = Color.Hex(0x48484a)
    };

    /// <summary>
    /// Black on white with 7:1 text contrast, solid borders and a 3 px focus ring, for users who need
    /// high contrast. <see cref="Desktop.Appearance.Theme"/> selects it when the system asks for more contrast.
    /// </summary>
    public static Theme HighContrast
    {
        get;
    } = new()
    {
        Background = Color.White,
        Surface = Color.White,
        SurfaceHover = Color.Hex(0xe8e8e8),
        Text = Color.Black,
        Muted = Color.Hex(0x3d3d3d),
        Border = Color.Black,
        Accent = Color.Hex(0x00007a),
        AccentHover = Color.Hex(0x000050),
        AccentText = Color.White,
        Track = Color.Hex(0x545454),
        Knob = Color.White,
        Danger = Color.Hex(0x9e0000),
        Success = Color.Hex(0x005700),
        Selection = Color.Hex(0x00007a),
        SelectionText = Color.White,
        FocusRing = Color.Black,
        Controls = new(FocusWidth: 3, DisabledOpacity: .6f)
    };

    /// <summary>White and yellow on black with 7:1 text contrast, the dark counterpart of <see cref="HighContrast"/>.</summary>
    public static Theme HighContrastDark
    {
        get;
    } = new()
    {
        Dark = true,
        Background = Color.Black,
        Surface = Color.Black,
        SurfaceHover = Color.Hex(0x262626),
        Text = Color.White,
        Muted = Color.Hex(0xd4d4d4),
        Border = Color.White,
        Accent = Color.Hex(0xffff00),
        AccentHover = Color.Hex(0xffff80),
        AccentText = Color.Black,
        Track = Color.Hex(0xb3b3b3),
        Knob = Color.Black,
        Danger = Color.Hex(0xff8080),
        DangerText = Color.Black,
        Success = Color.Hex(0x5ce65c),
        Selection = Color.Hex(0x00ffff),
        SelectionText = Color.Black,
        FocusRing = Color.Hex(0xffff00),
        Controls = new(FocusWidth: 3, DisabledOpacity: .6f)
    };

    /// <summary>
    /// Scales every typography token and control dimension, for example by the system text size
    /// (<see cref="Desktop.Appearance.TextScale"/>). Spacing and radii are unchanged.
    /// </summary>
    public Theme WithTextScale(float factor)
    {
        if (!float.IsFinite(factor) || factor <= 0)
            throw new ArgumentOutOfRangeException(nameof(factor));
        TextStyle Text(TextStyle style) => style with
        {
            Size = style.Size * factor
        };
        float Size(float value) => MathF.Round(value * factor);
        return this with
        {
            Typography = Typography with
            {
                Caption = Text(Typography.Caption),
                Body = Text(Typography.Body),
                Label = Text(Typography.Label),
                Title = Text(Typography.Title),
                Heading = Text(Typography.Heading),
                Badge = Text(Typography.Badge),
                Keyboard = Text(Typography.Keyboard)
            },
            Controls = Controls with
            {
                Height = Size(Controls.Height),
                RowHeight = Size(Controls.RowHeight),
                HeaderHeight = Size(Controls.HeaderHeight),
                MenuItemHeight = Size(Controls.MenuItemHeight),
                PopupWidth = Size(Controls.PopupWidth)
            }
        };
    }
}
