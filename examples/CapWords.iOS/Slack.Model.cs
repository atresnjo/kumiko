using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;

internal sealed record SlackModel(int Index = 4, ImmutableArray<string> Dispositions = default,
    NativeSwipeDirection Direction = NativeSwipeDirection.Right, int Revision = 0, bool Busy = false, bool Undoing = false)
{
    /// <summary>The study's one-screen history when it runs on its own; the gallery keeps its own.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("study");
}

internal abstract partial record SlackMessage
{
    public sealed record Swipe(string Id, NativeSwipeDirection Direction) : SlackMessage;
    public sealed record KeepUnread : SlackMessage;
    public sealed record MarkRead : SlackMessage;
    public sealed record Undo : SlackMessage;
    public sealed record Ready(int Revision) : SlackMessage;
    public sealed record Back : SlackMessage;
    public sealed record Done : SlackMessage;
    public sealed record Navigate(HistoryRequest Request) : SlackMessage;
}

internal sealed partial class SlackApp
{
    internal static ImmutableArray<string> Ids => ["channel-1", "channel-2", "channel-3", "channel-4", "channel-5"];
}
