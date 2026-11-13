using System.Collections.Immutable;
using Kumiko.Runtime;

namespace CRMLite;

/// <summary>A fake session handle, not a credential. Real access tokens belong inside a service.</summary>
public sealed record AuthSession(string Id, Member User, bool CanEdit, string Workspace = "Harbor");

public interface IAuthService
{
    Task<AuthSession> SignInAsync(string email, CancellationToken token);
    Task SignOutAsync(AuthSession session, CancellationToken token);
}

public interface ICrmApi
{
    Task<WorkspaceData> LoadAsync(AuthSession session, CancellationToken token);
    Task SaveAsync(AuthSession session, WorkspaceData data, CancellationToken token);
    Task<ImmutableArray<string>> SearchAsync(AuthSession session, string query, CancellationToken token);
}

/// <summary>The backend document; UI state, preferences and services never enter it.</summary>
public sealed record WorkspaceData(
    ImmutableArray<Company> Companies,
    ImmutableArray<Deal> Deals,
    ImmutableArray<Contact> Contacts,
    ImmutableArray<Activity> Activities,
    ImmutableArray<InboxThread> Inbox,
    ImmutableArray<string> Favorites,
    int Sequence,
    long Revision = 0)
{
    public static WorkspaceData From(CrmModel m) => new(m.Companies, m.Deals, m.Contacts, m.Activities, m.Inbox, m.Favorites, m.Sequence, m.Backend.Revision);

    public CrmModel Apply(CrmModel m) => m with
    {
        Companies = Companies,
        Deals = Deals,
        Contacts = Contacts,
        Activities = Activities,
        Inbox = Inbox,
        Favorites = Favorites,
        Sequence = Sequence,
        Backend = m.Backend with
        {
            Loaded = true,
            Loading = false,
            Revision = Revision,
            Error = null
        }
    };
}

/// <summary>
/// An in-process C# backend with cancellable latency, server-owned sessions and revisioned storage.
/// One instance is shared by the auth and API registrations; each host owns a separate backend.
/// </summary>
public sealed class FakeCrmBackend : IAuthService, ICrmApi
{
    readonly object gate = new();
    readonly Dictionary<string, AuthSession> sessions = [];
    readonly TimeSpan latency;
    WorkspaceData data;
    int nextSession;

    public FakeCrmBackend(TimeSpan? latency = null, CrmModel? seed = null)
    {
        this.latency = latency ?? TimeSpan.FromMilliseconds(250);
        var initial = seed ?? Data.Initial();
        data = WorkspaceData.From(initial);
        if (initial.Auth.Session is { } session)
            sessions.Add(session.Id, session);
    }

    /// <summary>This backend's auth and API registrations with an offline desktop shell, as tests and snapshots use.</summary>
    public KumikoServices Services() => Services(new OfflineDesktop("This host has no desktop notification service."));

    public KumikoServices Services(ICrmDesktop desktop) => new KumikoServices().Add<IAuthService>(this).Add<ICrmApi>(this).Add(desktop);

    public async Task<AuthSession> SignInAsync(string email, CancellationToken token)
    {
        await Task.Delay(latency, token).ConfigureAwait(false);
        var member = Data.Team.FirstOrDefault(m => string.Equals(m.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Choose a demo account listed below.");
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            var session = new AuthSession($"fake-session-{++nextSession}", member, member.Id != "nina");
            sessions.Add(session.Id, session);
            return session;
        }
    }

    public async Task SignOutAsync(AuthSession session, CancellationToken token)
    {
        await Task.Delay(latency, token).ConfigureAwait(false);
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            sessions.Remove(session.Id);
        }
    }

    public async Task<WorkspaceData> LoadAsync(AuthSession session, CancellationToken token)
    {
        await Task.Delay(latency, token).ConfigureAwait(false);
        lock (gate)
        {
            Require(session, token);
            return data;
        }
    }

    public async Task SaveAsync(AuthSession session, WorkspaceData changes, CancellationToken token)
    {
        await Task.Delay(latency, token).ConfigureAwait(false);
        lock (gate)
        {
            if (!Require(session, token).CanEdit)
                throw new UnauthorizedAccessException("This demo account has read-only access.");
            // A cancelled/slow older save cannot replace a more recent document.
            if (changes.Revision > data.Revision)
                data = changes;
        }
    }

    public async Task<ImmutableArray<string>> SearchAsync(AuthSession session, string query, CancellationToken token)
    {
        await Task.Delay(latency, token).ConfigureAwait(false);
        lock (gate)
        {
            Require(session, token);
            return CompanyDirectory.Match(data.Companies, query);
        }
    }

    AuthSession Require(AuthSession session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return sessions.GetValueOrDefault(session.Id) ?? throw new UnauthorizedAccessException("Sign in again to continue.");
    }
}
