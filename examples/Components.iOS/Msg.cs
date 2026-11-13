using Kumiko.Runtime;

internal abstract partial record ComponentsMessage
{
    public sealed record Add : ComponentsMessage;
    public sealed record Select(string Id) : ComponentsMessage;
    public sealed record Close : ComponentsMessage;
    public sealed record RemoveSelected : ComponentsMessage;
    public sealed record Navigate(HistoryRequest Request) : ComponentsMessage;
}
