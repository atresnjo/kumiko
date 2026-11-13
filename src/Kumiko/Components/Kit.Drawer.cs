using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

/// <summary>
/// Drawer placement. <paramref name="Size"/> is the width of a left or right drawer and the height of a top or bottom
/// one (default 400 or 320); <paramref name="Inset"/> keeps the panel off the window edges.
/// </summary>
public readonly record struct DrawerProps(
    Side Side = Side.Right,
    float? Size = null,
    string? Description = null,
    bool CloseOnBackdrop = true,
    bool ShowClose = true,
    float Inset = 8
);

public readonly partial struct Kit
{
    /// <summary>
    /// A modal panel docked to a window edge with a title, optional description and close button over a scrolling
    /// body. Call it only while open, add body content, then dispose. It slides in from its edge and back out when
    /// it is no longer built; Escape, the close button and (optionally) a backdrop click call
    /// <paramref name="onClose"/>. Focus moves into the drawer and Tab stays inside it.
    /// </summary>
    public KitScope Drawer(
        string title,
        Action onClose,
        DrawerProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var window = context.Size;
        var inset = MathF.Max(0, props.Inset);
        var across = props.Side is Side.Left or Side.Right;
        var room = MathF.Max(0, (across ? window.Width : window.Height) - inset * 2);
        var size = MathF.Min(props.Size ?? (across ? 400 : 320), room);
        var slide = props.Side switch
        {
            Side.Left => Transition.FromLeft,
            Side.Top => Transition.FromTop,
            Side.Bottom => Transition.FromBottom,
            _ => Transition.FromRight
        };
#pragma warning disable KUM001 // The returned KitScope disposes the overlay, panel and body.
        var overlay = context.Overlay(key.Value is null ? "drawer" : key).TabStop(false).Background(t.Overlay).Semantics(Role.Group)
            .Enter(Transition.Fade with
            {
                Motion = Motion.Gentle
            })
            .Exit(Transition.Fade);
        var close = context.Shortcut(Key.Escape, Modifiers.None);
        if (overlay.Control().Clicked && props.CloseOnBackdrop)
            close = true;
        var panel = context.Div(key: "panel")
            .Absolute()
            .Column()
            .Background(t.Popover)
            .Border(t.Border)
            .Rounded(t.RadiusLarge * 1.5f)
            .Clip()
            .TabStop(false)
            .Semantics(Role.Dialog, title)
            .Enter(slide)
            .Exit(slide with
            {
                Motion = Motion.Standard
            });
#pragma warning restore KUM001
        // Offsets are in layout coordinates, which mirror in right-to-left windows; the side is physical.
        var right = (props.Side == Side.Right) != context.RightToLeftWindow;
        var bottom = props.Side == Side.Bottom;
        if (across)
            panel.Offset(right ? window.Width - inset - size : inset, inset).Width(size).Height(MathF.Max(0, window.Height - inset * 2));
        else
            panel.Offset(inset, bottom ? window.Height - inset - size : inset).Width(MathF.Max(0, window.Width - inset * 2)).Height(size);
        if (t.Shadows)
            panel.Shadow(32, 12, Color.Black.Alpha(t.IsDark ? .5f : .18f));
        panel.Control();
        if (!context.FocusWithin(panel.Id))
            context.FocusNextControl();
        using (context.Div(key: "header").Row().FillWidth().Items(Align.Start).Gap(12).Padding(new Edges(20, 20, 16, 24)))
        {
            using (context.Div(key: "titles").Flex().Column().Gap(6).Padding(new Edges(4, 0, 0, 0)))
            {
                context.Text(title, Text(t.FontSize + 4, t.PopoverForeground, Weight.Semibold), key: "title");
                if (props.Description is { } description)
                    context.Text(description, Text(t.FontSize, t.MutedForeground) with
                    {
                        Wrap = true
                    }, key: "description");
            }
            if (props.ShowClose && IconButton("xmark", "Close", new(Appearance: Appearance.Outline, Size: ControlSize.Small), "close").Clicked)
                close = true;
        }
        using (context.Div(key: "rule").FillWidth().Height(1).Background(t.Border))
        {
        }
#pragma warning disable KUM001 // The returned KitScope disposes the body.
        var body = context.Div(key: "body").FillWidth().Grow().Scroll().Column().Gap(12).Padding(24);
#pragma warning restore KUM001
        if (close)
            onClose();
        return new(overlay, panel, body);
    }
}
