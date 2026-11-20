using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>The ⌘P file finder: a query over paths and the highlighted match.</summary>
internal sealed record Finder(string Query = "", int Selected = 0);

/// <summary>⌘F search over shown lines. Matches are rows of the current layout, in order.</summary>
internal sealed record Search
{
    public string Query { get; init; } = "";
    public ImmutableArray<int> Found { get; init; } = [];
    public int Current
    {
        get; init;
    }
}

/// <summary>
/// Text selected in one shown line and every other place it shows. Row and Line say where the selection is (Line
/// indexes FileDiff.Lines), Anchor and Head are its ends as characters of that line. Found lists the occurrences in
/// order once the pointer is released; Current is the one the selection sits on.
/// </summary>
internal sealed record Selection
{
    public int Row
    {
        get; init;
    }
    public int Line
    {
        get; init;
    }
    public int Anchor
    {
        get; init;
    }
    public int Head
    {
        get; init;
    }
    /// <summary>The selection began on the left side of a split row, which shows a context line on both sides.</summary>
    public bool Left
    {
        get; init;
    }
    /// <summary>The pointer is still down; occurrences are found when it is released.</summary>
    public bool Dragging
    {
        get; init;
    }
    public string Text { get; init; } = "";
    public ImmutableArray<Occurrence> Found { get; init; } = [];
    public int Current
    {
        get; init;
    }
    public int Start => Math.Min(Anchor, Head);
    public int End => Math.Max(Anchor, Head);
}

/// <summary>
/// Open pull requests of every gh account. A list is kept for the configured time, then marked stale and searched
/// again the next time the palette shows it; Generation drops results of a superseded search.
/// </summary>
internal sealed record PullList
{
    public ImmutableArray<PullRequest> Items { get; init; } = [];
    public long Generation
    {
        get; init;
    }
    public bool Loading
    {
        get; init;
    }
    public bool Loaded
    {
        get; init;
    }
    public bool Stale
    {
        get; init;
    }
    public string? Error
    {
        get; init;
    }
}

internal sealed record PullPrefetch(DiffDocument? Document, bool Failed);

/// <summary>Zen mode's file, by path so it survives reloads, and the view mode to return to.</summary>
internal sealed record ZenView(string Path, ViewMode Return);

/// <summary>The Git checkouts found on a host while the palette shows it: searching, found, or the reason it failed.</summary>
internal sealed record RemoteRepos(ImmutableArray<string> Paths, bool Loading, string? Error);

internal sealed record Model
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("diff");
    public DiffRequest Request { get; init; } = new(SourceKind.WorkingTree, ".");
    public DiffDocument Document { get; init; } = DiffDocument.Empty;
    /// <summary>Increments for every load; results of older loads are ignored.</summary>
    public long Generation
    {
        get; init;
    }
    public bool Loading { get; init; } = true;
    public string? Error
    {
        get; init;
    }
    public ViewMode Mode { get; init; } = ViewMode.Unified;
    public bool IgnoreWhitespace
    {
        get; init;
    }
    /// <summary>Paths of collapsed files, so collapse survives reloads.</summary>
    public ImmutableSortedSet<string> Collapsed { get; init; } = ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public RowLayout Layout { get; init; } = RowLayout.Empty;
    public int Cursor
    {
        get; init;
    }
    /// <summary>Changes whenever the cursor should be scrolled into view; see <see cref="VirtualReveal"/>.</summary>
    public long Reveal
    {
        get; init;
    }
    public VirtualRevealMode RevealMode
    {
        get; init;
    }
    public bool Help
    {
        get; init;
    }
    public Finder? Finder
    {
        get; init;
    }
    public Search? Search
    {
        get; init;
    }
    public Selection? Selection
    {
        get; init;
    }
    public bool SidebarHidden
    {
        get; init;
    }
    /// <summary>Zen mode: full screen with the two sides of one file and nothing else; null while it is off.</summary>
    public ZenView? Zen
    {
        get; init;
    }
    /// <summary>
    /// The pending load changes how much of a file is shown, as entering or leaving zen mode does, so the cursor's
    /// line moves to another row and is scrolled back into view once loaded. Watcher reloads leave scrolling alone.
    /// </summary>
    public bool RevealLoaded
    {
        get; init;
    }
    public QuickOpen? Quick
    {
        get; init;
    }
    /// <summary>gh and the GitHub API; null leaves pull requests out of the palette.</summary>
    public GitHubConfig? GitHub
    {
        get; init;
    }
    public PullList Pulls { get; init; } = new();
    /// <summary>
    /// Pull requests loaded in the background while the palette shows them, by <see cref="DiffApp.PrefetchKey"/>:
    /// loading (no document), loaded, or failed until the pull request changes.
    /// </summary>
    public ImmutableDictionary<string, PullPrefetch> Prefetched { get; init; } = ImmutableDictionary<string, PullPrefetch>.Empty.WithComparers(StringComparer.Ordinal);
    /// <summary>The newest commits of <see cref="CommitsOf"/>, read each time the palette opens.</summary>
    public ImmutableArray<Commit> Commits { get; init; } = [];
    public string CommitsOf { get; init; } = "";
    /// <summary>The home folder whose ~/.ssh/config lists hosts in the palette; null (offscreen runs) lists none.</summary>
    public string? SshHome
    {
        get; init;
    }
    /// <summary>The SSH hosts, read again each time the palette opens.</summary>
    public ImmutableArray<SshHost> SshHosts { get; init; } = [];
    /// <summary>Repositories found on each host typed in the palette, searched once each time the palette opens.</summary>
    public ImmutableDictionary<string, RemoteRepos> HostRepos { get; init; } = ImmutableDictionary<string, RemoteRepos>.Empty.WithComparers(StringComparer.Ordinal);
    /// <summary>Where projects are cached and recent ones saved; null keeps the palette to the open project.</summary>
    public ProjectStore? Store
    {
        get; init;
    }
    public ImmutableArray<Project> Projects { get; init; } = [];
    /// <summary>Project paths, most recently opened first.</summary>
    public ImmutableArray<string> Recent { get; init; } = [];
    /// <summary>The cache has been read; a scan starts the first time the palette opens and again on Rescan.</summary>
    public bool Cached
    {
        get; init;
    }
    public bool Scanning
    {
        get; init;
    }
    public bool Scanned
    {
        get; init;
    }
    /// <summary>The open repository's top folder once loaded, otherwise the requested path.</summary>
    public string ProjectPath => Document.Root.Length > 0 && Request.Kind is not (SourceKind.Paths or SourceKind.Demo or SourceKind.PullRequest) ? Document.Root : Request.Path;
    /// <summary>The open project where it is: ProjectPath on Request.Host, if any.</summary>
    public Place ProjectPlace => new(ProjectPath, Request.Host);
    /// <summary>How the palette and the recent list name the open project: its path, or host:path for a remote one.</summary>
    public string ProjectId => ProjectPlace.Id;

    public FileDiff? CursorFile => Cursor >= 0 && Cursor < Layout.Count ? Document.Files[Layout.Rows[Cursor].File] : null;
    public int CursorFileIndex => Cursor >= 0 && Cursor < Layout.Count ? Layout.Rows[Cursor].File : -1;
}
