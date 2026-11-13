using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    /// <summary>
    /// The desktop shell always reports access, activity and notice clicks. A running agent plan advances one beat of
    /// work every 700 ms; nothing ticks while plans wait or are paused.
    /// </summary>
    public static IEnumerable<Sub<CrmMsg>> Subscriptions(CrmModel model)
    {
        yield return Sub<CrmMsg>.From<ICrmDesktop>("desktop", static (desktop, send) => desktop.Listen(signal => send(Message(signal))));
        if (model.CanEdit && model.LiveAgent && model.Plans.Any(p => p.Status == PlanStatus.Running))
            yield return Sub<CrmMsg>.Every("agent", TimeSpan.FromMilliseconds(700), () => new CrmMsg.AgentTick());
    }

    static CrmMsg Message(DesktopSignal signal) => new CrmMsg.Desktop(signal switch
    {
        DesktopSignal.Access access => new DesktopMsg.AccessChanged(access.Value, access.Detail),
        DesktopSignal.Active active => new DesktopMsg.Activated(active.Value),
        DesktopSignal.Clicked clicked => new DesktopMsg.Clicked(clicked.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(signal))
    });
}
