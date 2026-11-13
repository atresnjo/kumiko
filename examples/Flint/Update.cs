using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp : IKumiko<FlintModel, Msg>
{
    public static Kumiko.Runtime.NavigationRoot<Msg> Root(FlintModel model) => Kumiko.Runtime.NavigationRoot<Msg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new FlintView(model).Map<Msg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static FlintModel Init => new([new("default", "Default", [])], "default", ImmutableDictionary<string, RepoTab>.Empty, []);
    public static (FlintModel, Cmd<Msg>) Update(FlintModel m, Msg msg)
    {
        var (next, command) = Transition(m, msg);
        var model = Visited(next);
        if (model.Persistence != m.Persistence && msg is not Msg.Saved)
            return (model, Cmd<Msg>.Batch(command, Save(model.Persistence)));
        return (model, command);
    }
    static (FlintModel, Cmd<Msg>) Transition(FlintModel m, Msg msg) => msg switch
    {
        Msg.ShowPalette p => ShowPalette(m, p.Mode, p.Target),
        Msg.QueryChanged q => Pure(m with { Query = q.Text, Highlight = 0, Armed = null }),
        Msg.Highlight h => Pure(m with { Highlight = Math.Max(0, h.Index), Armed = m.Highlight == h.Index ? m.Armed : null }),
        Msg.Arm a => Pure(m with { Armed = a.Action }),
        Msg.Open o => Open(m, o.Path),
        Msg.Loaded l => Loaded(m, l),
        Msg.StatusLoaded s => StatusLoaded(m, s),
        Msg.Refresh r => Refresh(m, r.Path, r.More, r.StatusOnly),
        Msg.Failed f => Failed(m, f),
        Msg.ActionFailed f => ActionFailed(m, f),
        Msg.SelectRepo s => Switched(Activate(Restore(SelectRepo(Remember(m, s.Scroll), s.Path)) with { Palette = PaletteMode.Closed }), s.Path),
        Msg.CloseRepo c => Pure(CloseRepo(m, c.Path)),
        Msg.Navigate n => Navigate(m, n.Page),
        Msg.SelectCommit s => SelectCommit(Edit(m, r => r with { Page = Page.History }), s.Sha),
        Msg.DetailLoaded d => DetailLoaded(m, d),
        Msg.SelectFile f => SelectFile(m, f),
        Msg.PatchLoaded p => PatchLoaded(m, p),
        Msg.SetChangesTab t => Pure(Edit(m, r => r with { ChangesTab = t.Changes })),
        Msg.EditSubject e => Pure(Edit(m, r => r with { Subject = e.Text })),
        Msg.EditBody e => Pure(Edit(m, r => r with { Body = e.Text })),
        Msg.SetAmend a => Amend(m, a.On),
        Msg.AmendLoaded a => Pure(m.Repositories.TryGetValue(a.Path, out var amended) && amended is { Amend: true, Subject.Length: 0, Body.Length: 0 }
            ? m with
            {
                Repositories = m.Repositories.SetItem(a.Path, amended with
                {
                    Subject = a.Subject,
                    Body = a.Body
                })
            } : m),
        Msg.SetFind f => Pure(Edit(m, r => r with { Finding = f.IsOpen, Match = 0 })),
        Msg.FindChanged f => Pure(Edit(m, r => r with { Find = f.Text, SearchResults = Diff.Matches(r.Lines, f.Text), Match = 0, RevealDiff = true })),
        Msg.StepMatch s => Pure(Edit(m, r => StepMatch(r, s.Delta))),
        Msg.StepCommit s => StepCommit(m, s.Delta),
        Msg.LoadMore l => LoadMore(m, l.Path),
        Msg.ResizeHistory r => Pure(Edit(m, tab => tab with { HistoryHeight = Math.Clamp(r.Height, 120, 1400) })),
        Msg.ResizeCommitFiles r => Pure(Edit(m, tab => tab with { CommitFilesWidth = MathF.Max(180, r.Width) })),
        Msg.ResizeSidebar r => Pure(m with { SidebarSize = MathF.Max(220, r.Width) }),
        Msg.ResizeWorkingFiles r => Pure(Edit(m, tab => tab with { WorkingFilesWidth = MathF.Max(260, r.Width) })),
        Msg.ResizeUnstaged r => Pure(Edit(m, tab => tab with { UnstagedFraction = Math.Clamp(r.Fraction, 0, 1) })),
        Msg.ToggleFolder f => Pure(Edit(m, r => r with { Collapsed = r.Folders.Contains(f.Folder) ? r.Folders.Remove(f.Folder) : r.Folders.Add(f.Folder) })),
        Msg.SetFolder f => Pure(Edit(m, r => r with { Collapsed = f.Expanded ? r.Folders.Remove(f.Folder) : r.Folders.Add(f.Folder) })),
        Msg.SelectAllFiles => Pure(Edit(m, SelectAll)),
        Msg.StepFile s => StepFile(m, s.Delta, s.Extend, s.Staged),
        Msg.Run r => Request(m with { Menu = null }, r.Request),
        Msg.EditRemote e => EditRemote(m, e),
        Msg.RemoteNameChanged e => Pure(m.RemoteEditor is { } editor ? m with { RemoteEditor = editor with { Name = e.Text, Error = "" } } : m),
        Msg.RemoteUrlChanged e => Pure(m.RemoteEditor is { } editor ? m with { RemoteEditor = editor with { Url = e.Text, Error = "" } } : m),
        Msg.RemoteUrlLoaded e => Pure(m.RemoteEditor is { } editor && editor.Id == e.Id
            ? m with
            {
                RemoteEditor = editor with
                {
                    Url = e.Url,
                    Loading = false,
                    Error = e.Error
                }
            } : m),
        Msg.SaveRemote s => SaveRemote(m, s.Upstream),
        Msg.RunSelection r => RunSelection(m with { Menu = null }, r.Action, r.Staged),
        Msg.Ask a => Pure(m with { Pending = a.Dialog, Menu = null, Palette = PaletteMode.Closed }),
        Msg.Confirm => m.Pending is { Request: { } request } p ? Execute(m with { Pending = null }, p.Path, request) : Pure(m with { Pending = null }),
        Msg.Cancel => Pure(m with { Pending = null, Menu = null, Palette = PaletteMode.Closed, Error = "", Armed = null, Switcher = null, RemoteEditor = null }),
        Msg.ToggleSidebar => Pure(m with { Sidebar = !m.Sidebar }),
        Msg.CreateWorkspace w => CreateWorkspace(m, w.Name),
        Msg.SelectWorkspace w => SelectWorkspace(m, w.Id),
        Msg.RenameWorkspace w => Pure(RenameWorkspace(m, w.Id, w.Name)),
        Msg.DeleteWorkspace w => DeleteWorkspace(m, w.Id),
        Msg.SetAccount a => Pure(SetAccount(m, a.Id, a.Login)),
        Msg.AccountsLoaded a => Pure(m with { GitHub = a.Accounts }),
        Msg.Discovered d => Pure(m with { Known = d.Paths }),
        Msg.OpenMenu menu => Pure(OpenMenu(m, menu)),
        Msg.SwitchTab tab => SwitchTab(Remember(m, tab.Scroll), tab.Index, tab.Relative),
        Msg.CycleTabs c => Pure(CycleTabs(m, c.Delta)),
        Msg.CommitTabs c => CommitTabs(m, c.Scroll),
        Msg.ShowSwitcher s => Pure(m.Switcher?.Id == s.Id ? m with { Switcher = m.Switcher with { Visible = true } } : m),
        Msg.SidebarFilter f => Pure(Edit(m, r => r with { Filter = f.Text })),
        Msg.FocusCurrentBranch => Pure(Edit(m, FocusCurrentBranch)),
        Msg.CollapseSidebar => Pure(Edit(m, CollapseSidebar)),
        Msg.SidebarRevealed r => Pure(m.Repositories.TryGetValue(r.Path, out var revealed)
            ? m with
            {
                Repositories = m.Repositories.SetItem(r.Path, revealed with
                {
                    RevealSidebar = false
                })
            } : m),
        Msg.SidebarSelect s => s.Sha is { } sha ? SelectCommit(Edit(m, r => r with { SideSelected = s.Id, Page = Page.History }), sha)
            : Pure(Edit(m, r => r with { SideSelected = s.Id })),
        Msg.Launch l => (m with { Menu = null, Palette = PaletteMode.Closed }, Launch(l.Request)),
        Msg.Notify n => Pure(n.Text.Length == 0 ? m : Notify(m, n.Text, n.Kind)),
        Msg.DismissToast d => Pure(m with { Notices = m.Toasts.RemoveAll(t => t.Id == d.Id) }),
        Msg.Revealed r => Pure(Revealed(m, r.Path, r.History)),
        Msg.Start => Start(m),
        Msg.Saved => Pure(m),
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    };
    static (FlintModel, Cmd<Msg>) Pure(FlintModel m) => (m, Cmd<Msg>.None);
    static FlintModel Edit(FlintModel m, Func<RepoTab, RepoTab> change) => m.Current is { } tab
        ? m with
        {
            Repositories = m.Repositories.SetItem(tab.Snapshot.Path, change(tab))
        } : m;
    static FlintModel EditWorkspace(FlintModel m, string id, Func<Workspace, Workspace> change) =>
        m.Workspaces.FirstOrDefault(w => w.Id == id) is { } workspace ? m with
        {
            Workspaces = m.Workspaces.Replace(workspace, change(workspace))
        } : m;
    static FlintModel ChangeWorkspace(FlintModel m, Func<Workspace, Workspace> change) => EditWorkspace(m, m.Workspace.Id, change) with
    {
        Palette = PaletteMode.Closed
    };
    /// <summary>Adds a toast; at most four stay visible, and subscriptions dismiss each after its delay.</summary>
    static FlintModel Notify(FlintModel m, string text, ToastKind kind, string details = "") => m with
    {
        Notices = [.. m.Toasts.TakeLast(3), new(m.Sequence + 1, text, kind, details)],
        Sequence = m.Sequence + 1
    };
    static (FlintModel, Cmd<Msg>) ShowPalette(FlintModel m, PaletteMode mode, string? target)
    {
        var name = m.Workspaces.FirstOrDefault(w => w.Id == (target ?? m.ActiveWorkspace))?.Name ?? "";
        m = m with
        {
            Palette = mode,
            PaletteTarget = target,
            Query = mode == PaletteMode.RenameWorkspace ? name : "",
            Highlight = 0,
            Armed = null,
            Error = "",
            Menu = null
        };
        return (m, mode switch
        {
            PaletteMode.QuickLaunch or PaletteMode.Open => Discover(TimeSpan.Zero),
            PaletteMode.Account => Accounts(),
            _ => Cmd<Msg>.None
        });
    }
    static Cmd<Msg> Save(Persisted value) => Cmd<Msg>.Debounce<IWorkspaceStorage>("save-workspaces", TimeSpan.FromMilliseconds(100),
        async (services, token) =>
        {
            await services.Save(value, token).ConfigureAwait(false);
            return new Msg.Saved();
        }, static e => new Msg.Failed(e.Message));
    static Cmd<Msg> Discover(TimeSpan delay) => Cmd<Msg>.Debounce<IRepositoryDiscovery>("discover", delay,
        async (services, token) => new Msg.Discovered(await services.Discover(token).ConfigureAwait(false)), static e => new Msg.Failed(e.Message));
    static Cmd<Msg> Accounts() => Cmd<Msg>.Debounce<IGitHubAccounts>("gh-accounts", TimeSpan.Zero,
        async (services, token) => new Msg.AccountsLoaded(await services.List(token).ConfigureAwait(false)), static e => new Msg.Failed(e.Message));
    static Cmd<Msg> Launch(LaunchRequest request) => Cmd<Msg>.Run<ILauncher>("launch-" + request.Kind, async (services, token) =>
    {
        await services.Launch(request, token).ConfigureAwait(false);
        return new Msg.Saved();
    }, static e => new Msg.Failed(e.Message));
    static (FlintModel, Cmd<Msg>) Failed(FlintModel m, Msg.Failed f)
    {
        if (f.Generation >= 0 && f.Path is null && f.Generation != m.OpenGeneration)
            return Pure(m);
        if (f.Generation >= 0 && f.Path is { } path && m.Generations.TryGetValue(path, out var current) && current != f.Generation)
            return Pure(m);
        if (f.Request >= 0 && (f.Path is null || !m.Repositories.TryGetValue(f.Path, out var r) || r.Request != f.Request))
            return Pure(m);
        if (f.Generation >= 0 && f.Path is null)
            m = m.WithoutPlaceholder();
        m = m with
        {
            Opening = f.Generation >= 0 && f.Path is null ? false : m.Opening,
            Error = f.Error
        };
        if (f.Path is { } loading && m.Repositories.TryGetValue(loading, out var tab) && tab.LoadingMore)
            m = m with
            {
                Repositories = m.Repositories.SetItem(loading, tab with
                {
                    LoadingMore = false
                })
            };
        return Pure(Notify(m, f.Error, ToastKind.Error));
    }
    static RepoTab StepMatch(RepoTab r, int delta)
    {
        var count = r.Matches.Length;
        return r with
        {
            Match = count == 0 ? 0 : (r.Match + delta + count) % count,
            RevealDiff = true
        };
    }
}
