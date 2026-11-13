using System.Globalization;

namespace DiskMap.Core;

/// <summary>Sizes in decimal units, as Finder shows them, and counts with the culture's grouping.</summary>
public static class Format
{
    public static string Bytes(long bytes, CultureInfo culture)
    {
        if (bytes < 1000)
            return bytes.ToString(culture) + " B";
        double value = bytes;
        int unit = 0;
        while (value >= 999.95 && unit < 5)
        {
            value /= 1000;
            unit++;
        }
        return value.ToString(value >= 100 ? "0" : "0.0", culture) + " " + Unit(unit);
    }

    static string Unit(int unit) => unit switch
    {
        1 => "KB",
        2 => "MB",
        3 => "GB",
        4 => "TB",
        _ => "PB"
    };

    public static string Count(long count, CultureInfo culture) => count.ToString("N0", culture);

    public static string Value(long value, Metric metric, CultureInfo culture) => metric == Metric.Files
        ? Count(value, culture) + (value == 1 ? " file" : " files")
        : Bytes(value, culture);

    public static string Share(double share, CultureInfo culture) => share >= .9995 ? "100%" :
        share < .001 && share > 0 ? "<0.1%" : (share * 100).ToString("0.0", culture) + "%";

    public static string Date(long seconds, CultureInfo culture) => seconds <= 0 ? "—" :
        DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("d", culture);

    public static string Duration(TimeSpan elapsed, CultureInfo culture) => elapsed.TotalSeconds < 10
        ? elapsed.TotalSeconds.ToString("0.0", culture) + " s"
        : elapsed.TotalSeconds.ToString("0", culture) + " s";
}

/// <summary>Capacity of the volume holding a path.</summary>
public sealed record Volume(string Name, long Total, long Free)
{
    public long Used => Math.Max(0, Total - Free);

    public static Volume? Of(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            return new(drive.VolumeLabel.Length > 0 ? drive.VolumeLabel : drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
