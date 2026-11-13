using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;

namespace Flint;

public abstract partial record Msg
{
    public sealed record ShowPalette(PaletteMode Mode, string? Target = null) : Msg;
    public sealed record QueryChanged(string Text) : Msg;
    public sealed record Highlight(int Index) : Msg;
    public sealed record Arm(string? Action) : Msg;
    public sealed record Open(string Path) : Msg;
    public sealed record Loaded(string Workspace, Snapshot Snapshot, string? Original = null, bool Mutation = false, string Notice = "",
        int Generation = 0, string? RemovedPath = null, GitRequest? Next = null, GitAction? Action = null) : Msg;
    public sealed record StatusLoaded(string Path, int Generation, WorkingStatus Status) : Msg;
    public sealed record Refresh(string? Path = null, bool More = false, bool StatusOnly = false) : Msg;
    public sealed record Failed(string Error, string? Path = null, int Request = -1, int Generation = -1) : Msg;
    /// <summary>A Git mutation failed; Output is everything Git and its hooks printed.</summary>
    public sealed record ActionFailed(string Path, GitRequest Request, string Error, string Output, bool Hook = false) : Msg;
    /// <summary>Scroll is the leaving tab's history offset, read by the view, so each repository keeps its position.</summary>
    public sealed record SelectRepo(string Path, float? Scroll = null) : Msg;
    public sealed record CloseRepo(string Path) : Msg;
    public sealed record Navigate(Page Page) : Msg;
    public sealed record SelectCommit(string Sha) : Msg;
    public sealed record DetailLoaded(string Path, int Request, CommitDetail Detail, string Patch, ImmutableArray<DiffLine> Lines) : Msg;
    public sealed record SelectFile(string? Path, bool Staged = false, bool Extend = false, bool Range = false) : Msg;
    public sealed record PatchLoaded(string Path, int Request, string Patch, ImmutableArray<DiffLine> Lines) : Msg;
    public sealed record SetChangesTab(bool Changes) : Msg;
    public sealed record EditSubject(string Text) : Msg;
    public sealed record EditBody(string Text) : Msg;
    public sealed record SetAmend(bool On) : Msg;
    public sealed record AmendLoaded(string Path, string Subject, string Body) : Msg;
    public sealed record SetFind(bool IsOpen) : Msg;
    public sealed record FindChanged(string Text) : Msg;
    public sealed record StepMatch(int Delta) : Msg;
    /// <summary>Moves the commit selection; int.MinValue/MaxValue jump to the first/last loaded commit.</summary>
    public sealed record StepCommit(int Delta) : Msg;
    public sealed record LoadMore(string Path) : Msg;
    public sealed record ResizeHistory(float Height) : Msg;
    public sealed record ResizeCommitFiles(float Width) : Msg;
    public sealed record ResizeSidebar(float Width) : Msg;
    public sealed record ResizeWorkingFiles(float Width) : Msg;
    public sealed record ResizeUnstaged(float Fraction) : Msg;
    public sealed record ToggleFolder(string Folder) : Msg;
    public sealed record SetFolder(string Folder, bool Expanded) : Msg;
    public sealed record SelectAllFiles : Msg;
    /// <summary>Moves the file-tree cursor in the focused section; Extend keeps the Shift anchor.</summary>
    public sealed record StepFile(int Delta, bool Extend = false, bool Staged = false) : Msg;
    public sealed record Run(GitRequest Request) : Msg;
    public sealed record EditRemote(GitAction Action, string Target = "") : Msg;
    public sealed record RemoteNameChanged(string Text) : Msg;
    public sealed record RemoteUrlChanged(string Text) : Msg;
    public sealed record RemoteUrlLoaded(int Id, string Url, string Error = "") : Msg;
    public sealed record SaveRemote(string Upstream = "") : Msg;
    /// <summary>Acts on the selected rows of a file section: stage/unstage, stash or revert.</summary>
    public sealed record RunSelection(GitAction Action, bool Staged) : Msg;
    public sealed record Ask(Dialog Dialog) : Msg;
    public sealed record Confirm : Msg;
    public sealed record Cancel : Msg;
    public sealed record ToggleSidebar : Msg;
    public sealed record CreateWorkspace(string Name) : Msg;
    public sealed record SelectWorkspace(string Id) : Msg;
    public sealed record RenameWorkspace(string Id, string Name) : Msg;
    public sealed record DeleteWorkspace(string Id) : Msg;
    public sealed record SetAccount(string Id, string Login) : Msg;
    public sealed record AccountsLoaded(ImmutableArray<GhAccount> Accounts) : Msg;
    public sealed record Discovered(ImmutableArray<string> Paths) : Msg;
    public sealed record OpenMenu(Kumiko.UI.Point At, MenuKind Kind, string Target, bool Staged = false) : Msg;
    public sealed record SwitchTab(int Index, bool Relative = false, float? Scroll = null) : Msg;
    /// <summary>Starts or steps a held recent-tab switch; Delta is 1 for Tab and -1 for Shift+Tab.</summary>
    public sealed record CycleTabs(int Delta) : Msg;
    public sealed record CommitTabs(float? Scroll = null) : Msg;
    public sealed record ShowSwitcher(int Id) : Msg;
    public sealed record SidebarFilter(string Text) : Msg;
    public sealed record FocusCurrentBranch : Msg;
    public sealed record CollapseSidebar : Msg;
    public sealed record SidebarRevealed(string Path) : Msg;
    public sealed record SidebarSelect(string? Id, string? Sha = null) : Msg;
    public sealed record Launch(LaunchRequest Request) : Msg;
    public sealed record Notify(string Text, ToastKind Kind = ToastKind.Info) : Msg;
    public sealed record DismissToast(int Id) : Msg;
    public sealed record Revealed(string Path, bool History) : Msg;
    public sealed record Start : Msg;
    public sealed record Saved : Msg;
}
