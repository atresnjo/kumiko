using Kumiko.Runtime;

namespace KumikoTemplate;

internal abstract partial record Msg
{
    public sealed record Increment : Msg;
    public sealed record Rename(string Text) : Msg;
    public sealed record SetUpdates(bool On) : Msg;
    public sealed record Navigate(HistoryRequest Request) : Msg;
}
