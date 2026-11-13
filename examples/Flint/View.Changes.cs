using System.Collections.Immutable;
using Flint.Git;
using Kumiko.UI;
using static Flint.Palette;

namespace Flint;

public sealed partial class FlintView
{
    /// <summary>Resizable Unstaged and Staged trees beside the diff and composer.</summary>
    void ChangesView(RepoTab tab, float sidebarSpace)
    {
        using var changes = C.Div(key: "local-changes").FillWidth().Flex().MinHeight(0).Row();
        var maximum = MathF.Max(260, C.Size.Width - sidebarSpace - 320 - ResizeThickness);
        var width = Math.Clamp(tab.WorkingFilesWidth, 260, maximum);
        var availableHeight = MathF.Max(220, C.Size.Height - (changes.PreviousRect?.Y ?? 93) - ResizeThickness);
        var unstagedHeight = Math.Clamp(availableHeight * tab.UnstagedFraction, 110, availableHeight - 110);
        using (C.Div(key: "index").Width(width).FillHeight())
        {
            FileSection(tab, false, unstagedHeight);
            Ui.ResizeHandle("Resize unstaged files", unstagedHeight, value => new Msg.ResizeUnstaged(value / availableHeight),
                new(Orientation: Kumiko.Components.Orientation.Horizontal, Min: 110, Max: availableHeight - 110), key: "sections-resize");
            FileSection(tab, true);
        }
        Ui.ResizeHandle("Resize local files", width, static value => new Msg.ResizeWorkingFiles(value),
            new(Min: 260, Max: maximum), key: "index-resize");
        using var right = C.Div(key: "working").Flex().MinWidth(0).FillHeight();
        var shown = tab.File is { } path ? (tab.Staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged).FirstOrDefault(f => f.Path == path) : null;
        using (C.Div(key: "working-diff").FillWidth().Flex().MinHeight(0))
        {
            if (shown is null)
                Empty("No local changes");
            else
            {
                using (C.Div(key: "file-header").FillWidth().Height(30).Row().ItemsCenter().Gap(8).Padding(new Edges(0, 8, 0, 12)).Background(Pane))
                {
                    using (C.Div(key: "name").Flex().MinWidth(0).Row().ItemsCenter().Gap(6).Clip())
                    {
                        StatusSquare(shown.Status);
                        Label(shown.Path, "path", 12, weight: Weight.Medium);
                    }
                    Button(tab.Staged ? "Unstage File" : "Stage File", new Msg.Run(new(tab.Staged ? GitAction.Unstage : GitAction.Stage, [shown], Staged: tab.Staged)),
                        "stage", enabled: Model.BusyLabel(tab.Snapshot.Path) is null);
                }
                Rule("file-rule");
                using (C.Div(key: "diff-row").FillWidth().Flex().MinHeight(0).Row())
                    DiffView(tab);
            }
        }
        Composer(tab, C.Size.Width - sidebarSpace - width - ResizeThickness - 26);
    }
    void FileSection(RepoTab tab, bool staged, float? height = null)
    {
        var files = staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged;
        using var section = C.Div(key: staged ? "staged" : "unstaged").FillWidth().MinHeight(0);
        if (height is { } fixedHeight)
            section.Height(fixedHeight);
        else
            section.Flex();
        using (C.Div(key: "header").FillWidth().Height(34).Row().ItemsCenter().Padding(new Edges(0, 8, 0, 12)).Background(Pane))
        {
            using (C.Div(key: "title").Flex().Row().ItemsCenter().Gap(6))
            {
                Label(staged ? "Staged" : "Unstaged", "label", 13, weight: Weight.Semibold);
                if (files.Length > 0)
                    Label(files.Length.ToString(C.Culture), "count", 12, Muted);
            }
            Button(staged ? "Unstage All" : "Stage All", new Msg.Run(new(staged ? GitAction.UnstageAll : GitAction.StageAll)), "all",
                enabled: files.Length > 0 && Model.BusyLabel(tab.Snapshot.Path) is null);
        }
        Rule("header-rule");
        if (files.IsEmpty)
        {
            Empty(staged ? "Nothing staged" : "Working tree clean");
            return;
        }
        var rows = FileTree.Rows(files, tab.Folders, staged ? "staged:" : "unstaged:");
        // With nothing picked yet, the file the diff pane shows counts as selected, as in the original.
        var selection = tab.Staged == staged ? tab.Selected.IsEmpty && tab.File is { } shown ? [shown] : tab.Selected : ImmutableArray<string>.Empty;
        using var list = C.Div(key: "list").FillWidth().Flex().MinHeight(0).Padding(new Edges(4, 0, 0, 0))
            .Semantics(Role.Tree, staged ? "Staged files" : "Unstaged files");
        var focused = C.FocusWithin(list.Id);
        var reveal = tab.RevealFiles && tab.Staged == staged && tab.Cursor is { } cursor ? rows.FindIndex(r => r.Key == cursor) : -1;
        C.UniformList(staged ? "staged-rows" : "unstaged-rows", rows.Length, 21, (_, index) =>
        {
            using var inset = C.Div(key: "inset").FillWidth().FillHeight().Px(4);
            TreeRowView(tab, rows[index], staged, selection.Contains(rows[index].Key), focused);
        },
            revealRow: reveal >= 0 ? reveal : null, rowKey: index => StableId(rows[index].Key));
        if (reveal >= 0)
            Ui.Dispatch(new Msg.Revealed(tab.Snapshot.Path, false));
        if (focused && !C.IsEditing)
            SectionKeys(tab, rows, staged, selection);
    }
    void TreeRowView(RepoTab tab, TreeRow row, bool staged, bool selected, bool focused)
    {
        var strong = selected && focused;
        var indent = 6 + row.Depth * 16f;
        using var box = C.Div(key: "row").FillWidth().FillHeight().Row().ItemsCenter().Gap(row.Folder ? 5 : 6)
            .Padding(new Edges(0, 6, 0, row.Folder ? indent : indent + 16)).Rounded(4).Clip();
        var name = row.Folder ? (staged ? "Staged folder: " : "Unstaged folder: ") + row.Path : (staged ? "Staged: " : "Unstaged: ") + row.Path;
        var hit = box.Semantics(Role.Button, name).Control();
        if (selected)
            box.Background(focused ? Accent : SelectedSoft);
        else if (hit.Hovered)
            box.Background(Hover);
        if (hit.Focused && C.FocusVisible && !selected)
            box.Border(Accent, 2);
        var key = row.Key;
        // Roving focus: keyboard focus follows the cursor row, so collapsing or removing rows keeps the section focused.
        if (focused && tab.Staged == staged && tab.Cursor == key && !hit.Focused)
            C.FocusControl(hit.Id);
        if (hit.Clicked)
        {
            // Real hosts report Command (macOS) or Control as the primary modifier; Control-click on macOS is a context menu.
            var input = C.Input;
            var shift = (input.Modifiers & Modifiers.Shift) != 0;
            if ((input.Modifiers & Modifiers.Control) != 0 && !input.Primary && hit.Rect is { } rect)
                Ui.Dispatch(new Msg.OpenMenu(new(rect.X + 20, rect.Bottom), MenuKind.Files, key, staged));
            else if (hit.DoubleClicked && !row.Folder && !input.Primary && !shift)
                Ui.Dispatch(new Msg.Run(new(staged ? GitAction.Unstage : GitAction.Stage, [row.File!], Staged: staged)));
            else
                Ui.Dispatch(new Msg.SelectFile(key, staged, input.Primary, shift && !input.Primary));
        }
        Ui.OnContextMenu(hit, at => new Msg.OpenMenu(at, MenuKind.Files, key, staged));
        var ink = strong ? Color.White : Text;
        if (row.Folder)
        {
            var collapsed = tab.Folders.Contains((staged ? "staged:" : "unstaged:") + row.Path);
            C.Icon(collapsed ? Icons.ChevronRight : Icons.ChevronDown, 11, strong ? Color.White : Muted, key: "chevron");
            C.Icon(Icons.Folder, 14, strong ? Color.White : Color.Hex(0x4a90e2), key: "icon");
            Label(row.Name, "name", 12, ink);
            return;
        }
        StatusSquare(row.File!.Status);
        Label(row.Name, "name", 12, ink);
        if (row.File.OriginalPath is { } original)
            Label("← " + original, "original", 11, strong ? SelectedMuted : Muted);
    }
    /// <summary>The original keyboard model for a focused section.</summary>
    void SectionKeys(RepoTab tab, ImmutableArray<TreeRow> rows, bool staged, ImmutableArray<string> selection)
    {
        var shift = (C.Input.Modifiers & Modifiers.Shift) != 0;
        if (C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut(Key.Down, Modifiers.Shift))
            Ui.Dispatch(new Msg.StepFile(1, shift, staged));
        else if (C.Shortcut(Key.Up, Modifiers.None) || C.Shortcut(Key.Up, Modifiers.Shift))
            Ui.Dispatch(new Msg.StepFile(-1, shift, staged));
        else if (C.Shortcut(Key.Home, Modifiers.None) || C.Shortcut(Key.Home, Modifiers.Shift))
            Ui.Dispatch(new Msg.StepFile(int.MinValue, shift, staged));
        else if (C.Shortcut(Key.End, Modifiers.None) || C.Shortcut(Key.End, Modifiers.Shift))
            Ui.Dispatch(new Msg.StepFile(int.MaxValue, shift, staged));
        else if (C.Shortcut(Key.Left, Modifiers.None) && (tab.Cursor ?? tab.File) is { } cursor && rows.FindIndex(r => r.Key == cursor) is var at and >= 0)
        {
            var row = rows[at];
            var prefix = staged ? "staged:" : "unstaged:";
            if (row.Folder && !tab.Folders.Contains(prefix + row.Path))
                Ui.Dispatch(new Msg.SetFolder(prefix + row.Path, false));
            else if (row.Path.LastIndexOf('/') is var slash and > 0)
                Ui.Dispatch(new Msg.SelectFile(row.Path[..slash] + "/", staged));
        }
        else if (C.Shortcut(Key.Right, Modifiers.None) && tab.Cursor is { } folder && folder.EndsWith('/'))
            Ui.Dispatch(new Msg.SetFolder((staged ? "staged:" : "unstaged:") + folder[..^1], true));
        else if (C.Shortcut(Key.Space, Modifiers.None) || C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(new Msg.RunSelection(staged ? GitAction.Unstage : GitAction.Stage, staged));
        else if (C.Shortcut(Key.Backspace, Modifiers.None) || C.Shortcut(Key.Delete, Modifiers.None))
            Ui.Dispatch(new Msg.RunSelection(GitAction.Revert, staged));
        else if (C.Input.Text is "s" && C.Input.Modifiers == Modifiers.None)
        {
            C.Input.Text = "";
            Ui.Dispatch(new Msg.RunSelection(GitAction.Stash, staged));
        }
        else if (C.Shortcut(Key.A))
            Ui.Dispatch(new Msg.SelectAllFiles());
        else if (C.Shortcut(Key.C))
            C.Clipboard = string.Join('\n', FileTree.Files(staged ? tab.Snapshot.Staged : tab.Snapshot.Unstaged, selection).Select(f => f.Path));
        else if (C.Shortcut(Key.Escape, Modifiers.None) && tab.Cursor is { } only)
            Ui.Dispatch(new Msg.SelectFile(only, staged));
    }
    /// <summary>The original Composer: subject and description in one bordered box, Amend, Commit & Push and Commit.</summary>
    void Composer(RepoTab tab, float width)
    {
        var busy = Model.BusyLabel(tab.Snapshot.Path);
        var staged = tab.Snapshot.Staged.Length;
        var enabled = busy is null && tab.Subject.Trim().Length > 0 && (staged > 0 || tab.Amend);
        Rule("composer-rule");
        using var composer = C.Div(key: "composer").FillWidth().Gap(8).Padding(12).Background(Pane);
        using (C.Div(key: "message").FillWidth().Rounded(6).Border(BorderStrong).Background(Canvas).Clip())
        {
            Field("subject", "Commit subject", tab.Subject, static text => new Msg.EditSubject(text), "Commit subject", width, 28, 13, borderless: true);
            Rule("message-rule");
            Field("body", "Description", tab.Body, static text => new Msg.EditBody(text), "Description", width, 50, 12, borderless: true, multiline: true);
        }
        using var actions = C.Div(key: "actions").FillWidth().Row().Wrap().ItemsCenter().Gap(8);
        Ui.Checkbox("Amend", tab.Amend, static on => new Msg.SetAmend(on), key: "amend");
        using (C.Div(key: "spacer").Flex())
        {
        }
        using var buttons = C.Div(key: "buttons").Row().Wrap().MaxWidth(Length.Percent(100)).ItemsCenter().Gap(8);
        if (width >= 520)
            Label("⌘↵ commit · ⇧⌘↵ commit & push", "hint", 11, Muted);
        Button("Commit & Push", new Msg.Run(new(GitAction.Commit, ThenPush: true)), "commit-push", enabled: enabled);
        var label = busy == "Committing…" ? "Committing…" : tab.Amend ? "Amend" : staged > 0 ? $"Commit {staged} file{(staged == 1 ? "" : "s")}" : "Commit";
        Button(label, new Msg.Run(new(GitAction.Commit)), "commit", primary: true, enabled: enabled);
    }
}
