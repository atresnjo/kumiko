using Kumiko.Runtime;

namespace TraceWaterfall;

public sealed partial class TraceApp
{
    // Four times real time for the demo. A pause or the last event removes the timer entirely.
    public static IEnumerable<Sub<TraceMsg>> Subscriptions(TraceModel model) => model.Playing && model.Now < 92000
        ? [Sub<TraceMsg>.Every("trace-clock", TimeSpan.FromMilliseconds(100), () => new TraceMsg.Tick())]
        : [];
}
