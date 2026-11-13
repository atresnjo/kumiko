using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    /// <summary>
    /// A button that opens a <see cref="Calendar"/> popover. The model owns <paramref name="open"/>; choosing a
    /// date reports it, closes the popover and returns focus to the trigger. Returns true when it reported a change.
    /// </summary>
    public bool DatePicker(
        string label,
        DateOnly? value,
        bool open,
        Action<DateOnly?> changed,
        Action<bool> openChanged,
        DatePickerProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var text = value?.ToString(props.Format ?? "d", Gregorian(context.Culture));
        using var root = Box(key, source, line).Row().ItemsCenter().Gap(4);
        var state = context.NodeState<PickerState>(root.Id);
        var hit = PickerTrigger(label, text, props.Placeholder ?? context.Strings.PickDate, "calendar", open, props.Width, props.Disabled,
            state);
        if (Toggled(hit, open, props.Disabled))
            openChanged(!open);
        var opening = open && !state.WasOpen;
        state.WasOpen = open;
        var reported = false;
        if (props.Clearable && value is not null && !props.Disabled &&
            IconButton("xmark", context.Strings.Clear, new(Appearance: Appearance.Ghost, Size: ControlSize.Small), "clear").Clicked)
        {
            changed(null);
            reported = true;
        }
        if (!open)
            return reported;
        void Close()
        {
            state.Refocus = true;
            openChanged(false);
        }
        var width = DayCell * 7 + DayGap * 6 + 24;
        using (Popover(hit.Rect, Close, new(Name: label, Width: width, Padding: 12), "popover"))
        {
            reported |= CalendarCore(label, value, date =>
            {
                changed(date);
                Close();
            }, props.Calendar, opening, "calendar", source, line);
        }
        return reported;
    }

    /// <summary>
    /// A button that opens a list of times in the window culture's short-time format. The model owns
    /// <paramref name="open"/>. Returns true when it reported a change.
    /// </summary>
    public bool TimePicker(
        string label,
        TimeOnly? value,
        bool open,
        Action<TimeOnly?> changed,
        Action<bool> openChanged,
        TimePickerProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    )
    {
        var t = Theme;
        var text = value?.ToString("t", context.Culture);
        using var root = Box(key, source, line).Row().ItemsCenter();
        var state = context.NodeState<PickerState>(root.Id);
        var hit = PickerTrigger(label, text, props.Placeholder ?? context.Strings.PickTime, "clock", open, props.Width, props.Disabled,
            state);
        if (Toggled(hit, open, props.Disabled))
            openChanged(!open);
        var opening = open && !state.WasOpen;
        state.WasOpen = open;
        if (!open)
            return false;
        void Close()
        {
            state.Refocus = true;
            openChanged(false);
        }
        var times = Times(props);
        var current = value is { } v ? times.IndexOf(v) : -1;
        var row = t.Metrics.HeightMedium;
        var reported = false;
        using (Popover(hit.Rect, Close, new(Name: label, Width: MathF.Max(160, hit.Rect?.Width ?? 0)), "popover"))
        using (var list = context.Div(key: "times").FillWidth().MaxHeight(row * 7.5f).Scroll())
        {
            if (opening)
            {
                state.FocusPending = true;
                if (current >= 0)
                    Reveal(list.Id, current * row - row * 3);
            }
            for (var i = 0; i < times.Count; i++)
            {
                var time = times[i];
                var name = time.ToString("t", context.Culture);
                var itemKey = time.Ticks.ToString(context.Culture);
                if (state.FocusPending && i == Math.Max(0, current))
                {
                    if (context.Focus == context.NextNodeId(itemKey))
                        state.FocusPending = false;
                    else
                        context.FocusNextControl();
                }
                if (!MenuItem(name, new(Checked: i == current), key: itemKey).Clicked)
                    continue;
                changed(time);
                Close();
                reported = true;
            }
        }
        return reported;
    }

    Interaction PickerTrigger(string label, string? text, string placeholder, string icon, bool open, float? width, bool disabled,
        PickerState state)
    {
        var t = Theme;
        using var trigger = context.Div(key: "trigger")
            .Row()
            .ItemsCenter()
            .Gap(8)
            .Height(t.Metrics.HeightMedium)
            .Width(width ?? 220)
            .Px(t.Metrics.PaddingMedium)
            .Rounded(t.Radius)
            .Background(t.Background)
            .Border(t.Input)
            .Semantics(Role.Button, label, text ?? placeholder)
            .Accessibility(new(Expanded: open));
        if (state.Refocus && !open)
        {
            context.FocusNextControl();
            state.Refocus = false;
        }
        var hit = trigger.Control(!disabled);
        Ring(trigger, hit, t);
        context.Icon(icon, 16, t.MutedForeground, key: "icon");
        using (context.Div(key: "value").Flex().Clip())
            context.Text(text ?? placeholder, Text(t.FontSize, text is null ? t.MutedForeground : t.Foreground), key: "text");
        context.Icon("chevron.down", 14, t.MutedForeground, key: "chevron");
        if (disabled)
            trigger.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        return hit;
    }

    /// <summary>A click, Enter/Space, or Alt+Down on a closed trigger toggles its popup.</summary>
    bool Toggled(Interaction hit, bool open, bool disabled)
    {
        if (disabled)
            return false;
        if (hit.Clicked)
            return true;
        if (open || !hit.Focused || context.Input.Key != Key.Down || (context.Input.Modifiers & Modifiers.Alt) == 0)
            return false;
        context.Input.Key = Key.None;
        return true;
    }

    void Reveal(ulong scroller, float offset)
    {
        if (!context.Scrolls.TryGetValue(scroller, out var scroll))
            context.Scrolls[scroller] = scroll = new();
        scroll.Offset = MathF.Max(0, offset);
        scroll.Follow = false;
    }

    static List<TimeOnly> Times(TimePickerProps props)
    {
        var step = TimeSpan.FromMinutes(props.StepMinutes > 0 ? props.StepMinutes : 30);
        var first = props.Min ?? TimeOnly.MinValue;
        var last = props.Max ?? TimeOnly.MaxValue;
        var result = new List<TimeOnly>();
        for (var span = first.ToTimeSpan(); span <= last.ToTimeSpan() && span < TimeSpan.FromDays(1); span += step)
            result.Add(TimeOnly.FromTimeSpan(span));
        return result;
    }

    sealed class PickerState
    {
        public bool Refocus;
        public bool WasOpen;
        public bool FocusPending;
    }
}
