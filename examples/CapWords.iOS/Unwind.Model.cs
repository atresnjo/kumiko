using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal sealed record UnwindModel(int Index = 2, string? Notice = null)
{
    /// <summary>The study's one-screen history when it runs on its own; the gallery keeps its own.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("study");
}

internal abstract partial record UnwindMessage
{
    public sealed record Select(int Index) : UnwindMessage;
    public sealed record Play(int Index) : UnwindMessage;
    public sealed record Explain(string Topic) : UnwindMessage;
    public sealed record CloseNotice : UnwindMessage;
    public sealed record Back : UnwindMessage;
    public sealed record Navigate(HistoryRequest Request) : UnwindMessage;
}

internal sealed partial class UnwindApp
{
    static readonly ImmutableArray<string> Breath = ["In 4", "Hold 4", "Out 6"];

    // Breathwork, Sleep and Outside follow the reference video; Focus and Journal complete its five-dot carousel.
    internal static ImmutableArray<NativeArcCard> Routines =>
    [
        new("focus", "Focus", "scope", "10:00 am", "One clear hour.", "Single task, phone face down", Breath, "25", "Focus",
            Color.Hex(0x7fb0f0), Color.Hex(0x9cc4f5)),
        new("sleep", "Sleep", "moon.stars.fill", "9:30 pm", "Wind down.", "Body scan before bed", Breath, "10", "Sleep",
            Color.Hex(0xa48af0), Color.Hex(0xc2b2f7)),
        new("breathwork", "Breathwork", "wind", "3:00 pm", "A slower start.", "Breathe in 4, hold 4, out 6", Breath, "3", "Breathe",
            Color.Hex(0xe8602a), Color.Hex(0xf1b553)),
        new("outside", "Outside", "figure.walk", "7:15 am", "Morning light.", "Ten minutes before screens", Breath, "15", "Outside",
            Color.Hex(0x9ed07e), Color.Hex(0xb9e09c)),
        new("journal", "Journal", "book.closed.fill", "8:00 pm", "One honest line.", "Write before you sleep", Breath, "5", "Journal",
            Color.Hex(0xf2b64a), Color.Hex(0xf6cf7e))
    ];

    internal static NativeArcHeading Greeting => new("good morning", "maya", "You made time for yourself on 4 days.");

    internal static Color Paper => Color.Hex(0xf4f2ee);
    internal static Color Ink => Color.Hex(0x1f1e1c);
    internal static Color Muted => Color.Hex(0x7c7873);
    internal static Color Faint => Color.Hex(0x8e8a84);
    internal static Color Chip => Color.Hex(0xf3f1ed);
    internal static Color Night => Color.Hex(0x232320);
    internal static NativePalette Palette => new(Paper, Color.White, Color.White, Ink, Muted, Color.Hex(0xe8602a),
        Color.Hex(0xf1b553), Color.Hex(0xd5d1cb));
}
