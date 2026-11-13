using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed record OpalModel(int Index = 1, bool Expanded = false, int TapCount = 0,
    long LastTap = 0, string? Notice = null)
{
    /// <summary>The study's one-screen history when it runs on its own; the gallery keeps its own.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("study");
}

internal abstract partial record OpalMessage
{
    public sealed record Step(int Direction, long Milliseconds) : OpalMessage;
    public sealed record Select(string Id) : OpalMessage;
    public sealed record Toggle : OpalMessage;
    public sealed record Back : OpalMessage;
    public sealed record Explain(string Title) : OpalMessage;
    public sealed record CloseNotice : OpalMessage;
    public sealed record Navigate(HistoryRequest Request) : OpalMessage;
}

internal sealed partial class OpalApp
{
    internal static ImmutableArray<NativeChoice> Steps
    {
        get;
    } = [.. Enumerable.Range(0, 13)
        .Select(index => new NativeChoice((index * 5).ToString(System.Globalization.CultureInfo.InvariantCulture), $"{index * 5} minutes")),
        .. Enumerable.Range(2, 23).Select(hours => new NativeChoice((hours * 60).ToString(System.Globalization.CultureInfo.InvariantCulture), $"{hours} hours"))];

    internal static string Value(int index) => index < 12 ? $"{index * 5}m" : index == 12 ? "1h" : $"{index - 11}h";
}
