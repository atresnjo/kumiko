using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>The side of the anchor a popover opens on; it flips when that side lacks room.</summary>
public enum Side
{
    Bottom,
    Top,
    Right,
    Left
}

/// <summary>Alignment along the anchor edge.</summary>
public enum PopoverAlign
{
    Start,
    Center,
    End
}

/// <summary>
/// Popover placement. Width defaults to the anchor's width when <paramref name="MatchAnchorWidth"/> is set,
/// otherwise 240. <paramref name="Name"/> labels the popup for assistive technology.
/// </summary>
public readonly record struct PopoverProps(
    Side Side = Side.Bottom,
    PopoverAlign Align = PopoverAlign.Start,
    float? Width = null,
    float Offset = 6,
    string? Name = null,
    float Padding = 4,
    bool MatchAnchorWidth = false
);

public readonly record struct MenuItemProps(string? Icon = null, string? Shortcut = null, bool Destructive = false, bool Disabled = false,
    bool Checked = false);

public readonly partial struct Kit
{
    static readonly ConditionalWeakTable<Context, MenuChain> menus = new();
    const float ScreenMargin = 8;

    /// <summary>
    /// A floating panel anchored to a trigger's rect (for example <c>Interaction.Rect</c>). Call it right after the
    /// trigger while open, add content or <see cref="MenuItem"/>s, then dispose. Escape and outside clicks call
    /// <paramref name="onClose"/>; focus moves into the panel and Tab stays inside it.
    /// </summary>
    public KitScope Popover(
        Rect? anchor,
        Action onClose,
        PopoverProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var window = context.Size;
        var target = anchor is { } trigger ? context.ToLayout(trigger) : new Rect(window.Width / 2, window.Height / 3, 0, 0);
        var width = MathF.Min(props.Width ?? (props.MatchAnchorWidth ? target.Width : 240), window.Width - ScreenMargin * 2);
#pragma warning disable KUM001 // The returned KitScope disposes the overlay and panel.
        var overlay = context.Overlay(key.Value is null ? "popover" : key).TabStop(false).Semantics(Role.Group);
        var close = context.Shortcut(Key.Escape, Modifiers.None);
        if (overlay.Control().Clicked)
            close = true;
        var panel = context.Div(key: "panel")
            .Absolute()
            .Column()
            .Width(width)
            .Padding(props.Padding)
            .Gap(2)
            .Background(t.Popover)
            .Border(t.Border)
            .Rounded(t.RadiusLarge)
            .TabStop(false)
            .Semantics(Role.Group, props.Name)
            .Enter(Transition.Pop with
            {
                Motion = Motion.Quick
            })
            .Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
#pragma warning restore KUM001
        if (t.Shadows)
            panel.Shadow(16, 6, Color.Black.Alpha(t.IsDark ? .45f : .12f));
        if (panel.PreviousRect is { } previous)
        {
            var (x, y) = Place(target, new(width, previous.Height), props, window);
            panel.Offset(x, y);
        }
        else
        {
            // The first frame measures the panel; it appears once its height is known.
            panel.Opacity(0);
            context.FrameIn(TimeSpan.FromMilliseconds(1));
        }
        panel.Control();
        if (!context.FocusWithin(panel.Id))
            context.FocusNextControl();
        var chain = menus.GetOrCreateValue(context);
        chain.Frame = context.FrameId;
        chain.Previous = 0;
        if (close)
            onClose();
        return new(overlay, panel);
    }

    /// <summary>Top-left corner for a panel beside <paramref name="anchor"/>, flipped and kept on screen.</summary>
    static (float X, float Y) Place(Rect anchor, Size panel, PopoverProps props, Size window)
    {
        var gap = props.Offset;
        var side = props.Side;
        if (side == Side.Bottom && anchor.Bottom + gap + panel.Height > window.Height - ScreenMargin && anchor.Y - gap - panel.Height >= ScreenMargin)
            side = Side.Top;
        else if (side == Side.Top && anchor.Y - gap - panel.Height < ScreenMargin && anchor.Bottom + gap + panel.Height <= window.Height - ScreenMargin)
            side = Side.Bottom;
        else if (side == Side.Right && anchor.Right + gap + panel.Width > window.Width - ScreenMargin && anchor.X - gap - panel.Width >= ScreenMargin)
            side = Side.Left;
        else if (side == Side.Left && anchor.X - gap - panel.Width < ScreenMargin && anchor.Right + gap + panel.Width <= window.Width - ScreenMargin)
            side = Side.Right;
        float Along(float start, float length, float size) => props.Align switch
        {
            PopoverAlign.Center => start + (length - size) / 2,
            PopoverAlign.End => start + length - size,
            _ => start
        };
        var (x, y) = side switch
        {
            Side.Top => (Along(anchor.X, anchor.Width, panel.Width), anchor.Y - gap - panel.Height),
            Side.Right => (anchor.Right + gap, Along(anchor.Y, anchor.Height, panel.Height)),
            Side.Left => (anchor.X - gap - panel.Width, Along(anchor.Y, anchor.Height, panel.Height)),
            _ => (Along(anchor.X, anchor.Width, panel.Width), anchor.Bottom + gap)
        };
        x = Math.Clamp(x, ScreenMargin, MathF.Max(ScreenMargin, window.Width - panel.Width - ScreenMargin));
        y = Math.Clamp(y, ScreenMargin, MathF.Max(ScreenMargin, window.Height - panel.Height - ScreenMargin));
        return (x, y);
    }

    /// <summary>
    /// A menu row for popovers. Up and Down move between items; Enter, Space or a click activates it.
    /// </summary>
    public Interaction MenuItem(
        string label,
        MenuItemProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
        => MenuItemCore(label, props, key, source, line);

    Interaction MenuItemCore(string label, MenuItemProps props, NodeKey key, string source, int line,
        CommandItem? command = null, Action<Context, CommandItem, CommandItemState>? renderItem = null, bool suggested = false)
    {
        var t = Theme;
        using var item = Box(key, source, line)
            .Row()
            .FillWidth()
            .ItemsCenter()
            .Gap(8)
            .Height(t.Metrics.HeightMedium)
            .Px(8)
            .Rounded(MathF.Max(0, t.Radius - 2))
            .Semantics(Role.MenuItem, label, command?.Description, isChecked: props.Checked);
        if (renderItem is not null)
            item.Height(Length.Auto).Px(0).Gap(0);
        var hit = item.Control(!props.Disabled);
        var chain = menus.GetOrCreateValue(context);
        if (chain.Frame != context.FrameId)
        {
            chain.Frame = context.FrameId;
            chain.Previous = 0;
        }
        if (hit.Focused && context.Input.Key == Key.Down)
        {
            context.Input.Key = Key.None;
            context.FocusNextControl();
        }
        else if (hit.Focused && context.Input.Key == Key.Up && chain.Previous != 0)
        {
            context.Input.Key = Key.None;
            context.FocusControl(chain.Previous);
        }
        if (!props.Disabled)
            chain.Previous = item.Id;
        var active = !props.Disabled && (context.MenuHighlight(hit) || suggested);
        var danger = props.Destructive;
        if (active)
            item.Background(danger ? t.Danger.Soft(.12f) : t.Accent);
        if (props.Disabled)
            item.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        var foreground = danger ? t.Danger.Text : active ? t.AccentForeground : t.PopoverForeground;
        if (renderItem is not null && command is not null)
            context.RenderContent(renderItem, command, new(props.Checked, hit.Hovered && !props.Disabled, active, context.Focus == item.Id, props.Disabled, foreground));
        else
        {
            if (props.Icon is { } icon)
                context.Icon(icon, 16, danger ? foreground : active ? foreground : t.MutedForeground, key: "icon");
            using (context.Div(key: "label").Flex())
                context.Text(label, Text(t.FontSize, foreground), key: "text");
            if (props.Checked)
                context.Icon("checkmark", 14, foreground, key: "check");
            if (props.Shortcut is { } shortcut)
                context.Text(shortcut, Text(t.FontSize - 2, Color.Mix(t.MutedForeground, t.PopoverForeground, .25f)), key: "shortcut");
        }
        return hit;
    }

    /// <summary>
    /// Whether a custom row inside a <see cref="Popover"/> paints as highlighted, as <see cref="MenuItem"/> does.
    /// Moving the pointer onto the row focuses it, so only one row is highlighted: the pointer's or the keyboard's.
    /// </summary>
    public bool Highlighted(Interaction item) => context.MenuHighlight(item);

    /// <summary>A muted heading for a group of menu items.</summary>
    public void MenuLabel(string text, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        using (Box(key, source, line).FillWidth().Padding(new Edges(6, 8, 4, 8)))
            context.Text(text, Text(t.FontSize - 2, Color.Mix(t.MutedForeground, t.PopoverForeground, .25f), Weight.Medium), key: "text");
    }

    /// <summary>A rule between groups of menu items.</summary>
    public void MenuSeparator(NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var t = Theme;
        using (Box(key, source, line).FillWidth().Py(4))
        using (context.Div(key: "line").FillWidth().Height(1).Background(t.Border))
        {
        }
    }

    sealed class MenuChain
    {
        public long Frame = -1;
        public ulong Previous;
    }
}
