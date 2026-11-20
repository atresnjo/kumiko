namespace DiffViewer.Sources;

public enum SourceKind : byte
{
    /// <summary>The working tree, staged and untracked files against Base (HEAD by default).</summary>
    WorkingTree,
    /// <summary>The index against HEAD.</summary>
    Staged,
    /// <summary>Base against Target, two revisions.</summary>
    Revisions,
    /// <summary>Two folders or two files, without Git.</summary>
    Paths,
    /// <summary>Built-in sample changes.</summary>
    Demo,
    /// <summary>A GitHub pull request, read through its API; Path stays the local project it was opened from.</summary>
    PullRequest
}

/// <summary>
/// What to compare. Path is the repository (or left path); Target is the right path for Paths. With a Host, Path is
/// a checkout on that machine, read over ssh.
/// </summary>
public sealed record DiffRequest(SourceKind Kind, string Path, string? Base = null, string? Target = null)
{
    /// <summary>The ssh destination of a remote checkout: an alias, user@host or ssh://user@host:port.</summary>
    public string? Host
    {
        get; init;
    }
    /// <summary>Local working trees and folders are watched; a remote checkout reloads on request.</summary>
    public bool Watches => Host is null && Kind is SourceKind.WorkingTree or SourceKind.Staged or SourceKind.Paths;
    /// <summary>The pull request a PullRequest request shows.</summary>
    public PullRequest? Pull
    {
        get; init;
    }
    /// <summary>
    /// A pull request to look up before it shows (from --pr): empty for the checkout's current branch, a number, or
    /// a pull request URL. Pull stays null until it is found.
    /// </summary>
    public string? PullQuery
    {
        get; init;
    }
    /// <summary>What Revisions show, such as a commit's subject, in place of "Base → Target".</summary>
    public string? Title
    {
        get; init;
    }
}
