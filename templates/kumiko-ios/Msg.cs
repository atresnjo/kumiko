using Kumiko.iOS;
using Kumiko.Runtime;

namespace KumikoiOSTemplate;

internal abstract partial record Msg
{
    public sealed record Increment : Msg;
    public sealed record Rename(string Text) : Msg;
    public sealed record SetUpdates(bool On) : Msg;
    public sealed record Open(Route Route) : Msg;
    public sealed record Navigate(HistoryRequest Request) : Msg;
    public sealed record PhaseChanged(NativeAppPhase Phase) : Msg;
}
