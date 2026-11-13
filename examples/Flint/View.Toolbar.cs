using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>The original 54 px toolbar: vertical icon/label buttons, the centred head pill and the workspace button.</summary>
    void ToolbarView(float sidebarSpace)
    {
        var reveal = sidebarSpace / (SidebarPaneWidth + ResizeThickness);
        using var bar = C.Div(key: "toolbar").Row().FillWidth().Height(54).ItemsCenter().Gap(2)
            .Padding(new Edges(0, 10, 0, 8 + 70 * (1 - reveal)));
        var tab = Model.Current;
        var path = Model.Workspace.Active;
        var enabled = tab is not null && path is not null && Model.BusyLabel(path) is null;
        IconButton("Toggle sidebar", Icons.Sidebar, new Msg.ToggleSidebar(), "sidebar");
        using (C.Div(key: "gap").Width(10))
        {
        }
        ToolButton("Quick Launch", Icons.Launch, new Msg.ShowPalette(PaletteMode.QuickLaunch), "launch");
        ToolButton("Fetch", Icons.Fetch, new Msg.Run(new(GitAction.Fetch)), "fetch", enabled);
        ToolButton("Pull", Icons.Pull, new Msg.Run(new(GitAction.Pull)), "pull", enabled, tab?.Snapshot.Head.Behind ?? 0);
        ToolButton("Push", Icons.Push, new Msg.Run(new(GitAction.Push)), "push", enabled, tab?.Snapshot.Head.Ahead ?? 0);
        ToolButton("Stash", Icons.Stash, new Msg.Run(new(GitAction.Stash)), "stash", enabled);
        using (C.Div(key: "left").Grow())
        {
        }
        if (tab is not null)
            HeadPill(tab);
        using (C.Div(key: "pill-gap").Width(8))
        {
        }
        ToolButton("Branch", Icons.Branch, new Msg.ShowPalette(PaletteMode.NewBranch), "branch", tab is not null, name: "New Branch");
        using (C.Div(key: "right").Grow())
        {
        }
        ToolButton(Model.Workspace.Name, Icons.Layers, new Msg.ShowPalette(PaletteMode.Workspaces), "workspace", name: "Workspace: " + Model.Workspace.Name);
    }
    void HeadPill(RepoTab tab)
    {
        var head = tab.Snapshot.Head;
        var busy = Model.BusyLabel(tab.Snapshot.Path);
        using var pill = C.Div(key: "head").Width(340).MinWidth(160).Shrink().Height(40).Center().Gap(1).Rounded(7).Background(Pill);
        var hit = Ui.Button(pill, "Checkout branch", new Msg.ShowPalette(PaletteMode.Branch), true);
        if (hit.Hovered)
            pill.Background(Color.Hex(0xe6e6eb));
        if (hit.Focused && C.FocusVisible)
            pill.Border(Accent, 2);
        Label(busy ?? tab.Snapshot.Name, "repo", 13, weight: Weight.Semibold);
        using var line = C.Div(key: "line").Row().ItemsCenter().Gap(4).MaxWidth(320).Clip();
        C.Icon(Icons.Branch, 11, MutedStrong, key: "branch-icon");
        var branch = head.Detached ? "detached at " + head.Sha[..Math.Min(7, head.Sha.Length)] : head.Branch.Length > 0 ? head.Branch : "—";
        Label(branch, "branch", 11, MutedStrong);
        var track = (head.Ahead > 0 ? $"{head.Ahead}↑" : "") + (head.Behind > 0 ? $" {head.Behind}↓" : "");
        if (track.Length > 0)
            Label(track.Trim(), "track", 11, Accent, Weight.Semibold);
        if (Model.Workspace.Account.Length > 0)
        {
            Label("·", "dot", 11, MutedStrong);
            C.Icon(Icons.User, 10, MutedStrong, key: "user-icon");
            Label(Model.Workspace.Account, "account", 11, MutedStrong);
        }
    }
    /// <summary>The original segmented tab bar: equal-width tabs, a dirty dot and a close button per repository.</summary>
    void TabBar()
    {
        using var bar = C.Div(key: "tabs").Row().FillWidth().Height(32).ItemsCenter().Gap(6).Px(8);
        var repositories = Model.Workspace.Repositories;
        var offset = HistoryOffset();
        using (C.Div(key: "repository-tabs").Row().Flex().MinWidth(0).Height(28).ItemsCenter().Padding(2).Gap(2).Rounded(8).Background(Palette.TabBar).Clip())
        {
            if (repositories.IsEmpty)
                using (C.Div(key: "empty").Flex().Center())
                    Label("No repositories in this workspace — press ⌘P to open one", "label", 12, MutedStrong);
            foreach (var path in repositories)
            {
                var active = path == Model.Workspace.Active;
                var name = Model.Repositories.Lookup(path)?.Snapshot.Name ?? System.IO.Path.GetFileName(path);
                using var tab = C.Div(key: path).Row().Flex().MinWidth(60).MaxWidth(repositories.Length == 1 ? 260 : 220).Height(24).ItemsCenter().Rounded(6);
                if (active)
                    tab.Background(Canvas).Shadow(2, 1, Color.Black.Alpha(.12f));
                using (var select = C.Div(key: "select").Flex().MinWidth(0).FillHeight().Row().ItemsCenter().Justify(Justify.Center).Gap(4).Padding(new Edges(0, 2, 0, 10)).Clip())
                {
                    var hit = Ui.Button(select, "Repository: " + name, new Msg.SelectRepo(path, offset), true);
                    if (!active && hit.Hovered)
                        tab.Background(Color.Hex(0xe2e2e7));
                    if ((hit.Focused && C.FocusVisible) || Model.Switcher?.Target == path)
                        tab.Border(Accent, 2);
                    Label(name, "name", 12, active ? Text : MutedStrong, active ? Weight.Medium : Weight.Regular);
                    if (Model.Repositories.Lookup(path) is { ChangeCount: > 0 })
                        using (C.Div(key: "dirty").Width(6).Height(6).Rounded(3).Background(Faint))
                        {
                        }
                }
                using (var close = C.Div(key: "close").Width(16).Height(16).Center().Rounded(4).Margin(new Edges(0, 6, 0, 0)))
                {
                    var hit = Ui.Button(close, "Close repository: " + path, new Msg.CloseRepo(path), true);
                    close.Opacity(hit.Hovered ? 1 : .6f);
                    if (hit.Hovered)
                        close.Background(Color.Hex(0xd4d4da));
                    C.Icon(Icons.Close, 10, Muted, key: "icon");
                }
            }
        }
        IconButton("Open Repository", Icons.Plus, new Msg.ShowPalette(PaletteMode.Open), "add", 26, 15);
    }
}
