using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>An inline row action shown on the highlighted row; Confirm asks for a second press.</summary>
    sealed record RowAction(string Id, string Label, string Icon, string Hint, Msg Message, bool Confirm = false);
    /// <summary>A palette result. KeepOpen items lead to another palette mode instead of closing it.</summary>
    sealed record LaunchItem(string Id, string Label, string Detail, string Group, string Icon, Msg Message, string Shortcut = "",
        bool KeepOpen = false, ImmutableArray<RowAction>? RowActions = null)
    {
        public ImmutableArray<RowAction> Actions => RowActions ?? [];
    }
    string Tilde(string path) => Model.Home.Length > 0 && path.StartsWith(Model.Home, StringComparison.Ordinal) ? "~" + path[Model.Home.Length..] : path;
    /// <summary>The original's repository groups: open tabs, other workspaces, recent and discovered repositories.</summary>
    ImmutableArray<LaunchItem> RepositoryItems()
    {
        var items = ImmutableArray.CreateBuilder<LaunchItem>();
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var workspace = Model.Workspace;
        void Add(string path, string group)
        {
            if (!seen.Add(path))
                return;
            var open = workspace.Repositories.Contains(path);
            var elsewhere = open ? null : Model.Workspaces.FirstOrDefault(w => w.Id != workspace.Id && w.Repositories.Contains(path));
            items.Add(new("repo:" + path, System.IO.Path.GetFileName(path), elsewhere is null ? Tilde(path) : $"{Tilde(path)} · open in {elsewhere.Name}", group,
                open ? Icons.Commits : elsewhere is not null ? Icons.Layers : Icons.Folder, open ? new Msg.SelectRepo(path) : new Msg.Open(path)));
        }
        foreach (var path in workspace.Repositories)
            Add(path, "Open tabs");
        foreach (var other in Model.Workspaces.Where(w => w.Id != workspace.Id))
            foreach (var path in other.Repositories)
                Add(path, "Other workspaces");
        foreach (var path in Model.Recent)
            Add(path, "Recent");
        foreach (var path in Model.Known)
            Add(path, "Repositories");
        return items.ToImmutable();
    }
    ImmutableArray<LaunchItem> CommandItems()
    {
        var items = ImmutableArray.CreateBuilder<LaunchItem>();
        var workspace = Model.Workspace;
        if (workspace.Active is { } repo)
        {
            var name = System.IO.Path.GetFileName(repo);
            items.Add(new("cmd:changes", "Show Local Changes", "", "Commands", Icons.Changes, new Msg.Navigate(Page.Changes), "⌘L"));
            items.Add(new("cmd:commits", "Show All Commits", "", "Commands", Icons.Commits, new Msg.Navigate(Page.History), "⌘G"));
            items.Add(new("cmd:fetch", "Fetch", "", "Commands", Icons.Fetch, new Msg.Run(new(GitAction.Fetch)), "⇧⌘F"));
            items.Add(new("cmd:pull", "Pull", "", "Commands", Icons.Pull, new Msg.Run(new(GitAction.Pull)), "⇧⌘L"));
            items.Add(new("cmd:push", "Push", "", "Commands", Icons.Push, new Msg.Run(new(GitAction.Push)), "⇧⌘P"));
            items.Add(new("cmd:stash", "Stash Changes", "", "Commands", Icons.Stash, new Msg.Run(new(GitAction.Stash)), "⇧⌘S"));
            items.Add(new("cmd:stage-all", "Stage All Changes", "", "Commands", Icons.Plus, new Msg.Run(new(GitAction.StageAll))));
            items.Add(new("cmd:checkout", "Checkout Branch…", "", "Commands", Icons.Branch, new Msg.ShowPalette(PaletteMode.Branch), "⌘B", true));
            items.Add(new("cmd:new-branch", "New Branch…", "", "Commands", Icons.Branch, new Msg.ShowPalette(PaletteMode.NewBranch), "⇧⌘B", true));
            items.Add(new("cmd:refresh", "Refresh", "", "Commands", Icons.Refresh, new Msg.Refresh(repo), "⌘R"));
            items.Add(new("cmd:close", "Close " + name, "", "Commands", Icons.Close, new Msg.CloseRepo(repo), "⌘W"));
            items.Add(new("cmd:finder", "Reveal in Finder", "", "Commands", Icons.Finder, new Msg.Launch(new(LaunchKind.Open, [repo]))));
            items.Add(new("cmd:terminal", "Open in Terminal", "", "Commands", Icons.Terminal, new Msg.Launch(new(LaunchKind.Terminal, [repo]))));
        }
        items.Add(new("cmd:open", "Open Repository…", "", "Commands", Icons.FolderOpen, new Msg.ShowPalette(PaletteMode.Open), "⌘O", true));
        items.Add(new("cmd:ws", "Switch Workspace…", "", "Commands", Icons.Layers, new Msg.ShowPalette(PaletteMode.Workspaces), "⇧⌘W", true));
        items.Add(new("cmd:gh", $"GitHub Account for \"{workspace.Name}\"…", workspace.Account.Length > 0 ? "@" + workspace.Account : "gh active account", "Commands",
            Icons.User, new Msg.ShowPalette(PaletteMode.Account, workspace.Id), KeepOpen: true));
        items.Add(new("cmd:ws-new", "New Workspace…", "", "Commands", Icons.Plus, new Msg.ShowPalette(PaletteMode.NewWorkspace), KeepOpen: true));
        items.Add(new("cmd:ws-rename", $"Rename Workspace \"{workspace.Name}\"…", "", "Commands", Icons.Pencil,
            new Msg.ShowPalette(PaletteMode.RenameWorkspace, workspace.Id), KeepOpen: true));
        items.Add(new("cmd:ws-delete", $"Delete Workspace \"{workspace.Name}\"", "", "Commands", Icons.Trash, new Msg.DeleteWorkspace(workspace.Id)));
        items.Add(new("cmd:sidebar", "Toggle Sidebar", "", "Commands", Icons.Sidebar, new Msg.ToggleSidebar(), "⌘\\"));
        return items.ToImmutable();
    }
    ImmutableArray<LaunchItem> BranchItems()
    {
        if (Model.Current is not { } tab)
            return [];
        var locals = tab.Snapshot.Refs.Where(r => r.Kind == "branch").ToImmutableArray();
        var names = locals.Select(r => r.Name).ToImmutableHashSet(StringComparer.Ordinal);
        var remotes = tab.Snapshot.Refs.Where(r => r.Kind == "remote" && !names.Contains(r.Name[(r.Name.IndexOf('/') + 1)..]));
        return [.. locals.Select(b => new LaunchItem("branch:" + b.Name, b.Name, b.Current ? "current" : b.Upstream, "Local branches",
                b.Current ? Icons.Check : Icons.Branch, new Msg.Run(new(GitAction.Checkout, Value: b.Name)))),
            .. remotes.Select(r => new LaunchItem("remote:" + r.Name, r.Name, "checkout & track", "Remote branches", Icons.Cloud,
                new Msg.Run(new(GitAction.CheckoutRemote, Value: r.Name))))];
    }
    ImmutableArray<LaunchItem> WorkspaceItems()
    {
        var many = Model.Workspaces.Length > 1;
        var items = Model.Workspaces.Select(w => new LaunchItem("ws:" + w.Id, w.Name,
            $"{w.Repositories.Length} repo{(w.Repositories.Length == 1 ? "" : "s")}{(w.Account.Length > 0 ? " · @" + w.Account : "")}{(w.Id == Model.ActiveWorkspace ? " · current" : "")}",
            "Workspaces", w.Id == Model.ActiveWorkspace ? Icons.Check : Icons.Layers, new Msg.SelectWorkspace(w.Id), RowActions:
            [
                new("account", "GitHub", Icons.User, "⌘G", new Msg.ShowPalette(PaletteMode.Account, w.Id)),
                new("rename", "Rename", Icons.Pencil, "⌘R", new Msg.ShowPalette(PaletteMode.RenameWorkspace, w.Id)),
                .. many ? [new RowAction("delete", "Delete", Icons.Trash, "⌘D", new Msg.DeleteWorkspace(w.Id), true)] : ImmutableArray<RowAction>.Empty
            ]));
        return [.. items, new("ws:new", "New Workspace…", "", "Workspaces", Icons.Plus, new Msg.ShowPalette(PaletteMode.NewWorkspace), KeepOpen: true)];
    }
    /// <summary>gh logins for the target workspace; picking one never runs `gh auth switch`.</summary>
    ImmutableArray<LaunchItem> AccountItems()
    {
        if (Model.Workspaces.FirstOrDefault(w => w.Id == (Model.PaletteTarget ?? Model.ActiveWorkspace)) is not { } workspace)
            return [];
        var group = $"GitHub account for \"{workspace.Name}\"";
        var active = Model.Accounts.FirstOrDefault(a => a.Active)?.Login;
        return [.. Model.Accounts.Select(a => new LaunchItem("gh:" + a.Login, a.Login,
                string.Join(" · ", new[] { a.Login == workspace.Account ? "assigned" : "", a.Active ? "gh active account" : "", a.Scopes }.Where(s => s.Length > 0)),
                group, a.Login == workspace.Account ? Icons.Check : Icons.User, new Msg.SetAccount(workspace.Id, a.Login))),
            new("gh:default", "No pinned account", $"{(workspace.Account.Length == 0 ? "current · " : "")}follow gh's active account{(active is null ? "" : $" (now {active})")}",
                group, workspace.Account.Length == 0 ? Icons.Check : Icons.Layers, new Msg.SetAccount(workspace.Id, "")),
            new("gh:add", "Add an account…", "runs `gh auth login` in Terminal", group, Icons.Plus, new Msg.Launch(new(LaunchKind.AddAccount, [])))];
    }
    ImmutableArray<LaunchItem> Results()
    {
        var query = Model.Query;
        var pool = Model.Palette switch
        {
            PaletteMode.Open => RepositoryItems(),
            PaletteMode.Branch => BranchItems(),
            PaletteMode.Workspaces => WorkspaceItems(),
            PaletteMode.Account => AccountItems(),
            _ => [.. RepositoryItems().Take(query.Length > 0 ? int.MaxValue : 8), .. CommandItems(), .. query.Length > 0 ? BranchItems() : [],
                .. query.Length > 0 ? WorkspaceItems() : []]
        };
        if (query.Length == 0)
            return [.. pool.Take(60)];
        return [.. pool.Select(item => (Item: item, Score: Math.Max(Fuzzy(query, item.Label), item.Detail.Length > 0 ? Fuzzy(query, item.Detail) - 300 : -1)))
            .Where(r => r.Score >= 0).OrderByDescending(r => r.Score).Take(60).Select(r => r.Item)];
    }
    /// <summary>The original subsequence score: contiguous and word-start matches rank higher; -1 means no match.</summary>
    static double Fuzzy(string query, string target)
    {
        if (query.Length == 0)
            return 0;
        var q = query.ToLowerInvariant();
        var t = target.ToLowerInvariant();
        static bool Boundary(string text, int at) => at == 0 || text[at - 1] is ' ' or '/' or '_' or '-' or '.';
        var direct = t.IndexOf(q, StringComparison.Ordinal);
        if (direct >= 0)
            return 1000 - direct * 2 - (t.Length - q.Length) * .1 + (Boundary(t, direct) ? 200 : 0);
        double score = 0;
        var at = 0;
        var streak = 0;
        foreach (var ch in q)
        {
            if (ch == ' ')
                continue;
            var found = t.IndexOf(ch, at);
            if (found < 0)
                return -1;
            streak = found == at ? streak + 1 : 0;
            score += 10 + streak * 5 + (Boundary(t, found) ? 15 : 0) - Math.Min(found - at, 10);
            at = found + 1;
        }
        return score - t.Length * .1;
    }
}
