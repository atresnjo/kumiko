using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>
    /// A modal dialog. Call it only while the dialog is open, add body content and a <see cref="Footer"/>, then
    /// dispose. Escape, the close button and (optionally) a backdrop click call <paramref name="onClose"/>.
    /// Focus moves into the dialog and Tab stays inside it.
    /// </summary>
    public KitScope Dialog(
        string title,
        Action onClose,
        DialogProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
#pragma warning disable KUM001 // The returned KitScope disposes the overlay and panel.
        var overlay = context.Overlay(key.Value is null ? "dialog" : key).Center().TabStop(false).Background(t.Overlay)
            .Semantics(Role.Group).Enter(Transition.Fade).Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
        var close = context.Shortcut(Key.Escape, Modifiers.None);
        var backdrop = overlay.Control();
        if (backdrop.Clicked && props.CloseOnBackdrop)
            close = true;
        var width = MathF.Min(props.Width ?? t.Metrics.DialogWidth, MathF.Max(0, context.Size.Width - 32));
        var panel = context.Div(key: "panel")
            .Column()
            .Width(width)
            .Padding(24)
            .Gap(16)
            .Background(t.Popover)
            .Border(t.Border)
            .Rounded(t.RadiusLarge * 1.5f)
            .TabStop(false)
            .Semantics(Role.Group, title)
            .Enter(Transition.Pop)
            .Exit(Transition.Pop with
            {
                Motion = Motion.Quick
            });
#pragma warning restore KUM001
        if (t.Shadows)
            panel.Shadow(32, 12, Color.Black.Alpha(t.IsDark ? .5f : .18f));
        panel.Control();
        if (!context.FocusWithin(panel.Id))
            context.FocusNextControl();
        using (context.Div(key: "header").Row().FillWidth().Items(Align.Start).Gap(12))
        {
            using (context.Div(key: "titles").Flex().Column().Gap(6))
            {
                context.Text(title, Text(t.FontSize + 4, t.PopoverForeground, Weight.Semibold), key: "title");
                if (props.Description is { } description)
                    context.Text(description, Text(t.FontSize, t.MutedForeground) with
                    {
                        Wrap = true
                    }, key: "description");
            }
            if (props.ShowClose && IconButton("xmark", "Close", new(Appearance: Appearance.Ghost, Size: ControlSize.Small), "close").Clicked)
                close = true;
        }
        if (close)
            onClose();
        return new(overlay, panel);
    }

    /// <summary>A toast-style message card. Its lifetime belongs to the application.</summary>
    public void Notification(
        string title,
        string? description = null,
        Tone tone = Tone.Default,
        Action? onClose = null,
        float width = 360,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var colors = Colors(tone, t);
        using var card = Box(key, source, line)
            .Row()
            .Width(width)
            .Items(Align.Start)
            .Gap(12)
            .Padding(16)
            .Background(t.Popover)
            .Border(t.Border)
            .Rounded(t.RadiusLarge)
            .Semantics(Role.Alert, title);
        if (t.Shadows)
            card.Shadow(16, 6, Color.Black.Alpha(t.IsDark ? .4f : .1f));
        if (tone != Tone.Default)
        {
            var icon = tone switch
            {
                Tone.Success => "checkmark.circle.fill",
                Tone.Warning => "exclamationmark.triangle",
                Tone.Danger => "xmark.octagon",
                _ => "info.circle"
            };
            context.Icon(icon, 18, colors.Text, key: "icon");
        }
        using (context.Div(key: "text").Flex().Column().Gap(4))
        {
            context.Text(title, Text(t.FontSize, t.PopoverForeground, Weight.Semibold), key: "title");
            if (description is not null)
                context.Text(description, Text(t.FontSize, t.MutedForeground) with
                {
                    Wrap = true
                }, key: "description");
        }
        if (onClose is not null && IconButton("xmark", "Dismiss", new(Appearance: Appearance.Ghost, Size: ControlSize.XSmall), "close")
            .Clicked)
            onClose();
    }

    /// <summary>A determinate progress bar for a fraction from zero to one.</summary>
    public void Progress(
        float fraction,
        ProgressProps props = default,
        string? name = null,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var value = Clamp01(fraction);
        var color = props.Tone == Tone.Default ? t.ProgressBar : Colors(props.Tone, t).Background;
        var height = props.Size switch
        {
            ControlSize.XSmall => 4f,
            ControlSize.Small => 6f,
            ControlSize.Large => 10f,
            _ => 8f
        };
        using var track = Box(key, source, line).Height(height).Rounded(t.RadiusFull).Background(color.Alpha(.2f)).Clip();
        if (props.Width is { } width)
            track.Width(width);
        else
            track.FillWidth();
        if (name is not null)
            track.Semantics(Role.Text, name, $"{MathF.Round(value * 100)}%");
        using (context.Div(key: "indicator").Width(Length.Percent(value * 100)).Height(height).Rounded(t.RadiusFull).Background(color))
        {
        }
    }

    /// <summary>Star rating; each star is a button named "N stars".</summary>
    public bool Rating(
        string name,
        int value,
        Action<int> changed,
        int max = 5,
        float size = 20,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        const float gap = 2;
        // Each star keeps a 24 px pointer target even when drawn smaller (WCAG 2.5.8).
        var cell = MathF.Max(size, Context.MinimumTargetSize);
        using var row = Box(key, source, line).Row().ItemsCenter().Gap(gap).Semantics(Role.Group, name);
        var preview = 0;
        if (row.PreviousRect is { } r && r.Contains(context.Input.Pointer))
        {
            var along = context.IsRightToLeft() ? r.Right - context.Input.Pointer.X : context.Input.Pointer.X - r.X;
            preview = Math.Clamp((int)(along / (cell + gap)) + 1, 1, max);
        }
        int target = 0;
        for (int i = 1; i <= max; i++)
        {
            using var star = context.Div(key: (ulong)i).Width(cell).Height(cell).Center().Rounded(t.RadiusSmall)
                .Semantics(Role.Button, i == 1 ? "1 star" : $"{i} stars");
            var hit = star.Control();
            Ring(star, hit, t);
            Hand(hit);
            var lit = preview > 0 ? i <= preview : i <= value;
            context.Icon("star.fill", size, lit ? t.Star : t.IsDark ? Palette.Neutral700 : Palette.Neutral300, key: "icon");
            if (hit.Clicked)
                target = i;
        }
        if (target == 0 || target == value)
            return false;
        changed(target);
        return true;
    }
}
