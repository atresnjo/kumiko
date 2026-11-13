using DiskMap.Core;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiskMap;

/// <summary>
/// The window: a toolbar with the breadcrumb and metric, the treemap, a sidebar with details and the folder's
/// items, and a status line. Tiles and list rows read the immutable layout and tree computed by updates.
/// </summary>
internal sealed partial class MapScreen(Model model) : Screen<Model, Msg>(model)
{
    const float ToolbarHeight = 52;
    const float StatusHeight = 28;
    const float SidebarWidth = 328;
    /// <summary>Monospaced figures; they truncate with an ellipsis, as tiles and columns can be narrower than they are.</summary>
    static TextStyle Mono(float size, Color color, Weight weight = Weight.Regular) => new(size, weight, color, Mono: true)
    {
        Overflow = TextOverflow.Ellipsis
    };

    protected override void Render()
    {
        var p = C.Theme.Dark ? Palette.DarkMode : Palette.LightMode;
        using var page = C.Div(key: "page").SizeFull().Background(p.Canvas);
        Toolbar(p);
        Rule(p, "toolbar-rule");
        ulong filter = 0;
        using (C.Div(key: "body").Row().FillWidth().Flex().MinHeight(0))
        {
            if (Model.Loaded)
            {
                using (C.Div(key: "main").Column().Flex().FillHeight().MinWidth(0))
                {
                    filter = Subbar(p);
                    Map(p);
                }
                Rule(p, "sidebar-rule", vertical: true);
                Sidebar(p);
            }
            else
                Scanning(p);
        }
        Rule(p, "status-rule");
        StatusBar(p);
        if (Model.Menu is { } menu)
            MenuOverlay(p, menu);
        if (Model.Confirm is { } confirm)
            ConfirmOverlay(p, confirm);
        if (Model.Help)
            HelpOverlay(p);
        if (Model.Menu is null && Model.Confirm is null && !Model.Help)
            Keys(filter);
    }

    void Toolbar(Palette p)
    {
        using var bar = C.Div(key: "toolbar").Row().FillWidth().Height(ToolbarHeight).ItemsCenter().Gap(Space.S3)
            .Px(Space.S4).Background(p.Surface);
        using (C.Div(key: "brand").Row().ItemsCenter().Gap(Space.S2))
        {
            using (C.Div(key: "mark").Width(26).Height(26).Rounded(7).Center().Background(p.Accent))
                C.Icon(Hugeicons.DashboardSquare01, 15, p.AccentText, key: "icon");
            C.Text("DiskMap", new(14, Weight.Semibold, p.Text), key: "name");
        }
        using (C.Div(key: "divider").Width(1).Height(20).Background(p.Border))
        {
        }
        Crumbs(p);
        using (C.Div(key: "metrics").Row().Rounded(8).Border(p.Border).Padding(2).Gap(2).Background(p.Well).Semantics(Role.Group, "Measure"))
        {
            Segment(p, "Disk size", "disk", Metric.Disk);
            Segment(p, "Apparent", "apparent", Metric.Apparent);
            Segment(p, "Files", "files", Metric.Files);
        }
        IconButton(p, Hugeicons.Refresh, "Rescan folder", "rescan", new Msg.Rescan(), Model.Loaded && Model.Scanning is null);
        IconButton(p, Hugeicons.FolderOpen, "Choose folder", "choose", new Msg.ChooseFolder(), true);
        IconButton(p, Hugeicons.Keyboard, "Keyboard shortcuts", "help", new Msg.SetHelp(true), true);
    }

    /// <summary>The scanned root's path, then one button per opened folder.</summary>
    void Crumbs(Palette p)
    {
        using var crumbs = C.Div(key: "crumbs").Row().ItemsCenter().Gap(2).Flex().MinWidth(0).Semantics(Role.Group, "Path");
        var trail = Model.Trail;
        if (trail.IsEmpty)
        {
            C.Text(Model.Root, new(13, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "root");
            return;
        }
        // Long trails keep the root and the last three folders.
        int first = trail.Length > 5 ? trail.Length - 3 : 1;
        for (int i = 0; i < trail.Length; i++)
        {
            if (i > 0 && i < first)
                continue;
            if (i > 0)
            {
                C.Icon(Hugeicons.ArrowRight01, 13, p.Faint, key: (ulong)(1000 + i));
                if (i == first && first > 1)
                {
                    C.Text("…", new(13, Color: p.Faint), key: "gap");
                    C.Icon(Hugeicons.ArrowRight01, 13, p.Faint, key: "gap-arrow");
                }
            }
            var name = i == 0 ? Model.Root : trail[i].Name;
            bool last = i == trail.Length - 1;
            using var crumb = C.Div(key: (ulong)(i + 1)).Row().ItemsCenter().Gap(6).Height(28).Px(Space.S2).Rounded(6).TabStop(false).MinWidth(0).Shrink();
            var hit = Ui.Button(crumb, name, new Msg.Crumb(i), !last);
            if (hit.Hovered && !last)
                crumb.Background(p.Hover);
            if (i == 0)
                C.Icon(Hugeicons.HardDrive, 14, last ? p.Text : p.Muted, key: "drive");
            C.Text(i == 0 ? Abbreviate(name) : name, new(13, last ? Weight.Semibold : Weight.Medium, last ? p.Text : p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "name");
        }
    }

    /// <summary>A path with the home folder written as ~.</summary>
    string Abbreviate(string path)
    {
        var home = Model.Home;
        return home.Length > 1 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    void Segment(Palette p, string label, string key, Metric metric)
    {
        bool selected = Model.Metric == metric;
        using var button = C.Div(key: key).Px(Space.S3).Height(26).Center().Rounded(6).TabStop(false);
        var hit = Ui.Button(button, label, new Msg.SetMetric(metric), true);
        if (selected)
            button.Background(p.Raised).Border(p.Border).SharedLayout("metric");
        else if (hit.Hovered)
            button.Background(p.Hover);
        C.Text(label, new(12, selected ? Weight.Semibold : Weight.Medium, selected ? p.Text : p.Muted), key: "label");
    }

    void IconButton(Palette p, string icon, string label, string key, Msg message, bool enabled)
    {
        using var button = C.Div(key: key).Width(32).Height(32).Center().Rounded(8).TabStop(false);
        var hit = Ui.Button(button, label, message, enabled);
        if (hit.Hovered && enabled)
            button.Background(p.Hover);
        C.Icon(icon, 17, enabled ? p.Muted : p.Faint.Alpha(.5f), key: "icon");
    }

    void Rule(Palette p, string key, bool vertical = false)
    {
        using var rule = C.Div(key: key).Background(p.Border);
        if (vertical)
            rule.Width(1).FillHeight();
        else
            rule.FillWidth().Height(1);
    }

    /// <summary>Totals of the open folder, the size legend and the name filter; returns the filter's group.</summary>
    ulong Subbar(Palette p)
    {
        using var bar = C.Div(key: "subbar").Row().FillWidth().Height(44).ItemsCenter().Gap(Space.S3).Px(Space.S4);
        var folder = Model.Folder!.Value;
        C.Text(Format.Bytes(Model.Metric == Metric.Apparent ? folder.Apparent : folder.Size, C.Culture), Mono(13, p.Text, Weight.Semibold), key: "size");
        C.Text("·", new(13, Color: p.Faint), key: "dot");
        C.Text(Format.Count(folder.Files, C.Culture) + " files", Mono(12, p.Muted), key: "files");
        C.Text(Format.Count(folder.Folders, C.Culture) + " folders", Mono(12, p.Muted), key: "folders");
        if (folder.Errors > 0)
            C.Text(Format.Count(folder.Errors, C.Culture) + " unreadable", Mono(12, p.Danger), key: "errors");
        C.Spacer(key: "space");
        if (!Model.Marked.IsEmpty)
        {
            using var marked = C.Div(key: "marked").Row().ItemsCenter().Gap(Space.S2).Height(28).Px(Space.S3).Rounded(7).Background(p.Mark.Alpha(.14f)).TabStop(false);
            var hit = Ui.Button(marked, "Move marked items to the Trash", new Msg.AskTrash(), true);
            if (hit.Hovered)
                marked.Background(p.Mark.Alpha(.22f));
            C.Icon(Hugeicons.Delete02, 14, p.Mark, key: "icon");
            C.Text($"{Format.Count(Model.Marked.Count, C.Culture)} marked", new(12, Weight.Semibold, p.Mark), key: "count");
        }
        using var field = C.Div(key: "filter").Row().ItemsCenter().Gap(Space.S2).Semantics(Role.Group, "Filter");
        if (C.FocusWithin(field.Id) && C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.SetFilter(""));
        C.Icon(Hugeicons.Search01, 14, p.Faint, key: "icon");
        Ui.TextField("field-" + Model.FilterFocus, "Filter names", Model.Filter, static text => new Msg.SetFilter(text),
            new("Filter names…", 200)
            {
                AutoFocus = Model.FilterFocus > 0
            });
        return field.Id;
    }

    void StatusBar(Palette p)
    {
        using var bar = C.Div(key: "status").Row().FillWidth().Height(StatusHeight).ItemsCenter().Gap(Space.S4)
            .Px(Space.S4).Background(p.Surface);
        var hint = new TextStyle(11, Color: p.Muted);
        if (Model.Loaded)
        {
            Hint(p, "↵", "open", "open");
            Hint(p, "⌫", "back", "back");
            Hint(p, "↑↓", "select", "select");
            Hint(p, "space", "mark", "mark");
            Hint(p, "⌘⌫", "trash", "trash");
            Hint(p, "⌘R", "rescan", "rescan");
            Hint(p, "⌘F", "filter", "filter");
            Hint(p, "1 2 3", "measure", "measure");
        }
        C.Spacer(key: "space");
        string state;
        Color color = p.Muted;
        if (Model.Notice is { } notice)
            (state, color) = (notice, notice.StartsWith("Could not", StringComparison.Ordinal) ? p.Danger : p.Muted);
        else if (Model.Error is { } error)
            (state, color) = (error, p.Danger);
        else if (Model.Scanning is not null && Model.Loaded)
            state = Model.Progress is { } progress ? $"Rescanning… {Format.Count(progress.Files, C.Culture)} files" : "Rescanning…";
        else if (Model.Loaded)
            state = Model.ScanTime > TimeSpan.Zero
                ? $"Scanned {Format.Count(Model.Tree!.Value.Files, C.Culture)} files in {Format.Duration(Model.ScanTime, C.Culture)} · links and other volumes are not followed"
                : $"{Format.Count(Model.Tree!.Value.Files, C.Culture)} files · links and other volumes are not followed";
        else
            state = "Scanning…";
        C.Text(state, hint with
        {
            Color = color,
            Overflow = TextOverflow.Ellipsis
        }, key: "state");
    }

    void Hint(Palette p, string keys, string label, string key)
    {
        using var hint = C.Div(key: key).Row().ItemsCenter().Gap(5);
        using (C.Div(key: "keys").Px(5).Height(17).Center().Rounded(4).Background(p.Raised).Border(p.Border))
            C.Text(keys, Mono(10, p.Muted), key: "text");
        C.Text(label, new(11, Color: p.Muted), key: "label");
    }
}
