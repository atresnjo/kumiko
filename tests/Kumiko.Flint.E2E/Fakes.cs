using System.Collections.Concurrent;
using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;

/// <summary>Records platform launches instead of opening the user's applications, Finder or Terminal.</summary>
sealed class RecordingLauncher : ILauncher
{
    public ConcurrentQueue<LaunchRequest> Requests { get; } = new();
    public Task Launch(LaunchRequest request, CancellationToken token)
    {
        Requests.Enqueue(request);
        return Task.CompletedTask;
    }
}
/// <summary>Fixed gh logins; the user's gh configuration and active account are never read.</summary>
sealed class FixedAccounts : IGitHubAccounts
{
    public int Calls;
    public Task<ImmutableArray<GhAccount>> List(CancellationToken token)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult<ImmutableArray<GhAccount>>([new("octo-personal", true, "repo, read:org"), new("octo-work", false, "repo")]);
    }
}
/// <summary>Counts full snapshot loads and status-only refreshes around the real Git service.</summary>
sealed class CountingGit(IGitService inner) : IGitService
{
    public int Loads;
    public int Statuses;
    /// <summary>While set, loads wait for it, so a test can look at the window before Git answers.</summary>
    public TaskCompletionSource? Hold;
    public async Task<Snapshot> Load(string path, int limit, CancellationToken token)
    {
        Interlocked.Increment(ref Loads);
        if (Hold is { } hold)
            await hold.Task.WaitAsync(token).ConfigureAwait(false);
        return await inner.Load(path, limit, token).ConfigureAwait(false);
    }
    public Task<WorkingStatus> Status(string path, CancellationToken token)
    {
        Interlocked.Increment(ref Statuses);
        return inner.Status(path, token);
    }
    public Task<CommitDetail> Detail(string path, string sha, CancellationToken token) => inner.Detail(path, sha, token);
    public Task<(CommitDetail Detail, string Patch)> Commit(string path, Commit commit, CancellationToken token) => inner.Commit(path, commit, token);
    public Task<string> Patch(string path, CommitDetail? detail, FileChange? file, bool staged, CancellationToken token) => inner.Patch(path, detail, file, staged, token);
    public Task<string> Execute(string path, GitRequest request, string account, CancellationToken token) => inner.Execute(path, request, account, token);
    public Task<string> RemoteUrl(string path, string remote, CancellationToken token) => inner.RemoteUrl(path, remote, token);
}
