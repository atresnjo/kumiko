using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;

namespace DiffViewer;

internal abstract partial record Msg
{
    public sealed record Loaded(long Generation, DiffDocument Document) : Msg;
    public sealed record Failed(long Generation, string Error) : Msg;
    public sealed record Reload : Msg;
    /// <summary>The watched workspace changed on disk.</summary>
    public sealed record Changed : Msg;
    public sealed record SetMode(ViewMode Mode) : Msg;
    public sealed record ToggleWhitespace : Msg;
    public sealed record ToggleSidebar : Msg;
    public sealed record ToggleZen : Msg;
    /// <summary>The window left full screen on its own (its green button or ⌃⌘F), which ends zen mode.</summary>
    public sealed record LeftFullScreen : Msg;
    /// <summary>Moves the cursor by rows; reveals it with the least scrolling.</summary>
    public sealed record Move(int Rows) : Msg;
    public sealed record Top : Msg;
    public sealed record Bottom : Msg;
    public sealed record NextHunk(int Direction) : Msg;
    public sealed record NextFile(int Direction) : Msg;
    /// <summary>Puts the cursor on a row without scrolling, as a click does.</summary>
    public sealed record Point(int Row) : Msg;
    /// <summary>
    /// The pointer went down on a character of a shown line (Line indexes FileDiff.Lines; Left is a split row's
    /// left side): one click starts a selection there, a double or triple click selects the word or the line, and
    /// Extend (Shift) moves the head of a selection in the same line.
    /// </summary>
    public sealed record TextPress(int Row, int Line, int Column, int Clicks, bool Left, bool Extend) : Msg;
    public sealed record TextDrag(int Column) : Msg;
    /// <summary>The pointer was released: an empty selection clears, otherwise every occurrence is found.</summary>
    public sealed record TextRelease : Msg;
    public sealed record SelectionStep(int Direction) : Msg;
    public sealed record ClearSelection : Msg;
    public sealed record SelectFile(int File) : Msg;
    /// <summary>Collapses or expands the cursor's file (-1) or a given file.</summary>
    public sealed record ToggleFile(int File) : Msg;
    public sealed record SetAllCollapsed(bool Collapsed) : Msg;
    public sealed record SetHelp(bool Open) : Msg;
    public sealed record OpenFinder : Msg;
    public sealed record FinderQuery(string Query) : Msg;
    public sealed record FinderStep(int Delta) : Msg;
    public sealed record FinderAccept : Msg;
    public sealed record OpenSearch : Msg;
    public sealed record SearchQuery(string Query) : Msg;
    public sealed record SearchStep(int Direction) : Msg;
    public sealed record CloseOverlay : Msg;
    public sealed record Navigate(HistoryRequest Request) : Msg;
    /// <summary>Opens the project palette with a starting query; "&gt;" lists commands only.</summary>
    public sealed record OpenQuick(string Query = "") : Msg;
    public sealed record QuickQuery(string Query) : Msg;
    public sealed record QuickStep(int Delta) : Msg;
    /// <summary>Runs the highlighted item, or the item at an index when one is given (a click).</summary>
    public sealed record QuickAccept(int Index = -1) : Msg;
    public sealed record ProjectsFound(ImmutableArray<Project> Projects, bool Scanned) : Msg;
    public sealed record Rescan : Msg;
    /// <summary>Shows a repository's working tree, replacing the open diff.</summary>
    public sealed record OpenProject(string Path) : Msg;
    public sealed record SetSource(SourceKind Kind) : Msg;
    /// <summary>A pull request search finished; results of an older search are ignored.</summary>
    public sealed record PullsFound(long Generation, ImmutableArray<PullRequest> Pulls, string? Error) : Msg;
    /// <summary>The cached pull requests reached their age; the palette searches again when it shows them.</summary>
    public sealed record PullsExpired(long Generation) : Msg;
    public sealed record RefreshPulls : Msg;
    public sealed record OpenPull(PullRequest Pull) : Msg;
    /// <summary>Looks up the pull request the request's PullQuery names (from --pr), then shows it.</summary>
    public sealed record FindPull : Msg;
    public sealed record PullFound(long Generation, PullRequest Pull) : Msg;
    /// <summary>A background load of a listed pull request finished; no document means it failed.</summary>
    public sealed record PullPrefetched(string Key, DiffDocument? Document) : Msg;
    public sealed record CommitsFound(string Project, ImmutableArray<Commit> Commits) : Msg;
    public sealed record HostsFound(ImmutableArray<SshHost> Hosts) : Msg;
    /// <summary>The open repository's worktrees, remote and pull requests were read; Project names the repository.</summary>
    public sealed record ContextFound(string Project, ImmutableArray<Worktree> Worktrees, GitRemote? Remote, ImmutableArray<PullRequest> Pulls, string? Error) : Msg;
    /// <summary>Fills the palette's query with <c>host:~/</c>, keeping it open for the path.</summary>
    public sealed record PickHost(string Alias) : Msg;
    /// <summary>A search for repositories on a host finished; an error says why it failed.</summary>
    public sealed record HostReposFound(string Host, ImmutableArray<string> Paths, string? Error) : Msg;
    /// <summary>Searches a host for repositories again, keeping the palette open.</summary>
    public sealed record SearchHost(string Host) : Msg;
    /// <summary>Shows a commit of the open project against its first parent.</summary>
    public sealed record OpenCommit(Commit Commit) : Msg;
    /// <summary>A background save finished; nothing changes.</summary>
    public sealed record Saved : Msg;
}
