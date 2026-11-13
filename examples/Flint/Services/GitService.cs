using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using Flint.Git;

namespace Flint.Services;

public interface IGitService
{
    Task<Snapshot> Load(string path, int limit, CancellationToken token);
    /// <summary>The cheap path for working-tree edits: one `git status`, sorted in tree order.</summary>
    Task<WorkingStatus> Status(string path, CancellationToken token);
    Task<CommitDetail> Detail(string path, string sha, CancellationToken token);
    /// <summary>A history commit's details and full patch. Its parents are known, so the Git commands run together.</summary>
    Task<(CommitDetail Detail, string Patch)> Commit(string path, Commit commit, CancellationToken token);
    Task<string> Patch(string path, CommitDetail? detail, FileChange? file, bool staged, CancellationToken token);
    Task<string> Execute(string path, GitRequest request, string account, CancellationToken token);
    Task<string> RemoteUrl(string path, string remote, CancellationToken token);
}
public sealed partial class GitService(IProcessRunner runner, string stateDirectory, IRepositoryCache? cache = null) : IGitService
{
    readonly ConcurrentDictionary<string, Snapshot> snapshots = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);
    Task<string> Git(string path, CancellationToken token, params string[] args) => runner.Run("git", path, args, token);
    public async Task<Snapshot> Load(string path, int limit, CancellationToken token)
    {
        // Git accepts any folder inside the work tree and porcelain status paths are root-relative, so nothing waits for the root.
        var directory = Expand(path);
        var top = Git(directory, token, "rev-parse", "--show-toplevel");
        var status = Git(directory, token, "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all");
        var refs = Git(directory, token, "for-each-ref", "--format=" + Parse.RefFormat, "refs/heads", "refs/remotes", "refs/tags");
        var stash = Git(directory, token, "stash", "list", "--format=%gd%x00%H%x00%gs");
        var worktrees = Git(directory, token, "worktree", "list", "--porcelain", "-z");
        var remotes = Git(directory, token, "remote");
        // Without a previous snapshot there is no history to reuse, so the log starts with the others. It fails harmlessly
        // in a repository without commits, which never reads it.
        var early = snapshots.ContainsKey(directory) ? null : Log(directory, unborn: false, limit, token);
        early?.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await Task.WhenAll(top, status, refs, stash, worktrees, remotes).ConfigureAwait(false);
        }
        catch when (top.IsFaulted)
        {
            // Report "not a git repository" rather than whichever command failed first.
            await top.ConfigureAwait(false);
        }
        var root = (await top.ConfigureAwait(false)).TrimEnd('\r', '\n');
        var parsed = Parse.Status(await status.ConfigureAwait(false));
        var parsedRefs = Parse.Refs(await refs.ConfigureAwait(false));
        snapshots.TryGetValue(root, out var previous);
        var reuse = previous is not null && previous.Head.Sha == parsed.Head.Sha && previous.Limit == limit && previous.Refs.SequenceEqual(parsedRefs);
        var commits = reuse ? previous!.Commits : parsed.Head.Unborn && parsedRefs.IsEmpty ? [] : Parse.Log(await (early is not null && !parsed.Head.Unborn
            ? early : Log(root, parsed.Head.Unborn, limit, token)).ConfigureAwait(false));
        var more = reuse ? previous!.HasMore : commits.Length > limit;
        if (commits.Length > limit)
            commits = commits[..limit];
        var snapshot = new Snapshot(root, parsed.Head, commits, parsedRefs, parsed.Staged, parsed.Unstaged,
            Parse.Stashes(await stash.ConfigureAwait(false)), Parse.Worktrees(await worktrees.ConfigureAwait(false)), reuse ? previous!.Graph : Graph.Layout(commits), limit, more,
            [.. (await remotes.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        snapshots[root] = snapshot;
        if (cache is not null && (!reuse || previous!.Head != snapshot.Head || !previous.Remotes.SequenceEqual(snapshot.Remotes)))
            await cache.Write(snapshot, token).ConfigureAwait(false);
        return snapshot;
    }
    Task<string> Log(string directory, bool unborn, int limit, CancellationToken token) => runner.Run("git", directory,
        ["log", "--branches", "--remotes", "--tags", .. unborn ? Array.Empty<string>() : ["HEAD"], "--date-order", "--decorate=full",
            "-n" + (limit + 1).ToString(CultureInfo.InvariantCulture), "--format=" + Parse.LogFormat], token);
    public async Task<WorkingStatus> Status(string path, CancellationToken token) =>
        Parse.Status(await Git(path, token, "status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all").ConfigureAwait(false));
    public Task<CommitDetail> Detail(string path, string sha, CancellationToken token) => Detail(path, sha, null, token);
    public async Task<(CommitDetail Detail, string Patch)> Commit(string path, Commit commit, CancellationToken token)
    {
        var detail = Detail(path, commit.Sha, commit.Parents, token);
        var patch = Patch(path, (commit.Sha, commit.Parents), null, false, token);
        try
        {
            await Task.WhenAll(detail, patch).ConfigureAwait(false);
        }
        catch when (detail.IsFaulted)
        {
            await detail.ConfigureAwait(false);
        }
        return (await detail.ConfigureAwait(false), await patch.ConfigureAwait(false));
    }
    /// <summary>With <paramref name="known"/> parents the header and file list are read together; otherwise the header names them.</summary>
    async Task<CommitDetail> Detail(string path, string sha, ImmutableArray<string>? known, CancellationToken token)
    {
        ValidateRevision(sha);
        var header = Git(path, token, "show", "-s", "--decorate=full", "--format=%H%x00%P%x00%an%x00%ae%x00%at%x00%cn%x00%ce%x00%ct%x00%D%x00%B", sha, "--");
        var early = known is { } given ? Files(path, sha, given, token) : null;
        var meta = (await header.ConfigureAwait(false)).Split('\0', 10);
        ImmutableArray<string> parents = [.. meta[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        var files = await (early ?? Files(path, sha, parents, token)).ConfigureAwait(false);
        var message = meta[9].TrimEnd('\r', '\n');
        var nl = message.IndexOf('\n');
        return new(meta[0], parents, meta[2], meta[3], long.Parse(meta[4], CultureInfo.InvariantCulture),
            nl < 0 ? message : message[..nl], nl < 0 ? "" : message[(nl + 1)..].TrimStart('\n'), Parse.Files(files),
            meta[5], meta[6], long.Parse(meta[7], CultureInfo.InvariantCulture), Parse.Decoration(meta[8]), Parse.Identicon(meta[3]));
    }
    // Merges are shown against their first parent, like Fork and the original.
    Task<string> Files(string path, string sha, ImmutableArray<string> parents, CancellationToken token) => parents.IsEmpty
        ? Git(path, token, "diff-tree", "--no-commit-id", "-r", "-M", "--root", "--name-status", "-z", sha, "--")
        : Git(path, token, "diff", "-M", "--name-status", "-z", parents[0], sha, "--");
    public Task<string> Patch(string path, CommitDetail? detail, FileChange? file, bool staged, CancellationToken token) =>
        Patch(path, detail is null ? null : (detail.Sha, detail.Parents), file, staged, token);
    async Task<string> Patch(string path, (string Sha, ImmutableArray<string> Parents)? commit, FileChange? file, bool staged, CancellationToken token)
    {
        var args = new List<string>();
        var difference = false;
        if (commit is var (sha, parents))
        {
            ValidateRevision(sha);
            if (parents.IsEmpty)
                args.AddRange(["show", "--format=", "--root", "-M", sha]);
            else
                args.AddRange(["diff", "-M", parents[0], sha]);
        }
        else if (file?.Status == '?' && !staged)
        {
            args.AddRange(["diff", "--no-index"]);
            difference = true;
        }
        else
        {
            args.AddRange(["diff", "-M"]);
            if (staged)
                args.Add("--cached");
        }
        args.Add("--");
        if (difference)
            args.Add(OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
        if (file is not null)
        {
            args.Add(file.Path);
            if (file.OriginalPath is { } original && !difference)
                args.Add(original);
        }
        var result = await runner.Run("git", path, args, token, allowDifference: difference).ConfigureAwait(false);
        return result.Length > 1_500_000 ? result[..1_500_000] + "\n[Patch truncated at 1.5 MB]\n" : result;
    }
    static void ValidateRevision(string revision)
    {
        if (revision.Length == 0 || revision[0] == '-')
            throw new ArgumentException("Choose a valid Git revision.");
    }
    internal static string Expand(string path) => Path.GetFullPath(path.StartsWith("~/", StringComparison.Ordinal)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]) : path);
}
