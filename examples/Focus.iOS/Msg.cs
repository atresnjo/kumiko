using Kumiko.Runtime;

internal abstract partial record FocusMessage
{
    public sealed record SelectTab(string Id) : FocusMessage;
    public sealed record Navigate(HistoryRequest Request) : FocusMessage;
    public sealed record OpenDrawer(string Kind) : FocusMessage;
    public sealed record CloseDrawer : FocusMessage;
    public sealed record SetDuration(int Minutes) : FocusMessage;
    public sealed record ToggleBlock(string Id) : FocusMessage;
    public sealed record Start : FocusMessage;
    public sealed record Pause : FocusMessage;
    public sealed record Tick : FocusMessage;
}
