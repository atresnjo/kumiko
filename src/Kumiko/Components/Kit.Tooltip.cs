using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    static readonly TimeSpan TooltipDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Describes <paramref name="trigger"/> (from a control built just before) with a short hint. It shows after a
    /// hover delay or immediately on keyboard focus, hides on press, Escape or exit, never takes input, and is
    /// always the trigger's accessible description.
    /// </summary>
    public void Tooltip(
        Interaction trigger,
        string text,
        TooltipProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        context.AmendAccessibility(trigger.Id, a => a with
        {
            Description = text
        });
        var state = context.NodeState<TooltipState>(trigger.Id);
        var input = context.Input;
        // A shown tooltip stays while the pointer moves onto it (WCAG 1.4.13 hoverable).
        var onTip = state.Shown && context.RectOf(state.Panel) is { } tip && tip.Contains(input.Pointer);
        var hovered = trigger.Hovered || onTip;
        // A press hides the hover tooltip until the pointer returns; Escape hides it until hover or focus returns.
        if (trigger.Hovered && !state.Hovered)
            state.Pressed = state.Escaped = false;
        if (trigger.Focused && !state.Focused)
            state.Escaped = false;
        state.Hovered = trigger.Hovered;
        state.Focused = trigger.Focused;
        if (trigger.Hovered && (input.Pressed || input.Down))
            state.Pressed = true;
        if (!hovered)
            state.HoverSince = null;
        else if (state.HoverSince is null)
            state.HoverSince = context.Time;
        var delay = (props.Delay ?? TooltipDelay).TotalSeconds;
        var waited = state.HoverSince is { } since ? context.Time - since : 0;
        var due = hovered && waited >= delay && !state.Pressed;
        var visible = !state.Escaped && (due || trigger.Focused && context.FocusVisible);
        if (visible && context.Shortcut(Key.Escape, Modifiers.None))
        {
            state.Escaped = true;
            visible = false;
        }
        if (trigger.Hovered && !due && !state.Pressed && !state.Escaped)
            context.FrameIn(TimeSpan.FromSeconds(Math.Max(.001, delay - waited)));
        state.Shown = visible;
        if (!visible || trigger.Rect is not { } anchor)
            return;
        var t = Theme;
        using var layer = context.Overlay(key.Value is null ? $"tooltip.{trigger.Id}" : key, false);
        using var panel = context.Div(key: "panel")
            .Absolute()
            .MaxWidth(props.MaxWidth ?? 280)
            .Px(8)
            .Py(4)
            .Rounded(t.RadiusSmall)
            .Background(t.Primary.Background)
            .Semantics(Role.Tooltip, text);
        state.Panel = panel.Id;
        if (panel.PreviousRect is { } previous)
        {
            var window = context.Size;
            var (x, y) = Place(context.ToLayout(anchor), new(previous.Width, previous.Height), new(Side: props.Side ?? Side.Top, Align: PopoverAlign.Center, Offset: 4),
                window);
            panel.Offset(x, y);
        }
        else
        {
            panel.Opacity(0);
            context.FrameIn(TimeSpan.FromMilliseconds(1));
        }
        context.Text(text, Text(t.FontSize - 2, t.Primary.Foreground) with
        {
            Wrap = true
        }, key: "text", source, line);
    }

    sealed class TooltipState
    {
        public double? HoverSince;
        public bool Pressed;
        public bool Escaped;
        public bool Hovered;
        public bool Focused;
        public bool Shown;
        public ulong Panel;
    }
}
