using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public sealed partial record KitTheme
{
    static readonly Lock gate = new();
    static readonly Dictionary<KitTheme, Theme> themes = new();
    static readonly ConditionalWeakTable<Theme, KitTheme> kits = new();
    const int MaxCachedThemes = 64;

    /// <summary>
    /// The core theme for this kit, so text fields, grids and menus match the components. Value-equal kits
    /// return the same instance; assigning it every frame does not reset font caches.
    /// </summary>
    public Theme Theme
    {
        get
        {
            lock (gate)
            {
                if (themes.TryGetValue(this, out var cached))
                    return cached;
                if (themes.Count >= MaxCachedThemes)
                    themes.Clear();
                var theme = CreateTheme();
                themes[this] = theme;
                kits.AddOrUpdate(theme, this);
                return theme;
            }
        }
    }

    /// <summary>The kit that created a core theme, or a kit derived from that theme's colors.</summary>
    public static KitTheme Of(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        lock (gate)
        {
            if (kits.TryGetValue(theme, out var kit))
                return kit;
            kit = Derive(theme);
            kits.AddOrUpdate(theme, kit);
            return kit;
        }
    }

    Theme CreateTheme()
    {
        var height = Metrics.HeightMedium;
        return new Theme
        {
            Dark = IsDark,
            Background = Background,
            Surface = Card,
            SurfaceHover = Accent,
            Text = Foreground,
            Muted = MutedForeground,
            Border = Border,
            Accent = Primary.Background,
            AccentHover = Primary.Hover,
            AccentText = Primary.Foreground,
            Track = Switch,
            Knob = SwitchThumb,
            Danger = Danger.Background,
            DangerText = Danger.Foreground,
            Success = Success.Background,
            Popover = Popover,
            PopoverText = PopoverForeground,
            Selection = Accent,
            SelectionText = AccentForeground,
            FocusRing = Ring,
            InputBorder = Input,
            Secondary = Secondary.Background,
            SecondaryText = Secondary.Foreground,
            ErrorSurface = Danger.Soft(.08f),
            Font = Font,
            MonoFont = MonoFont,
            Radius = new(Sm: RadiusSmall, Md: Radius, Lg: RadiusLarge, Xl: Radius * 2, Pill: RadiusFull),
            Controls = new(Height: height, RowHeight: height, HeaderHeight: height, MenuItemHeight: height - 4,
                FocusWidth: Metrics.FocusWidth, DisabledOpacity: Metrics.DisabledOpacity),
            Typography = new()
            {
                Caption = new(Size: FontSize - 2),
                Body = new(Size: FontSize),
                Label = new(Size: FontSize, Weight: Weight.Medium),
                Title = new(Size: FontSize + 4, Weight: Weight.Semibold),
                Heading = new(Size: FontSize + 10, Weight: Weight.Bold),
                Badge = new(Size: FontSize - 2, Weight: Weight.Medium),
                Keyboard = new(Size: FontSize - 2, Mono: true)
            }
        };
    }

    static KitTheme Derive(Theme t)
    {
        var defaults = t.Dark ? Dark : Light;
        return defaults with
        {
            IsDark = t.Dark,
            Background = t.Background,
            Foreground = t.Text,
            Card = t.Surface,
            CardForeground = t.Text,
            Popover = t.Popover,
            PopoverForeground = t.PopoverText,
            Muted = t.SurfaceHover,
            MutedForeground = t.Muted,
            Accent = t.Selection,
            AccentForeground = t.SelectionText,
            Border = t.Border,
            Input = t.InputBorder,
            Ring = t.FocusRing,
            Link = t.Text,
            Primary = new(t.Accent, t.AccentText, t.Accent)
            {
                Hover = t.AccentHover
            },
            Secondary = new(t.Secondary, t.SecondaryText, t.SecondaryText),
            Danger = new(t.Danger, t.DangerText, t.Danger),
            Success = defaults.Success with
            {
                Background = t.Success
            },
            Switch = t.Track,
            SwitchThumb = t.Knob,
            SliderBar = t.Accent,
            ProgressBar = t.Accent,
            TabActive = t.Surface,
            TabActiveForeground = t.Text,
            Sidebar = t.Surface,
            SidebarForeground = t.Text,
            SidebarAccent = t.SurfaceHover,
            SidebarBorder = t.Border,
            ListHover = t.SurfaceHover,
            Radius = t.Radius.Md,
            RadiusLarge = t.Radius.Lg,
            FontSize = t.Typography.Body.Size,
            Font = t.Font,
            MonoFont = t.MonoFont,
            Metrics = Scaled(defaults.Metrics, t.Typography.Body.Size / 14) with
            {
                HeightMedium = t.Controls.Height,
                FocusWidth = t.Controls.FocusWidth,
                DisabledOpacity = t.Controls.DisabledOpacity
            }
        };
    }

    /// <summary>Grows control sizes with enlarged system text so labels keep fitting.</summary>
    static KitMetrics Scaled(KitMetrics metrics, float scale)
    {
        if (scale <= 1.01f || !float.IsFinite(scale))
            return metrics;
        float Size(float value) => MathF.Round(value * scale);
        return metrics with
        {
            HeightXSmall = Size(metrics.HeightXSmall),
            HeightSmall = Size(metrics.HeightSmall),
            HeightLarge = Size(metrics.HeightLarge),
            PaddingXSmall = Size(metrics.PaddingXSmall),
            PaddingSmall = Size(metrics.PaddingSmall),
            PaddingMedium = Size(metrics.PaddingMedium),
            PaddingLarge = Size(metrics.PaddingLarge),
            DialogWidth = Size(metrics.DialogWidth),
            SidebarWidth = Size(metrics.SidebarWidth)
        };
    }
}
