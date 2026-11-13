using System.Text.Json.Serialization;
using CRMLite;

/// <summary>
/// An injected desktop shell that records what CRM Lite asks of it. It never touches a notification centre,
/// activates an application or reads user defaults.
/// </summary>
sealed class RecordingDesktop(NoticeAccess access = NoticeAccess.Ready, string? detail = null) : ICrmDesktop
{
    readonly object gate = new();
    readonly List<Action<DesktopSignal>> listeners = [];
    readonly List<string> log = [];

    public Exception? Failure
    {
        get; set;
    }
    public List<CrmNotice> Posts { get; } = [];
    public List<string> Withdrawn { get; } = [];
    public int Clears
    {
        get; private set;
    }
    public int Activations
    {
        get; private set;
    }
    public int Listeners
    {
        get
        {
            lock (gate)
                return listeners.Count;
        }
    }
    public IReadOnlyList<string> Log
    {
        get
        {
            lock (gate)
                return [.. log];
        }
    }

    public IDisposable Listen(Action<DesktopSignal> signal)
    {
        lock (gate)
            listeners.Add(signal);
        signal(new DesktopSignal.Access(access, detail));
        return new Stop(this, signal);
    }

    /// <summary>Delivers a shell event as the native adapter would, from the caller's thread.</summary>
    public void Emit(DesktopSignal signal)
    {
        Action<DesktopSignal>[] current;
        lock (gate)
        {
            log.Add("signal " + signal);
            current = [.. listeners];
        }
        foreach (var listener in current)
            listener(signal);
    }

    public Task Post(CrmNotice notice, CancellationToken token)
    {
        lock (gate)
        {
            Posts.Add(notice);
            log.Add($"post {notice.Id}: {notice.Title} | {notice.Body}");
        }
        return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public Task Withdraw(string id, CancellationToken token)
    {
        lock (gate)
        {
            Withdrawn.Add(id);
            log.Add("withdraw " + id);
        }
        return Task.CompletedTask;
    }

    public Task Clear(CancellationToken token)
    {
        lock (gate)
        {
            Clears++;
            log.Add("clear");
        }
        return Task.CompletedTask;
    }

    public Task BringForward(CancellationToken token)
    {
        lock (gate)
        {
            Activations++;
            log.Add("bring forward");
        }
        return Task.CompletedTask;
    }

    sealed class Stop(RecordingDesktop owner, Action<DesktopSignal> signal) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
                owner.listeners.Remove(signal);
        }
    }
}

[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(IReadOnlyList<string>))]
partial class DesktopJson : JsonSerializerContext;
