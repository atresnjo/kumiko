using DiskMap.Core;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiskMap;

/// <summary>The scanning screen, the context menu, the Trash confirmation and the shortcut sheet.</summary>
internal sealed partial class MapScreen
{
    /// <summary>Counters of the first scan; they count up smoothly between the ten updates a second.</summary>
    void Scanning(Palette p)
    {
        using var area = C.Div(key: "scanning").Flex().FillHeight().Center();
        using var card = C.Div(key: "card").Column().Width(440).Padding(Space.S6).Gap(Space.S4).Rounded(16)
            .Background(p.Surface).Border(p.Border).Semantics(Role.Group, "Scan progress");
        if (Model.Error is { } error)
        {
            C.Icon(Hugeicons.Alert02, 22, p.Danger, key: "icon");
            C.Text("Could not scan this folder", new(16, Weight.Semibold, p.Text), key: "title");
            C.Text(error, new(12, Color: p.Muted, Wrap: true), key: "error");
            using var choose = C.Div(key: "choose").Row().ItemsCenter().Gap(Space.S2).Height(32).Px(Space.S3).Rounded(8).Background(p.Accent).TabStop(false);
            Ui.Button(choose, "Choose Another Folder", new Msg.ChooseFolder(), true);
            C.Text("Choose Another Folder", new(12.5f, Weight.Semibold, p.AccentText), key: "label");
            return;
        }
        var progress = Model.Progress;
        using (C.Div(key: "heading").Row().ItemsCenter().Gap(Space.S3))
        {
            using (C.Div(key: "spinner").Width(30).Height(30).Rounded(9).Center().Background(p.AccentSoft))
                C.Icon(Hugeicons.HardDrive, 16, p.Accent, key: "icon");
            using (C.Div(key: "names").Column().Flex().MinWidth(0).Gap(2))
            {
                C.Text("Scanning", new(16, Weight.Semibold, p.Text), key: "title");
                C.Text(Abbreviate(Model.Root), Mono(11.5f, p.Muted) with
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "root");
            }
        }
        using (C.Div(key: "counters").Row().FillWidth().Gap(Space.S4))
        {
            Counter(p, "Size", progress?.Bytes ?? 0, bytes: true, "size");
            Counter(p, "Files", progress?.Files ?? 0, bytes: false, "files");
            Counter(p, "Folders", progress?.Folders ?? 0, bytes: false, "folders");
        }
        C.Text(progress is { Current.Length: > 0 } ? Abbreviate(progress.Current) : "Starting…", Mono(11, p.Faint) with
        {
            Overflow = TextOverflow.Ellipsis
        }, key: "current");
        C.Text(progress is null ? " " : Format.Duration(progress.Elapsed, C.Culture) + (progress.Errors > 0 ? $" · {Format.Count(progress.Errors, C.Culture)} unreadable" : ""),
            new(11, Color: p.Muted), key: "elapsed");
    }

    void Counter(Palette p, string label, long value, bool bytes, string key)
    {
        using var counter = C.Div(key: key).Column().Flex().Gap(4);
        var shown = (long)counter.Animate(value, Motion.Gentle);
        C.Text(label.ToUpperInvariant(), new(10, Weight.Semibold, p.Faint), key: "label");
        C.Text(bytes ? Format.Bytes(shown, C.Culture) : Format.Count(shown, C.Culture), Mono(20, p.Text, Weight.Medium), key: "value");
    }

    void MenuOverlay(Palette p, Menu menu)
    {
        var item = menu.Item;
        var node = item.Item;
        bool real = node.Kind != ItemKind.Rest;
        var path = real ? Model.PathOf(item) : "";
        bool marked = real && Model.Marked.Contains(path);
        using var overlay = C.Overlay("menu").TabStop(false).Semantics(Role.Group);
        if (overlay.Control().Clicked || C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.CloseMenu());
        float height = 6 * 30 + 2 * 9 + 34 + 12;
        using var panel = C.Div(key: "panel").Absolute()
            .Offset(Math.Clamp(menu.At.X, 6, MathF.Max(6, C.Size.Width - 246)), Math.Clamp(menu.At.Y, 6, MathF.Max(6, C.Size.Height - height - 6)))
            .Width(240).Padding(5).Rounded(10).Background(p.Raised).Border(p.Border)
            .Shadow(28, 10, Color.Black.Alpha(p.Dark ? .5f : .16f)).Semantics(Role.Menu, node.Name).TabStop(false)
            .Enter(Transition.Pop with
            {
                Motion = Motion.Quick
            });
        panel.Control();
        if (!C.FocusWithin(panel.Id))
            C.FocusNextControl();
        using (C.Div(key: "title").FillWidth().Px(8).Py(6))
            C.Text(node.Name, new(11, Weight.Semibold, p.Faint)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        MenuItem(p, "Open", Hugeicons.FolderOpen, "↵", new Msg.Open(item.Names), node.Opens);
        MenuItem(p, "Reveal in Finder", Hugeicons.Search01, "", new Msg.Reveal(path), real);
        MenuItem(p, "Open with Default App", Hugeicons.ArrowUpRight01, "", new Msg.OpenItem(path), real && !node.IsFolder);
        MenuSeparator(p, "first");
        CopyItem(p, path, real);
        MenuItem(p, marked ? "Unmark" : "Mark", Hugeicons.Tick02, "space", new Msg.ToggleMark(), real);
        MenuSeparator(p, "second");
        MenuItem(p, "Move to Trash…", Hugeicons.Delete02, "⌘⌫", new Msg.AskTrash(), real, destructive: true);
    }

    void MenuItem(Palette p, string label, string icon, string shortcut, Msg message, bool enabled, bool destructive = false)
    {
        using var row = C.Div(key: label).FillWidth().Height(30).Row().ItemsCenter().Gap(9).Px(8).Rounded(6).TabStop(enabled)
            .Semantics(Role.MenuItem, label);
        var hit = row.Control(enabled);
        Ui.OnPress(hit, message);
        bool lit = enabled && (hit.Hovered || hit.Focused && C.FocusVisible);
        if (lit)
            row.Background(destructive ? p.DangerFill : p.Accent);
        var color = !enabled ? p.Faint : lit ? (destructive ? Color.White : p.AccentText) : destructive ? p.Danger : p.Text;
        C.Icon(icon, 14, color, key: "icon");
        using (C.Div(key: "label").Flex().MinWidth(0))
            C.Text(label, new(12.5f, Weight.Medium, color), key: "text");
        if (shortcut.Length > 0)
            C.Text(shortcut, Mono(10.5f, lit ? color : p.Faint), key: "shortcut");
    }

    /// <summary>Copies on the click itself; the clipboard belongs to the window, not to an update.</summary>
    void CopyItem(Palette p, string path, bool enabled)
    {
        using var row = C.Div(key: "copy").FillWidth().Height(30).Row().ItemsCenter().Gap(9).Px(8).Rounded(6).TabStop(enabled)
            .Semantics(Role.MenuItem, "Copy Path");
        var hit = row.Control(enabled);
        bool lit = enabled && (hit.Hovered || hit.Focused && C.FocusVisible);
        if (lit)
            row.Background(p.Accent);
        if (hit.Clicked && enabled)
        {
            Copy(path);
            Ui.Dispatch(new Msg.CloseMenu());
        }
        var color = !enabled ? p.Faint : lit ? p.AccentText : p.Text;
        C.Icon(Hugeicons.Copy01, 14, color, key: "icon");
        using (C.Div(key: "label").Flex().MinWidth(0))
            C.Text("Copy Path", new(12.5f, Weight.Medium, color), key: "text");
        C.Text("⌘C", Mono(10.5f, lit ? color : p.Faint), key: "shortcut");
    }

    void MenuSeparator(Palette p, string key)
    {
        using (C.Div(key: key).FillWidth().Height(1).Margin(new Edges(4, 6, 4, 6)).Background(p.Border))
        {
        }
    }

    void ConfirmOverlay(Palette p, Confirm confirm)
    {
        using var overlay = C.Overlay("confirm").Center().Background(Color.Black.Alpha(p.Dark ? .55f : .28f)).TabStop(false);
        overlay.Control();
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.CancelTrash());
        else if (C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(new Msg.ConfirmTrash());
        using var dialog = C.Div(key: "dialog").Column().Width(400).Padding(Space.S5).Gap(Space.S4).Rounded(14)
            .Background(p.Raised).Border(p.Border).Shadow(40, 14, Color.Black.Alpha(.4f)).Semantics(Role.Dialog, "Move to Trash")
            .Enter(Transition.Pop with
            {
                Motion = Motion.Quick
            });
        var what = confirm.Paths.Length == 1 ? Path.GetFileName(confirm.Paths[0]) : $"{Format.Count(confirm.Paths.Length, C.Culture)} items";
        using (C.Div(key: "heading").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            using (C.Div(key: "icon").Width(34).Height(34).Rounded(10).Center().Background(p.Danger.Alpha(.14f)))
                C.Icon(Hugeicons.Delete02, 17, p.Danger, key: "glyph");
            using (C.Div(key: "names").Column().Flex().MinWidth(0).Gap(2))
            {
                C.Text($"Move {what} to the Trash?", new(14.5f, Weight.Semibold, p.Text)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "title");
                C.Text($"Frees about {Format.Bytes(confirm.Size, C.Culture)} once the Trash is emptied.", new(12, Color: p.Muted, Wrap: true), key: "detail");
            }
        }
        using (C.Div(key: "buttons").Row().FillWidth().Gap(Space.S2).Justify(Justify.End))
        {
            DialogButton(p, "Cancel", "cancel", new Msg.CancelTrash(), false);
            DialogButton(p, "Move to Trash", "trash", new Msg.ConfirmTrash(), true);
        }
    }

    void DialogButton(Palette p, string label, string key, Msg message, bool primary)
    {
        using var button = C.Div(key: key).Height(32).Px(Space.S4).Center().Rounded(8).TabStop(true);
        var hit = Ui.Button(button, label, message, true);
        if (primary)
            button.Background(hit.Hovered ? Color.Mix(p.DangerFill, Color.Black, .12f) : p.DangerFill);
        else
            button.Background(hit.Hovered ? p.Hover : p.Surface).Border(p.Border);
        if (hit.Focused && C.FocusVisible)
            button.Border(p.Accent, 2);
        C.Text(label, new(12.5f, Weight.Semibold, primary ? Color.White : p.Text), key: "label");
    }

    void HelpOverlay(Palette p)
    {
        using var overlay = C.Overlay("help").Center().Background(Color.Black.Alpha(p.Dark ? .55f : .28f)).TabStop(false);
        if (overlay.Control().Clicked || C.Shortcut(Key.Escape, Modifiers.None) || C.Shortcut('?'))
            Ui.Dispatch(new Msg.SetHelp(false));
        using var sheet = C.Div(key: "sheet").Column().Width(420).Padding(Space.S5).Gap(Space.S2).Rounded(14)
            .Background(p.Raised).Border(p.Border).Semantics(Role.Dialog, "Keyboard shortcuts").Enter(Transition.Pop with
            {
                Motion = Motion.Quick
            });
        C.Text("Keyboard shortcuts", new(15, Weight.Semibold, p.Text), key: "title");
        Shortcut(p, "Click / ↑ ↓", "Select an item");
        Shortcut(p, "Click again / ↵ / →", "Open the selected folder");
        Shortcut(p, "⌫ / ← / Esc", "Back to the enclosing folder");
        Shortcut(p, "Right-click", "Reveal, copy, mark or trash");
        Shortcut(p, "Space", "Mark or unmark for the Trash");
        Shortcut(p, "⌘⌫", "Move marked or selected items to the Trash");
        Shortcut(p, "⌘C", "Copy the selected path");
        Shortcut(p, "⌘F", "Filter names");
        Shortcut(p, "1  2  3", "Disk size, apparent size, file count");
        Shortcut(p, "⌘R", "Rescan the open folder");
        Shortcut(p, "⌘O", "Choose a folder to scan");
    }

    void Shortcut(Palette p, string keys, string action)
    {
        using var row = C.Div(key: action).Row().FillWidth().ItemsCenter().Gap(Space.S3).Height(26);
        using (C.Div(key: "keys").Width(150))
            C.Text(keys, Mono(11.5f, p.Accent), key: "text");
        C.Text(action, new(12.5f, Color: p.Text), key: "action");
    }
}
