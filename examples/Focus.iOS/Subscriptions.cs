using Kumiko.Runtime;

internal sealed partial class FocusApp
{
    public static IEnumerable<Sub<FocusMessage>> Subscriptions(FocusModel model) => model.Running
        ? [Sub<FocusMessage>.Every("session-clock", TimeSpan.FromSeconds(1), () => new FocusMessage.Tick())]
        : [];
}
