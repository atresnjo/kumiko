using System.Globalization;
using System.Runtime.CompilerServices;
using Kumiko.UI;

namespace Kumiko.Components;

public readonly partial struct Kit
{
    const float DayCell = 36;
    const float DayGap = 2;

    /// <summary>
    /// A month calendar in the window culture. Arrows move by day and week, Page Up/Down by month (Shift for
    /// years), Home/End to the week's ends; Enter, Space or a click selects. Returns true when it reported a change.
    /// </summary>
    public bool Calendar(
        string name,
        DateOnly? value,
        Action<DateOnly> changed,
        CalendarProps props = default,
        NodeKey key = default,
        [CallerFilePath] string source = "",
        [CallerLineNumber] int line = 0
    ) => CalendarCore(name, value, changed, props, false, key, source, line);

    /// <summary><paramref name="restart"/> shows and focuses the value (or today) again, as when a popup opens.</summary>
    bool CalendarCore(string name, DateOnly? value, Action<DateOnly> changed, CalendarProps props, bool restart, NodeKey key,
        string source, int line)
    {
        var t = Theme;
        var culture = Gregorian(context.Culture);
        var format = culture.DateTimeFormat;
        var today = props.Today ?? DateOnly.FromDateTime(DateTime.Now);
        using var root = Box(key, source, line).Column().Gap(8).Semantics(Role.Group, name);
        var state = context.NodeState<CalendarState>(root.Id);
        if (restart || !state.Started || state.Value != value)
        {
            var origin = ClampDate(value ?? today, props);
            state.Focus = origin;
            state.Month = new(origin.Year, origin.Month, 1);
            state.FocusPending = restart;
            state.Started = true;
            state.Value = value;
        }
        var month = state.Month;
        var first = format.FirstDayOfWeek;
        var gridWidth = DayCell * 7 + DayGap * 6;
        using (context.Div(key: "header").Row().Width(gridWidth).ItemsCenter())
        {
            var earliest = props.Min is { } min ? new DateOnly(min.Year, min.Month, 1) : DateOnly.MinValue;
            var latest = props.Max is { } max ? new DateOnly(max.Year, max.Month, 1) : DateOnly.MaxValue;
            if (MonthButton("chevron.left", context.Strings.PreviousMonth, month <= earliest, "previous"))
                ShowMonth(state, month.AddMonths(-1));
            using (context.Div(key: "title").Flex().ItemsCenter())
                context.Text(month.ToString(format.YearMonthPattern, culture), Text(t.FontSize, t.Foreground, Weight.Medium), key: "text");
            if (MonthButton("chevron.right", context.Strings.NextMonth, month >= latest, "next"))
                ShowMonth(state, month.AddMonths(1));
        }
        using (context.Div(key: "weekdays").Row().Gap(DayGap))
        {
            for (var i = 0; i < 7; i++)
            {
                var day = (DayOfWeek)(((int)first + i) % 7);
                using (context.Div(key: i).Width(DayCell).Height(24).Center().Semantics(Role.ColumnHeader, format.GetDayName(day)))
                    context.Text(format.GetShortestDayName(day), Text(t.FontSize - 2, t.MutedForeground), key: "text");
            }
        }
        var offset = ((int)month.DayOfWeek - (int)first + 7) % 7;
        var start = month.AddDays(-offset);
        var focusVisible = month.Year == state.Focus.Year && month.Month == state.Focus.Month;
        DateOnly? focused = null;
        DateOnly? picked = null;
        using (context.Div(key: "weeks").Column().Gap(DayGap).Semantics(Role.Grid, month.ToString(format.YearMonthPattern, culture)))
        {
            for (var week = 0; week < 6; week++)
            {
                using var row = context.Div(key: week).Row().Gap(DayGap).Semantics(Role.Row);
                for (var d = 0; d < 7; d++)
                {
                    var date = start.AddDays(week * 7 + d);
                    var hit = Day(date, value, today, month, props, focusVisible ? date == state.Focus : date == month, state);
                    if (hit.Focused)
                        focused = date;
                    if (hit.Clicked && Available(date, props))
                        picked = date;
                }
            }
        }
        if (picked is { } chosen)
        {
            state.Focus = chosen;
            changed(chosen);
            return true;
        }
        if (focused is not { } from)
            return false;
        var target = Navigate(from, context.Logical(context.Input.Key), (context.Input.Modifiers & Modifiers.Shift) != 0, first, props);
        if (target is not { } next)
            return false;
        context.Input.Key = Key.None;
        state.Focus = next;
        state.Month = new(next.Year, next.Month, 1);
        state.FocusPending = true;
        context.Changed = true;
        return false;
    }

    Interaction Day(DateOnly date, DateOnly? value, DateOnly today, DateOnly month, CalendarProps props, bool tabStop, CalendarState state)
    {
        var t = Theme;
        var enabled = Available(date, props);
        var selected = value == date;
        var outside = date.Month != month.Month;
        using var cell = context.Div(key: date.DayNumber)
            .Width(DayCell)
            .Height(DayCell)
            .Center()
            .Rounded(t.Radius)
            .TabStop(tabStop)
            .Semantics(Role.Cell, date.ToString("D", Gregorian(context.Culture)))
            .Accessibility(new(Selected: selected));
        // A popup's first frame is transparent and cannot hold focus, so keep asking until focus persists.
        if (state.FocusPending && date == state.Focus)
        {
            if (context.Focus == cell.Id)
                state.FocusPending = false;
            else
                context.FocusNextControl();
        }
        var hit = cell.Control(enabled);
        var foreground = selected ? t.Primary.Foreground : outside ? t.MutedForeground : t.Foreground;
        if (selected)
            cell.Background(t.Primary.Background);
        else if (hit.Hovered && enabled || date == today)
            cell.Background(t.Accent);
        if (date == today && !selected)
            foreground = t.AccentForeground;
        Ring(cell, hit, t);
        if (!enabled)
            cell.Opacity(t.Metrics.DisabledOpacity);
        else
            Hand(hit);
        context.Text(date.Day.ToString(context.Culture), Text(t.FontSize, foreground), key: "day");
        return hit;
    }

    bool MonthButton(string icon, string name, bool disabled, NodeKey key) =>
        IconButton(icon, name, new(Appearance: Appearance.Ghost, Size: ControlSize.Small, Disabled: disabled), key).Clicked && !disabled;

    static void ShowMonth(CalendarState state, DateOnly month)
    {
        state.Month = month;
        state.FocusPending = false;
    }

    static bool Available(DateOnly date, CalendarProps props) =>
        date >= (props.Min ?? DateOnly.MinValue) && date <= (props.Max ?? DateOnly.MaxValue) && props.IsDisabled?.Invoke(date) != true;

    static DateOnly ClampDate(DateOnly date, CalendarProps props)
    {
        if (props.Min is { } min && date < min)
            return min;
        if (props.Max is { } max && date > max)
            return max;
        return date;
    }

    /// <summary>The date a key moves focus to, clamped to Min/Max and skipping disabled dates; null for other keys.</summary>
    static DateOnly? Navigate(DateOnly from, Key key, bool shift, DayOfWeek first, CalendarProps props)
    {
        var weekday = ((int)from.DayOfWeek - (int)first + 7) % 7;
        DateOnly? raw = key switch
        {
            Key.Left => Shift(from, -1),
            Key.Right => Shift(from, 1),
            Key.Up => Shift(from, -7),
            Key.Down => Shift(from, 7),
            Key.PageUp => AddMonths(from, shift ? -12 : -1),
            Key.PageDown => AddMonths(from, shift ? 12 : 1),
            Key.Home => Shift(from, -weekday),
            Key.End => Shift(from, 6 - weekday),
            _ => null
        };
        if (raw is not { } target)
            return null;
        var direction = target >= from ? 1 : -1;
        var probe = ClampDate(target, props);
        // Skip at most ten years of disabled dates in the direction of travel.
        for (var tries = 0; tries < 3660; tries++)
        {
            if (Available(probe, props))
                return probe;
            var next = Shift(probe, direction);
            if (next == probe || next != ClampDate(next, props))
                break;
            probe = next;
        }
        return from;
    }

    static DateOnly Shift(DateOnly date, int days)
    {
        var number = Math.Clamp((long)date.DayNumber + days, DateOnly.MinValue.DayNumber, DateOnly.MaxValue.DayNumber);
        return DateOnly.FromDayNumber((int)number);
    }

    static DateOnly AddMonths(DateOnly date, int months)
    {
        var index = date.Year * 12L + date.Month - 1 + months;
        if (index < 12 || index >= 9999 * 12 + 12)
            return months < 0 ? DateOnly.MinValue : DateOnly.MaxValue;
        return date.AddMonths(months);
    }

    static readonly Dictionary<string, CultureInfo> gregorian = new(StringComparer.Ordinal);

    /// <summary>
    /// The culture's formatting with the Gregorian calendar the grid uses; cultures such as ar-SA or th-TH
    /// otherwise format DateOnly values in another calendar.
    /// </summary>
    static CultureInfo Gregorian(CultureInfo culture)
    {
        if (culture.DateTimeFormat.Calendar is GregorianCalendar)
            return culture;
        lock (gregorian)
        {
            if (gregorian.TryGetValue(culture.Name, out var cached))
                return cached;
            var clone = (CultureInfo)culture.Clone();
            clone.DateTimeFormat.Calendar = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault() ?? new GregorianCalendar();
            var result = CultureInfo.ReadOnly(clone);
            gregorian[culture.Name] = result;
            return result;
        }
    }

    sealed class CalendarState
    {
        public bool Started;
        public bool FocusPending;
        public DateOnly? Value;
        public DateOnly Month;
        public DateOnly Focus;
    }
}
