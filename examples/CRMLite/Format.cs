using System.Globalization;

namespace CRMLite;

/// <summary>Money, relative dates and timeline headings. Callers pass the window culture.</summary>
public static class Format
{
    /// <summary>Interactions older than this are flagged as going cold.</summary>
    public const int StaleDays = 14;

    public static string Money(long value, CultureInfo culture) => "$" + value.ToString("N0", culture);

    /// <summary>"$186k", "$3.39M".</summary>
    public static string Short(long value, CultureInfo culture) => value >= 1_000_000
        ? "$" + (value / 1_000_000d).ToString("0.##", culture) + "M"
        : value >= 1_000 ? "$" + (value / 1_000d).ToString("0.#", culture) + "k" : "$" + value.ToString(culture);

    public static string Relative(DateTime at, DateTime now, CultureInfo culture)
    {
        var days = (now.Date - at.Date).Days;
        if (days == 0)
        {
            var minutes = (int)(now - at).TotalMinutes;
            if (minutes < 1)
                return "Just now";
            return minutes < 60 ? $"{minutes}m ago" : $"{minutes / 60}h ago";
        }
        if (days == 1)
            return "Yesterday";
        if (days is > 1 and < 7)
            return $"{days} days ago";
        return Date(DateOnly.FromDateTime(at), DateOnly.FromDateTime(now), culture);
    }

    public static bool Stale(DateTime at, DateTime now) => (now.Date - at.Date).Days > StaleDays;

    /// <summary>"Oct 13"; dates more than about eleven months from today add the year.</summary>
    public static string Date(DateOnly day, DateOnly today, CultureInfo culture) =>
        day.ToString(Math.Abs(day.DayNumber - today.DayNumber) <= 330 ? "MMM d" : "MMM d, yyyy", culture);

    /// <summary>A board close date; deals closing within a week read "In 3 days".</summary>
    public static (string Text, bool Soon) Close(DateOnly close, DateTime now, CultureInfo culture)
    {
        var days = close.DayNumber - DateOnly.FromDateTime(now).DayNumber;
        return days switch
        {
            < 0 => (Date(close, DateOnly.FromDateTime(now), culture), false),
            0 => ("Today", true),
            1 => ("Tomorrow", true),
            < 7 => ($"In {days} days", true),
            _ => (Date(close, DateOnly.FromDateTime(now), culture), false)
        };
    }

    /// <summary>"Today", "Yesterday" or "Thursday, Oct 15".</summary>
    public static string Day(DateOnly day, DateOnly today, CultureInfo culture)
    {
        var days = today.DayNumber - day.DayNumber;
        if (days == 0)
            return "Today";
        if (days == 1)
            return "Yesterday";
        return day.ToString(day.Year == today.Year ? "dddd, MMM d" : "dddd, MMM d, yyyy", culture);
    }

    public static string Time(DateTime at, CultureInfo culture) => at.ToString("h:mm tt", culture);

    public static string Count(int value, CultureInfo culture) => value.ToString("N0", culture);

    /// <summary>Parses "$120k", "120,000" or "1.2m"; null when the text is not a positive amount.</summary>
    public static long? ParseMoney(string text)
    {
        var clean = text.Trim().TrimStart('$').Replace(",", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        var scale = 1d;
        if (clean.EndsWith('k'))
            scale = 1_000;
        else if (clean.EndsWith('m'))
            scale = 1_000_000;
        if (scale > 1)
            clean = clean[..^1];
        if (!double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            return null;
        return (long)Math.Round(amount * scale);
    }
}
