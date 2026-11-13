using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>A context-menu entry: a message to dispatch, or text to copy to the clipboard.</summary>
    sealed record MenuEntry(string Label, string Icon = "", string Shortcut = "", Msg? Message = null, string? Copy = null,
        bool Destructive = false, bool Disabled = false)
    {
        public static readonly MenuEntry Separator = new("");
    }
    ImmutableArray<MenuEntry> MenuEntries(MenuState menu)
    {
        if (Model.Current is not { } tab)
            return [];
        var snapshot = tab.Snapshot;
        string Absolute(string path) => System.IO.Path.Combine(snapshot.Path, path);
        switch (menu.Kind)
        {
            case MenuKind.Files:
                var files = FileTree.Files(menu.Staged ? snapshot.Staged : snapshot.Unstaged, tab.Staged == menu.Staged && tab.Selected.Contains(menu.Target) ? tab.Selected : [menu.Target]);
                if (files.IsEmpty)
                    return [];
                var label = files.Length == 1 ? System.IO.Path.GetFileName(files[0].Path) : files.Length + " files";
                ImmutableArray<string> paths = [.. files.Select(f => Absolute(f.Path))];
                var deleted = files.Any(f => f.Status == 'D');
                return
                [
                    menu.Staged ? new($"Unstage {label}", Icons.Pull, "Space", new Msg.RunSelection(GitAction.Unstage, true))
                        : new($"Stage {label}", Icons.Push, "Space", new Msg.RunSelection(GitAction.Stage, false)),
                    new($"Stash {label}", Icons.Stash, "S", new Msg.RunSelection(GitAction.Stash, menu.Staged)),
                    new($"Revert {label}…", Icons.Refresh, "⌫", new Msg.RunSelection(GitAction.Revert, menu.Staged), Destructive: true),
                    MenuEntry.Separator,
                    new(files.Length == 1 ? "Copy Path" : "Copy Paths", Icons.File, "⌘C", Copy: string.Join('\n', files.Select(f => f.Path))),
                    new("Open in Zed", Icons.File, Message: new Msg.Launch(new(LaunchKind.Editor, paths)), Disabled: deleted),
                    new("Reveal in Finder", Icons.Finder, Message: new Msg.Launch(new(LaunchKind.Reveal, paths))),
                    new(files.Length == 1 ? "Open File" : "Open Files", Icons.FolderOpen, Message: new Msg.Launch(new(LaunchKind.Open, paths)), Disabled: deleted)
                ];
            case MenuKind.CommitFile:
                // Opens the working-tree copy, so a file deleted by this commit cannot be opened.
                var gone = tab.Detail?.Files.FirstOrDefault(f => f.Path == menu.Target)?.Status == 'D';
                ImmutableArray<string> file = [Absolute(menu.Target)];
                return
                [
                    new("Open in Zed", Icons.File, Message: new Msg.Launch(new(LaunchKind.Editor, file)), Disabled: gone),
                    new("Open File", Icons.FolderOpen, Message: new Msg.Launch(new(LaunchKind.Open, file)), Disabled: gone),
                    new("Reveal in Finder", Icons.Finder, Message: new Msg.Launch(new(LaunchKind.Reveal, file)), Disabled: gone)
                ];
            case MenuKind.Branch:
                var current = snapshot.Refs.Any(r => r.Kind == "branch" && r.Name == menu.Target && r.Current);
                var upstream = snapshot.Refs.FirstOrDefault(r => r.Kind == "branch" && r.Name == menu.Target)?.Upstream ?? "";
                return
                [
                    new($"Checkout {menu.Target}", Icons.Branch, "↵", new Msg.Run(new(GitAction.Checkout, Value: menu.Target)), Disabled: current),
                    new("New Branch from Here…", Icons.Plus, Message: new Msg.ShowPalette(PaletteMode.NewBranch, menu.Target)),
                    new("Set Upstream…", Icons.Branch, Message: new Msg.EditRemote(GitAction.SetUpstream, menu.Target)),
                    new("Remove Upstream", Icons.Close, Message: new Msg.Run(new(GitAction.RemoveUpstream, Value: menu.Target)), Disabled: upstream.Length == 0),
                    MenuEntry.Separator,
                    new("Copy Branch Name", Icons.File, Copy: menu.Target)
                ];
            case MenuKind.Remote:
                return
                [
                    new($"Checkout & Track {menu.Target[(menu.Target.IndexOf('/') + 1)..]}", Icons.Branch, "↵", new Msg.Run(new(GitAction.CheckoutRemote, Value: menu.Target))),
                    MenuEntry.Separator,
                    new("Copy Branch Name", Icons.File, Copy: menu.Target)
                ];
            case MenuKind.Remotes:
                return [new("Add Remote…", Icons.Plus, Message: new Msg.EditRemote(GitAction.AddRemote))];
            case MenuKind.RemoteFolder:
                return
                [
                    new("Fetch " + menu.Target, Icons.Fetch, Message: new Msg.Run(new(GitAction.Fetch, Value: menu.Target))),
                    new("Edit Remote URL…", Icons.File, Message: new Msg.EditRemote(GitAction.SetRemoteUrl, menu.Target)),
                    new("Rename Remote…", Icons.Folder, Message: new Msg.EditRemote(GitAction.RenameRemote, menu.Target)),
                    new("Add Remote…", Icons.Plus, Message: new Msg.EditRemote(GitAction.AddRemote)),
                    MenuEntry.Separator,
                    new("Remove Remote…", Icons.Trash, Message: new Msg.Run(new(GitAction.RemoveRemote, Value: menu.Target)), Destructive: true)
                ];
            case MenuKind.Stash:
                return [new("Pop Stash", Icons.Stash, "↵", new Msg.Run(new(GitAction.PopStash, Value: menu.Target)))];
            case MenuKind.MissingWorktree:
                return [new("Prune Missing Worktrees", Icons.Trash, Message: new Msg.Run(new(GitAction.PruneWorktrees)))];
            case MenuKind.Worktree:
                var index = snapshot.Worktrees.FindIndex(w => w.Path == menu.Target);
                if (index < 0)
                    return [];
                var tree = snapshot.Worktrees[index];
                var here = tree.Path == snapshot.Path;
                var removable = index > 0 && !tree.Bare;
                ImmutableArray<string> folder = [tree.Path];
                var entries = ImmutableArray.CreateBuilder<MenuEntry>();
                entries.Add(new(here ? "Already Open" : "Open in New Tab", Icons.Commits, "↵", new Msg.Open(tree.Path), Disabled: here));
                entries.Add(new("Reveal in Finder", Icons.Finder, Message: new Msg.Launch(new(LaunchKind.Open, folder))));
                entries.Add(new("Open in Terminal", Icons.Terminal, Message: new Msg.Launch(new(LaunchKind.Terminal, folder))));
                entries.Add(new("Copy Path", Icons.File, Copy: tree.Path));
                entries.Add(MenuEntry.Separator);
                entries.Add(new("Delete Worktree…", Icons.Trash, "⌫", new Msg.Run(new(GitAction.DeleteWorktree, Value: tree.Path)), Destructive: true, Disabled: !removable));
                if (removable && tree.Branch.Length > 0)
                    entries.Add(new("Delete Worktree & Branch…", Icons.Trash, Message: new Msg.Run(new(GitAction.DeleteWorktreeAndBranch, Value: tree.Path)), Destructive: true));
                return entries.ToImmutable();
            default:
                return [];
        }
    }
    /// <summary>The original 248 px context menu with icons, shortcuts, separators and destructive entries.</summary>
    void Menu()
    {
        var menu = Model.Menu!;
        var entries = MenuEntries(menu);
        using var overlay = C.Overlay("menu").TabStop(false).Semantics(Role.Group);
        var backdrop = overlay.Control();
        if (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None) || entries.IsEmpty)
            Ui.Dispatch(new Msg.Cancel());
        var height = entries.Sum(e => e.Label.Length == 0 ? 9 : 26) + 10;
        using var panel = C.Div(key: "panel").Absolute()
            .Offset(Math.Clamp(menu.At.X, 4, MathF.Max(4, C.Size.Width - 256)), Math.Clamp(menu.At.Y, 4, MathF.Max(4, C.Size.Height - height - 8)))
            .Width(248).Padding(5).Rounded(9).Background(Color.Hex(0xfbfbfd)).Border(BorderStrong)
            .Shadow(30, 10, Color.Black.Alpha(.19f)).Semantics(Role.Menu, "Actions").TabStop(false);
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        ulong previous = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Label.Length == 0)
            {
                using (C.Div(key: "separator-" + i).Height(1).Margin(new Edges(4, 6, 4, 6)).Background(Border))
                {
                }
                continue;
            }
            using var row = C.Div(key: entry.Label).FillWidth().Height(26).Row().ItemsCenter().Gap(8).Px(8).Rounded(5).Semantics(Role.MenuItem, entry.Label);
            var hit = row.Control(!entry.Disabled);
            var highlighted = !entry.Disabled && C.Kit.Highlighted(hit);
            if (highlighted)
                row.Background(entry.Destructive ? Danger : Accent);
            var ink = entry.Disabled ? Muted : highlighted ? Color.White : entry.Destructive ? Danger : Text;
            using (C.Div(key: "icon").Width(14))
                if (entry.Icon.Length > 0)
                    C.Icon(entry.Icon, 13, ink, key: "glyph");
            using (C.Div(key: "label").Flex().MinWidth(0).Clip())
                Label(entry.Label, "text", 13, ink);
            if (entry.Shortcut.Length > 0)
                Label(entry.Shortcut, "shortcut", 11, highlighted ? SelectedMuted : Muted);
            if (hit.Focused && C.Shortcut(Key.Down, Modifiers.None))
                C.FocusNextControl();
            if (hit.Focused && previous != 0 && C.Shortcut(Key.Up, Modifiers.None))
                C.FocusControl(previous);
            if (!entry.Disabled)
                previous = hit.Id;
            if (entry.Message is { } message)
                Ui.OnPress(hit, message);
            else if (entry.Copy is { } text && hit.Clicked)
            {
                C.Clipboard = text;
                Ui.Dispatch(new Msg.Cancel());
            }
        }
    }
    /// <summary>The original ConfirmDialog. Complete hook output scrolls in a monospace box and can be copied.</summary>
    void Confirmation()
    {
        var pending = Model.Pending!;
        var details = pending.Details.Length > 0;
        using var overlay = C.Overlay("confirmation").Items(Align.Center).Padding(new Edges(details ? 90 : 150, 24, 24, 24)).Background(Overlay);
        var backdrop = overlay.Control();
        if (backdrop.Clicked || C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.Cancel());
        else if (C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(pending.Request is null ? new Msg.Cancel() : new Msg.Confirm());
        using var dialog = C.Div(key: "dialog").Width(MathF.Min(details ? 720 : 440, C.Size.Width - 48)).Padding(20).Gap(10).Rounded(12)
            .Background(Canvas).Border(BorderStrong).Shadow(48, 18, Color.Black.Alpha(.25f)).Semantics(Role.Dialog, pending.Title);
        dialog.Control();
        C.Text(pending.Title, new(Size: 15, Weight: Weight.Semibold, Color: Text, Wrap: true), key: "title");
        C.Text(pending.Message, new(Size: 13, Color: Muted, Wrap: true, Leading: 19 / 15.6f), key: "message");
        if (details)
        {
            using var box = C.Div(key: "details").FillWidth().MaxHeight(MathF.Min(360, MathF.Max(80, C.Size.Height - 280)))
                .Scroll().Padding(10).Rounded(7).Border(Border).Background(Color.Hex(0xf6f6f8))
                .Semantics(Role.Group, "Command output");
            C.Text(pending.Details, new(Size: 11, Color: Text, Wrap: true, Mono: true, Leading: 16 / 13.2f), key: "output");
        }
        using var actions = C.Div(key: "actions").FillWidth().Row().ItemsCenter().Gap(8).Margin(new Edges(6, 0, 0, 0));
        using (C.Div(key: "hint").Flex())
            Label(pending.Request is null ? "esc close" : "↵ confirm · esc cancel", "text", 11, Muted);
        if (details && Button("Copy Output", null, "copy-output").Clicked)
            C.Clipboard = pending.Details;
        Button(pending.CancelLabel, new Msg.Cancel(), "cancel", primary: pending.Request is null);
        if (pending.Request is not null)
            Button(pending.ConfirmLabel, new Msg.Confirm(), "confirm", primary: !pending.Destructive, destructive: pending.Destructive);
    }
}
