using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Flint.Git;

namespace Flint;

public enum Page
{
    History, Changes
}
public enum PaletteMode
{
    Closed, QuickLaunch, Open, Branch, NewBranch, Workspaces, NewWorkspace, RenameWorkspace, Account
}
public enum ToastKind
{
    Info, Success, Error
}
public enum MenuKind
{
    Files, CommitFile, Branch, Remote, Tag, Stash, Worktree, MissingWorktree, RemoteFolder, Remotes
}
public sealed record Workspace(string Id, string Name, ImmutableArray<string> Repositories, string? Active = null, string Account = "");
public sealed record Persisted(ImmutableArray<Workspace> Workspaces, string ActiveWorkspace, bool Sidebar = true, ImmutableArray<string>? RecentPaths = null)
{
    [JsonIgnore]
    public ImmutableArray<string> Recent => RecentPaths ?? [];
}
public sealed record RepoTab(Snapshot Snapshot, Page Page = Page.History, string? Sha = null, CommitDetail? Detail = null,
    bool ChangesTab = false, string? File = null, bool Staged = false, ImmutableArray<string>? Selection = null,
    string Subject = "", string Body = "", bool Amend = false, string Patch = "", ImmutableArray<DiffLine>? Rows = null,
    string Find = "", bool Finding = false, int Match = 0, int Request = 0, float HistoryHeight = 380,
    ImmutableHashSet<string>? Collapsed = null, ImmutableArray<DiffMatch>? SearchResults = null, bool RevealHistory = false, bool RevealDiff = false,
    string? Anchor = null, string? Cursor = null, string Filter = "", string? SideSelected = null, bool LoadingMore = false,
    bool RevealFiles = false, float HistoryScroll = 0, bool RestoreScroll = false, float CommitFilesWidth = 300,
    float WorkingFilesWidth = 420, float UnstagedFraction = .5f, bool RevealSidebar = false)
{
    [JsonIgnore]
    public ImmutableArray<string> Selected => Selection ?? [];
    [JsonIgnore]
    public ImmutableArray<DiffLine> Lines => Rows ?? [];
    [JsonIgnore]
    public ImmutableArray<DiffMatch> Matches => SearchResults ?? [];
    /// <summary>Collapsed sections, branch folders and file-tree folders. Tags start collapsed, as in the original.</summary>
    [JsonIgnore]
    public ImmutableHashSet<string> Folders => Collapsed ?? SideTree.DefaultCollapsed;
    [JsonIgnore]
    public int ChangeCount => Snapshot.Staged.Length + Snapshot.Unstaged.Length;
}
/// <summary>A context menu at a window point; its entries are derived from the kind, target and current model.</summary>
public sealed record MenuState(Kumiko.UI.Point At, MenuKind Kind, string Target, bool Staged = false);
/// <summary>A confirmation or report. Request runs on Confirm; Details holds complete command or hook output.</summary>
public sealed record Dialog(string Title, string Message, string Path, GitRequest? Request = null, string ConfirmLabel = "OK",
    string CancelLabel = "Cancel", bool Destructive = false, string Details = "");
public sealed record Toast(int Id, string Text, ToastKind Kind, string Details = "");
/// <summary>A held Control/Option+Tab choice. Target activates on release; the overlay appears after a short hold.</summary>
public sealed record TabSwitcher(int Id, string Target, bool Visible = false);
public sealed record RemoteEditor(int Id, string Path, GitAction Action, string Target, string Name = "", string Url = "",
    bool Loading = false, string Error = "");
public sealed record FlintModel(ImmutableArray<Workspace> Workspaces, string ActiveWorkspace,
    ImmutableDictionary<string, RepoTab> Repositories, ImmutableArray<string> Known,
    bool Sidebar = true, PaletteMode Palette = PaletteMode.Closed, string Query = "", string WorkspaceName = "",
    string Account = "", bool Opening = false, string Error = "", string Notice = "", Dialog? Pending = null,
    bool Watching = true, int Sequence = 0, int Generation = 0, MenuState? Menu = null,
    ImmutableDictionary<string, string>? Running = null, ImmutableDictionary<string, int>? Loads = null,
    ImmutableArray<Toast>? Notices = null, ImmutableArray<GhAccount>? GitHub = null, ImmutableArray<string>? RecentPaths = null,
    string? PaletteTarget = null, int Highlight = 0, string? Armed = null, string Home = "", int OpenGeneration = 0, float SidebarSize = 290,
    string? Placeholder = null, string? OpenedFrom = null, ImmutableArray<string>? RecentTabs = null, TabSwitcher? Switcher = null,
    RemoteEditor? RemoteEditor = null)
{
    [JsonIgnore]
    public Workspace Workspace => Workspaces.FirstOrDefault(w => w.Id == ActiveWorkspace) ?? Workspaces[0];
    [JsonIgnore]
    public RepoTab? Current => Workspace.Active is { } path ? Repositories.Lookup(path) : null;
    [JsonIgnore]
    public Persisted Persistence => new(Workspaces, ActiveWorkspace, Sidebar, Recent);
    /// <summary>Label of the running operation per repository, for example "Pushing…".</summary>
    [JsonIgnore]
    public ImmutableDictionary<string, string> Operations => Running ?? ImmutableDictionary<string, string>.Empty;
    [JsonIgnore]
    public bool Busy => Opening || !Operations.IsEmpty;
    [JsonIgnore]
    public ImmutableArray<Toast> Toasts => Notices ?? [];
    [JsonIgnore]
    public ImmutableArray<GhAccount> Accounts => GitHub ?? [];
    [JsonIgnore]
    public ImmutableArray<string> Recent => RecentPaths ?? [];
    /// <summary>The active workspace's repositories, most recently used first, then the rest in tab-bar order.</summary>
    [JsonIgnore]
    public ImmutableArray<string> RecentOrder
    {
        get
        {
            var tabs = Workspace.Repositories;
            var visited = (RecentTabs ?? []).Where(tabs.Contains).ToImmutableArray();
            return [.. visited, .. tabs.Where(path => !visited.Contains(path))];
        }
    }
    /// <summary>The latest load generation per repository; results for older loads are ignored.</summary>
    [JsonIgnore]
    public ImmutableDictionary<string, int> Generations => Loads ?? ImmutableDictionary<string, int>.Empty;
    /// <summary>Removes the tab an open showed before Git answered, returning each workspace to its previous repository.</summary>
    public FlintModel WithoutPlaceholder() => Placeholder is not { } placeholder ? this : this with
    {
        Workspaces = [.. Workspaces.Select(w => w.Repositories.Contains(placeholder) ? w with
        {
            Repositories = w.Repositories.Remove(placeholder),
            Active = w.Active == placeholder ? OpenedFrom : w.Active
        } : w)],
        Placeholder = null,
        OpenedFrom = null
    };
    public string? BusyLabel(string path) => Operations.TryGetValue(path, out var label) ? label : null;
}
