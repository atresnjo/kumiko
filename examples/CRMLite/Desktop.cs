namespace CRMLite;

/// <summary>Whether this host can post desktop notifications.</summary>
public enum NoticeAccess
{
    /// <summary>The shell has not reported yet; nothing is posted.</summary>
    Unknown,
    Ready,
    /// <summary>This host has no notification service, such as an unbundled, hidden or offscreen run.</summary>
    Unavailable,
    /// <summary>The user declined notifications for CRM Lite in the operating system.</summary>
    Denied
}

/// <summary>
/// One desktop notification. <paramref name="Id"/> names its subject ("plan:{dealId}" or "save"), so a newer notice
/// about the same subject replaces the older one and a click can be traced back to it.
/// </summary>
public sealed record CrmNotice(string Id, string Title, string Body);

/// <summary>What the desktop shell reports. The CRM subscription turns each signal into a message.</summary>
public abstract record DesktopSignal
{
    private DesktopSignal()
    {
    }
    public sealed record Access(NoticeAccess Value, string? Detail = null) : DesktopSignal;
    /// <summary>CRM Lite became the frontmost application, or stopped being it.</summary>
    public sealed record Active(bool Value) : DesktopSignal;
    public sealed record Clicked(string Id) : DesktopSignal;
}

/// <summary>
/// The desktop shell CRM Lite needs: notifications, activation and their events. Views and models never see it;
/// updates reach it through commands and its events arrive through a subscription.
/// </summary>
public interface ICrmDesktop
{
    /// <summary>Reports <see cref="DesktopSignal.Access"/> first, then activity and clicks. Disposing stops delivery.</summary>
    IDisposable Listen(Action<DesktopSignal> signal);

    /// <summary>
    /// Posts or replaces a notice. Fails with <see cref="UnauthorizedAccessException"/> when the user declined
    /// notifications and <see cref="PlatformNotSupportedException"/> when the host has no notification service.
    /// </summary>
    Task Post(CrmNotice notice, CancellationToken token);

    Task Withdraw(string id, CancellationToken token);

    /// <summary>Removes every notice CRM Lite posted, including ones a click can still reach.</summary>
    Task Clear(CancellationToken token);

    /// <summary>Brings CRM Lite forward. Hosts that must stay hidden do nothing.</summary>
    Task BringForward(CancellationToken token);
}

/// <summary>
/// The shell for snapshots, agents, replays, tests and hosts without a notification service: it reports
/// <see cref="NoticeAccess.Unavailable"/>, refuses to post and never touches native services.
/// </summary>
public sealed class OfflineDesktop(string reason) : ICrmDesktop
{
    public string Reason { get; } = reason;

    public IDisposable Listen(Action<DesktopSignal> signal)
    {
        signal(new DesktopSignal.Access(NoticeAccess.Unavailable, Reason));
        return Stopped.Instance;
    }

    public Task Post(CrmNotice notice, CancellationToken token) => Task.FromException(new PlatformNotSupportedException(Reason));

    public Task Withdraw(string id, CancellationToken token) => Task.CompletedTask;

    public Task Clear(CancellationToken token) => Task.CompletedTask;

    public Task BringForward(CancellationToken token) => Task.CompletedTask;

    sealed class Stopped : IDisposable
    {
        public static Stopped Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
