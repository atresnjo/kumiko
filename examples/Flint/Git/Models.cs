using System.Collections.Immutable;

namespace Flint.Git;

/// <summary>A local branch, remote branch or tag. Remote names include their remote, for example origin/main.</summary>
public sealed record GitRef(string Name, string Sha, string Kind, bool Current = false, string Upstream = "",
    int Ahead = 0, int Behind = 0, bool Gone = false);
public sealed record FileChange(string Path, char Status, string? OriginalPath = null);
public sealed record Head(string Branch = "", string Sha = "", string Upstream = "", int Ahead = 0, int Behind = 0, bool Unborn = false, bool Detached = false);
/// <summary>A decoration on a commit, ordered current first, then local, remote and tag, as in the original.</summary>
public sealed record RefLabel(string Name, RefKind Kind, bool Current = false);
public enum RefKind
{
    Head, Local, Remote, Tag
}
/// <summary>A GitHub-style 5x5 symmetric identicon: 15 cell bits (three columns by five rows) and an RGB tint.</summary>
public readonly record struct Avatar(int Cells, uint Color);
public sealed record Commit(string Sha, ImmutableArray<string> Parents, string Author, string Email, long Time, string Subject,
    ImmutableArray<RefLabel> Refs, Avatar Avatar = default);
public sealed record CommitDetail(string Sha, ImmutableArray<string> Parents, string Author, string Email, long Time, string Subject, string Body,
    ImmutableArray<FileChange> Files, string Committer = "", string CommitterEmail = "", long CommitTime = 0,
    ImmutableArray<RefLabel>? Labels = null, Avatar Avatar = default)
{
    public ImmutableArray<RefLabel> Refs => Labels ?? [];
}
public sealed record Stash(string Ref, string Sha, string Message);
public sealed record Worktree(string Path, string Sha, string Branch, bool Locked = false, bool Prunable = false, bool Detached = false, bool Bare = false);
/// <summary>Working tree status. Files are sorted in tree order: folders before files at every level.</summary>
public sealed record WorkingStatus(Head Head, ImmutableArray<FileChange> Staged, ImmutableArray<FileChange> Unstaged);
public sealed record Snapshot(string Path, Head Head, ImmutableArray<Commit> Commits, ImmutableArray<GitRef> Refs,
    ImmutableArray<FileChange> Staged, ImmutableArray<FileChange> Unstaged, ImmutableArray<Stash> Stashes,
    ImmutableArray<Worktree> Worktrees, ImmutableArray<GraphRow> Graph, int Limit = 4000, bool HasMore = false,
    ImmutableArray<string>? RemoteNames = null, long LoadedAt = 0)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public ImmutableArray<string> Remotes => RemoteNames ?? [];
}
public sealed record GraphSegment(int Lane, int Other, int Color);
public sealed record GraphRow(int Lane, int Color, int Width, ImmutableArray<GraphSegment> Top, ImmutableArray<GraphSegment> Bottom, bool Merge);
public enum GitAction
{
    Stage, Unstage, StageAll, UnstageAll, Commit, Fetch, Pull, Push, Stash, PopStash,
    Checkout, CheckoutRemote, NewBranch, Revert, DeleteWorktree, DeleteWorktreeAndBranch, PruneWorktrees, Detach,
    AddRemote, SetRemoteUrl, RenameRemote, RemoveRemote, SetUpstream, RemoveUpstream
}
/// <summary>A Git mutation. ThenPush chains a push after a commit lands; NoVerify skips hooks for this request only.</summary>
public sealed record GitRequest(GitAction Action, ImmutableArray<FileChange>? Files = null, string Value = "", string Body = "", bool Amend = false,
    bool Staged = false, bool NoVerify = false, bool ThenPush = false)
{
    public ImmutableArray<FileChange> SelectedFiles => Files ?? [];
}
/// <summary>A gh login for github.com. Tokens are never part of this record.</summary>
public sealed record GhAccount(string Login, bool Active, string Scopes);
