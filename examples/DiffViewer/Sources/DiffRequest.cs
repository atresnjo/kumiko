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

/// <summary>What to compare. Path is the repository (or left path); Target is the right path for Paths.</summary>
public sealed record DiffRequest(SourceKind Kind, string Path, string? Base = null, string? Target = null)
{
    public bool Watches => Kind is SourceKind.WorkingTree or SourceKind.Staged or SourceKind.Paths;
    /// <summary>The pull request a PullRequest request shows.</summary>
    public PullRequest? Pull
    {
        get; init;
    }
    /// <summary>What Revisions show, such as a commit's subject, in place of "Base → Target".</summary>
    public string? Title
    {
        get; init;
    }
}
