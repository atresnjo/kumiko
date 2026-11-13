using System.Collections.Immutable;
using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>A resizable sidebar: titlebar space, repository name, navigation, filter and a virtual ref tree.</summary>
    float SidebarArea()
    {
        using var outer = C.Div(key: "sidebar-reveal").FillHeight().Row().Clip();
        var reveal = outer.Animate(Model.Sidebar ? 1 : 0, Motion.Gentle);
        var space = (SidebarPaneWidth + ResizeThickness) * reveal;
        outer.Width(space);
        if (reveal <= 0)
            return space;
        using (C.Div(key: "sidebar").Width(SidebarPaneWidth).FillHeight().Background(Sidebar))
        {
            using (C.Div(key: "titlebar").Height(TitleBar))
            {
            }
            if (Model.Current is { } tab)
                SidebarContent(tab);
            else
                using (C.Div(key: "hint").Padding(new Edges(0, 0, 0, 16)).Gap(6))
                {
                    Label(Model.Workspace.Name, "name", 14, weight: Weight.Semibold);
                    Label(Model.Workspace.Active is null ? "Empty workspace" : "Loading…", "empty", 12, Muted);
                }
        }
        if (Model.Sidebar && reveal == 1)
            Ui.ResizeHandle("Resize sidebar", SidebarPaneWidth, static width => new Msg.ResizeSidebar(width),
                new(Min: 220, Max: SidebarMaximum), key: "sidebar-resize");
        return space;
    }
    void SidebarContent(RepoTab tab)
    {
        using (C.Div(key: "repo").FillWidth().Row().Height(26).ItemsCenter().Padding(new Edges(0, 12, 0, 16)).Clip())
            Label(tab.Snapshot.Name, "name", 14, weight: Weight.Semibold);
        using (C.Div(key: "nav").FillWidth().Gap(1).Padding(new Edges(0, 6, 8, 6)))
        {
            NavItem(tab.ChangeCount > 0 ? $"Local Changes ({tab.ChangeCount})" : "Local Changes", Icons.Changes, new Msg.Navigate(Page.Changes), "changes",
                tab.Page == Page.Changes, "Local Changes");
            NavItem("All Commits", Icons.Commits, new Msg.Navigate(Page.History), "commits", tab.Page == Page.History);
        }
        using (C.Div(key: "nav-rule").FillWidth().Px(10).Height(1))
        using (C.Div(key: "line").FillWidth().FillHeight().Background(Border))
        {
        }
        using (C.Div(key: "filter-tools").Width(SidebarPaneWidth - 20).Row().Margin(10).Height(24).ItemsCenter().Gap(4))
        {
            using (C.Div(key: "filter").Flex().MinWidth(0).Row().Height(24).ItemsCenter().Padding(new Edges(0, 0, 0, 8)).Gap(2).Rounded(6).Border(Border).Background(Canvas).Clip())
            {
                C.Icon(Icons.Search, 12, Faint, key: "icon");
                Field("field", "Filter", tab.Filter, static text => new Msg.SidebarFilter(text), "Filter", SidebarPaneWidth - 106, 22, borderless: true);
            }
            SidebarTool("Focus current branch", Icons.Focus, new Msg.FocusCurrentBranch(), "focus-current",
                tab.Snapshot.Refs.Any(r => r.Kind == "branch" && r.Current));
            SidebarTool("Collapse all", Icons.Collapse, new Msg.CollapseSidebar(), "collapse-all");
        }
        var items = SideTree.Items(tab.Snapshot, tab.Folders, tab.Filter);
        var reveal = tab.RevealSidebar ? items.FindIndex(item => item.Id == tab.SideSelected) : -1;
        using (var list = C.Div(key: "sidebar-list").FillWidth().Flex().MinHeight(0).Semantics(Role.Tree, "References"))
        {
            var focused = C.FocusWithin(list.Id);
            C.UniformList("refs", items.Length, RowHeight, (_, index) =>
            {
                using var inset = C.Div(key: "inset").FillWidth().FillHeight().Px(6);
                SideRow(tab, items[index], focused);
            }, revealRow: reveal >= 0 ? reveal : null, rowKey: index => StableId(items[index].Id));
            if (tab.RevealSidebar)
                Ui.Dispatch(new Msg.SidebarRevealed(tab.Snapshot.Path));
            if (focused)
                SidebarKeys(tab, items);
        }
        using var footer = C.Div(key: "footer").FillWidth().Height(29);
        Rule("footer-rule");
        using var hint = C.Div(key: "hint").FillWidth().Height(28).Row().ItemsCenter().Padding(new Edges(0, 0, 0, 14)).Clip();
        var hit = Ui.Button(hint, "Checkout branch from the sidebar", new Msg.ShowPalette(PaletteMode.Branch), true);
        if (hit.Hovered)
            hint.Background(Hover);
        Label("Double-click a branch to check out · right-click for more", "label", 11, Muted);
    }
    void SidebarTool(string name, string icon, Msg message, NodeKey key, bool enabled = true)
    {
        using var button = C.Div(key: key).Width(24).Height(24).Center().Rounded(6);
        var hit = Ui.Button(button, name, message, enabled);
        if (hit.Hovered && enabled)
            button.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            button.Border(Accent, 2);
        C.Icon(icon, 15, enabled ? Muted : Faint, key: "icon");
        C.Kit.Tooltip(hit, name, key: "tooltip");
    }
    void NavItem(string label, string icon, Msg message, NodeKey key, bool active, string? name = null)
    {
        using var row = C.Div(key: key).FillWidth().Height(24).Row().ItemsCenter().Gap(8).Padding(new Edges(0, 0, 0, 10)).Rounded(5);
        var hit = Ui.Button(row, name ?? label, message, true);
        if (active)
            row.Background(Pressed);
        else if (hit.Hovered)
            row.Background(Hover);
        if (hit.Focused && C.FocusVisible)
            row.Border(Accent, 2);
        C.Icon(icon, 14, Muted, key: "icon");
        Label(label, "label", 13, weight: active ? Weight.Medium : Weight.Regular);
    }
    void SideRow(RepoTab tab, SideItem item, bool focused)
    {
        var collapsed = tab.Filter.Trim().Length == 0 && tab.Folders.Contains(item.Id);
        if (item.Kind == SideKind.Section)
        {
            using var section = C.Div(key: "section").FillWidth().FillHeight().Row().ItemsCenter().Gap(4).Padding(new Edges(0, 0, 0, 10));
            using (var heading = C.Div(key: "heading").Flex().FillHeight().Row().ItemsCenter().Gap(4))
            {
                var toggle = Ui.Button(heading, item.Text, new Msg.ToggleFolder(item.Id), true);
                if (toggle.Focused && C.FocusVisible)
                    heading.Border(Accent, 2);
                if (item.Text == "Remotes")
                    Ui.OnContextMenu(toggle, static at => new Msg.OpenMenu(at, MenuKind.Remotes, ""));
                C.Icon(collapsed ? Icons.ChevronRight : Icons.ChevronDown, 11, Muted, key: "chevron");
                Label(item.Text, "label", 12, weight: Weight.Medium);
            }
            if (item.Text == "Remotes")
                IconButton("Add Remote", Icons.Plus, new Msg.EditRemote(GitAction.AddRemote), "add-remote", RowHeight, 12,
                    enabled: Model.BusyLabel(tab.Snapshot.Path) is null);
            return;
        }
        var indent = 12 + item.Depth * 12f;
        if (item.Kind == SideKind.Folder)
        {
            var remote = tab.Snapshot.Remotes.FirstOrDefault(r => item.Text == "remotes/" + r);
            var label = remote ?? item.Label;
            using var folder = C.Div(key: "folder").FillWidth().FillHeight().Row().ItemsCenter().Gap(4).Padding(new Edges(0, 0, 0, indent - 12)).Rounded(5).Clip();
            var hit = Ui.Button(folder, "Folder: " + label, new Msg.ToggleFolder(item.Id), true);
            if (remote is not null)
                Ui.OnContextMenu(hit, at => new Msg.OpenMenu(at, MenuKind.RemoteFolder, remote));
            if (hit.Hovered)
                folder.Background(Hover);
            if (hit.Focused && C.FocusVisible)
                folder.Border(Accent, 2);
            C.Icon(collapsed ? Icons.ChevronRight : Icons.ChevronDown, 10, Muted, key: "chevron");
            C.Icon(Icons.Folder, 14, Muted, key: "icon");
            Label(label, "label", 12);
            return;
        }
        var snapshot = tab.Snapshot;
        var (name, icon, current, badge, sha, dim) = Leaf(snapshot, item);
        var selected = tab.SideSelected == item.Id;
        using var row = C.Div(key: "leaf").FillWidth().FillHeight().Row().ItemsCenter().Gap(6).Padding(new Edges(0, 8, 0, indent + 2)).Rounded(5).Clip();
        var leaf = row.Semantics(Role.TreeItem, name).Control();
        if (selected)
            row.Background(SelectedSoft);
        else if (leaf.Hovered)
            row.Background(Hover);
        if (leaf.Focused && C.FocusVisible)
            row.Border(Accent, 2);
        if (focused && selected && !leaf.Focused)
            C.FocusControl(leaf.Id);
        var open = Activation(snapshot, item);
        var openOnClick = item.Kind == SideKind.Worktree && !current && !dim;
        if (leaf.Clicked)
        {
            if (open is not null && (openOnClick || leaf.DoubleClicked))
                Ui.Dispatch(open);
            else if (!leaf.DoubleClicked)
                Ui.Dispatch(new Msg.SidebarSelect(item.Id, openOnClick ? null : sha));
        }
        var kind = MenuFor(snapshot, item);
        var target = item.Kind == SideKind.Stash ? snapshot.Stashes[item.Source].Ref : item.Text;
        // Tags have no actions in the original, so they open no menu.
        if (item.Kind != SideKind.Tag)
            Ui.OnContextMenu(leaf, at => new Msg.OpenMenu(at, kind, target));
        C.Icon(current ? Icons.Check : icon, 13, current ? Text : Muted, key: "icon");
        if (item.Kind == SideKind.Worktree)
        {
            var available = MathF.Max(0, SidebarPaneWidth - indent - (badge.Length > 0 ? 47 : 41));
            var style = new TextStyle(Size: 12, Color: dim ? MutedStrong : Text, Weight: current ? Weight.Bold : Weight.Regular);
            var badgeStyle = new TextStyle(Size: 11, Color: MutedStrong);
            var badgeWidth = badge.Length > 0 ? MathF.Min(C.TextSystem.Measure(badge, badgeStyle).Width, available * .6f) : 0;
            using (C.Div(key: "name").Flex().MinWidth(0))
                C.Text(item.Label, style with
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "label");
            if (badge.Length > 0)
                using (C.Div(key: "branch").Width(badgeWidth))
                    C.Text(badge, badgeStyle with
                    {
                        Overflow = TextOverflow.Ellipsis
                    }, key: "badge");
            return;
        }
        using (C.Div(key: "name").Flex().MinWidth(0).Clip())
            Label(item.Label, "label", 12, dim ? MutedStrong : Text, current ? Weight.Bold : Weight.Regular);
        if (badge.Length > 0)
            Label(badge, "badge", 11, MutedStrong);
    }
    static (string Name, string Icon, bool Current, string Badge, string? Sha, bool Dim) Leaf(Snapshot snapshot, SideItem item)
    {
        switch (item.Kind)
        {
            case SideKind.Worktree:
                var tree = snapshot.Worktrees[item.Source];
                var here = tree.Path == snapshot.Path;
                var badge = tree.Prunable ? "missing" : tree.Branch.Length > 0 ? tree.Branch : tree.Detached ? "detached " + tree.Sha[..Math.Min(7, tree.Sha.Length)] : "";
                return ("Worktree: " + item.Label, Icons.Worktree, here, badge, null, tree.Prunable);
            case SideKind.Stash:
                var stash = snapshot.Stashes[item.Source];
                return ("Stash: " + item.Label, Icons.Stash, false, "", stash.Sha, false);
            default:
                var reference = snapshot.Refs[item.Source];
                var track = string.Join(' ', new[] { reference.Ahead > 0 ? $"{reference.Ahead}↑" : "", reference.Behind > 0 ? $"{reference.Behind}↓" : "" }.Where(s => s.Length > 0));
                var prefix = item.Kind == SideKind.Remote ? "Remote branch: " : item.Kind == SideKind.Tag ? "Tag: " : "Branch: ";
                return (prefix + reference.Name, item.Kind == SideKind.Tag ? Icons.Tag : Icons.Branch, reference.Current, track, reference.Sha, false);
        }
    }
    /// <summary>What Enter or a double-click does: check out, track, pop or open the worktree in a tab.</summary>
    static Msg? Activation(Snapshot snapshot, SideItem item) => item.Kind switch
    {
        SideKind.Branch when !snapshot.Refs[item.Source].Current => new Msg.Run(new(GitAction.Checkout, Value: item.Text)),
        SideKind.Remote => new Msg.Run(new(GitAction.CheckoutRemote, Value: item.Text)),
        SideKind.Stash => new Msg.Run(new(GitAction.PopStash, Value: snapshot.Stashes[item.Source].Ref)),
        SideKind.Worktree when !snapshot.Worktrees[item.Source].Prunable && snapshot.Worktrees[item.Source].Path != snapshot.Path
            => new Msg.Open(snapshot.Worktrees[item.Source].Path),
        _ => null
    };
    static MenuKind MenuFor(Snapshot snapshot, SideItem item) => item.Kind switch
    {
        SideKind.Remote => MenuKind.Remote,
        SideKind.Tag => MenuKind.Tag,
        SideKind.Stash => MenuKind.Stash,
        SideKind.Worktree => snapshot.Worktrees[item.Source].Prunable ? MenuKind.MissingWorktree : MenuKind.Worktree,
        _ => MenuKind.Branch
    };
    /// <summary>Up/Down move among leaves and select their commit; Enter opens; Delete removes a worktree; Escape clears.</summary>
    void SidebarKeys(RepoTab tab, ImmutableArray<SideItem> items)
    {
        var leaves = items.Where(i => i.Leaf).ToImmutableArray();
        if (leaves.IsEmpty)
            return;
        var at = leaves.FindIndex(l => l.Id == tab.SideSelected);
        var step = C.Shortcut(Key.Down, Modifiers.None) ? 1 : C.Shortcut(Key.Up, Modifiers.None) ? -1 : 0;
        if (step != 0)
        {
            var next = leaves[Math.Clamp(at < 0 ? 0 : at + step, 0, leaves.Length - 1)];
            var (_, _, _, _, sha, _) = Leaf(tab.Snapshot, next);
            Ui.Dispatch(new Msg.SidebarSelect(next.Id, next.Kind == SideKind.Worktree ? null : sha));
            return;
        }
        if (at < 0)
            return;
        var current = leaves[at];
        if (C.Shortcut(Key.Enter, Modifiers.None) && Activation(tab.Snapshot, current) is { } open)
            Ui.Dispatch(open);
        else if ((C.Shortcut(Key.Backspace, Modifiers.None) || C.Shortcut(Key.Delete, Modifiers.None)) && current.Kind == SideKind.Worktree
            && current.Source > 0 && !tab.Snapshot.Worktrees[current.Source].Bare)
            Ui.Dispatch(new Msg.Run(new(GitAction.DeleteWorktree, Value: current.Text)));
        else if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.SidebarSelect(null));
    }
    /// <summary>A text field sized like the original's 24 px inputs; the scoped theme only affects this control.</summary>
    void Field(NodeKey key, string label, string value, Func<string, Msg> onChange, string placeholder, float width, float height = 24,
        float size = 12, bool borderless = false, bool multiline = false)
    {
        var saved = C.Theme;
        C.Theme = saved with
        {
            Space = saved.Space with
            {
                S2 = multiline ? 8 : MathF.Max(2, (height - size * 1.3f) / 2),
                S3 = 8
            },
            Controls = saved.Controls with
            {
                Height = height
            },
            Radius = saved.Radius with
            {
                Md = borderless ? 0 : 4
            },
            InputBorder = borderless ? Color.Transparent : BorderStrong,
            FocusRing = borderless ? Color.Transparent : Accent,
            Typography = saved.Typography with
            {
                Body = new(size, Color: Text, Leading: multiline ? 17 / (size * 1.2f) : 1)
            }
        };
        Ui.TextField(key, label, value, onChange, new(placeholder, width, Multiline: multiline, Height: height));
        C.Theme = saved;
    }
}
