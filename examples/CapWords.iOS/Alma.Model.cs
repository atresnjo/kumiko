using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed record AlmaModel(int Index = 1, bool Playing = true, bool Notice = false)
{
    /// <summary>The study's one-screen history when it runs on its own; the gallery keeps its own.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("study");
}
internal abstract partial record AlmaMessage
{
    public sealed record Tick : AlmaMessage;
    public sealed record Next : AlmaMessage;
    public sealed record Previous : AlmaMessage;
    public sealed record Select(int Index) : AlmaMessage;
    public sealed record TogglePlayback : AlmaMessage;
    public sealed record Back : AlmaMessage;
    public sealed record Continue : AlmaMessage;
    public sealed record CloseNotice : AlmaMessage;
    public sealed record Navigate(HistoryRequest Request) : AlmaMessage;
}

internal sealed partial class AlmaApp
{
    internal static ImmutableArray<NativeCarouselItem> Items =>
    [
        new("bone", "Bone health", "🦴"),
        new("energy", "Support energy", "⚡️"),
        new("gain", "Gain weight", "⚖️", 1),
        new("custom", "Track custom nutrients", "🔬", 2),
        new("maintain", "Maintain weight", "⚖️"),
        new("heart", "Heart health", "🫀", 1),
        new("immunity", "Boost immunity", "🤧", 2),
        new("lose", "Lose weight", "🤧", 2)
    ];
}
