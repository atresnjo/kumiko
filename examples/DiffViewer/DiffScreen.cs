using DiffViewer.Core;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>
/// The whole window: toolbar, file sidebar, the virtual diff and a status line. Only rows inside the viewport
/// are built; every frame reads the immutable layout computed by updates.
/// </summary>
internal sealed partial class DiffScreen(Model model) : Screen<Model, Msg>(model)
{
    const float SidebarWidth = 300;
    const float ToolbarHeight = 44;
    const float StatusHeight = 26;
    static TextStyle Code(Palette p) => new(12, Mono: true, Color: p.Text);
    bool SidebarShown => !Model.SidebarHidden && Model.Zen is null;

    protected override void Render()
    {
        var p = C.Theme.Dark ? Palette.Dark : Palette.Light;
        using var page = C.Div(key: "page").SizeFull().Background(p.Canvas);
        bool zen = Model.Zen is not null;
        if (!zen)
        {
            Toolbar(p);
            Rule(p, "toolbar-rule");
        }
        ulong searchField = 0;
        if (Model.Search is { } search)
            searchField = SearchBar(p, search);
        VirtualListView view = default;
        using (C.Div(key: "body").Row().FillWidth().Flex().MinHeight(0))
        {
            if (SidebarShown)
                Sidebar(p);
            view = Pane(p);
        }
        if (!zen)
        {
            Rule(p, "status-rule");
            StatusBar(p);
        }
        if (Model.Finder is { } finder)
            FinderOverlay(p, finder);
        if (Model.Help)
            HelpOverlay(p);
        if (Model.Quick is { } quick)
            QuickOverlay(p, quick);
        if (Model.Finder is null && !Model.Help && Model.Quick is null)
            Keys(view, searchField);
    }

    void Toolbar(Palette p)
    {
        using var bar = C.Div(key: "toolbar").Row().FillWidth().Height(ToolbarHeight).ItemsCenter().Gap(Space.S3)
            .Px(Space.S4).Background(p.Surface);
        var document = Model.Document;
        C.Icon(Hugeicons.GitCompare, 18, p.Muted, key: "logo");
        using (C.Div(key: "title").Row().ItemsCenter().Gap(Space.S2).Flex().MinWidth(0))
        {
            C.Text(document.Title.Length > 0 ? document.Title : "Diff", new(13, Weight.Semibold, p.Text)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "name");
            C.Text(document.Detail, new(12, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "detail");
            if (Model.Loading)
                C.Text("Loading…", new(12, Color: p.Muted), key: "loading");
        }
        if (document.Files.Length > 0)
        {
            var files = document.Files.Length == 1 ? "1 file" : $"{document.Files.Length.ToString(C.Culture)} files";
            C.Text(files, new(12, Color: p.Muted), key: "files");
            C.Text($"+{document.Additions.ToString(C.Culture)}", new(12, Weight.Semibold, p.AddedText), key: "added");
            C.Text($"−{document.Deletions.ToString(C.Culture)}", new(12, Weight.Semibold, p.RemovedText), key: "removed");
        }
        using (C.Div(key: "modes").Row().Rounded(6).Border(p.Border).Padding(2).Gap(2).Background(p.Canvas))
        {
            Segment(p, "Unified", "unified", Model.Mode == ViewMode.Unified, new Msg.SetMode(ViewMode.Unified));
            Segment(p, "Split", "split", Model.Mode == ViewMode.Split, new Msg.SetMode(ViewMode.Split));
        }
        Segment(p, Model.IgnoreWhitespace ? "Whitespace hidden" : "Whitespace", "whitespace", Model.IgnoreWhitespace, new Msg.ToggleWhitespace());
        IconButton(p, Hugeicons.FullScreen, "Zen mode", "zen", new Msg.ToggleZen());
        IconButton(p, Hugeicons.FolderOpen, "Open project", "projects", new Msg.OpenQuick());
        IconButton(p, Hugeicons.Refresh, "Reload", "reload", new Msg.Reload());
        IconButton(p, Hugeicons.Keyboard, "Keyboard shortcuts", "help", new Msg.SetHelp(true));
    }

    void Rule(Palette p, string key, bool vertical = false)
    {
        using var rule = C.Div(key: key).Background(p.Border);
        if (vertical)
            rule.Width(1).FillHeight();
        else
            rule.FillWidth().Height(1);
    }

    void Segment(Palette p, string label, string key, bool selected, Msg message)
    {
        using var button = C.Div(key: key).Px(Space.S3).Height(26).Center().Rounded(5).TabStop(false);
        var hit = Ui.Button(button, label, message, true);
        if (selected)
            button.Background(p.AccentSoft);
        else if (hit.Hovered)
            button.Background(p.Hover);
        C.Text(label, new(12, selected ? Weight.Semibold : Weight.Regular, selected ? p.Accent : p.Text), key: "label");
    }

    void IconButton(Palette p, string icon, string label, string key, Msg message)
    {
        using var button = C.Div(key: key).Width(30).Height(30).Center().Rounded(6).TabStop(false);
        var hit = Ui.Button(button, label, message, true);
        if (hit.Hovered)
            button.Background(p.Hover);
        C.Icon(icon, 16, p.Muted, key: "icon");
    }

    void StatusBar(Palette p)
    {
        using var bar = C.Div(key: "status").Row().FillWidth().Height(StatusHeight).ItemsCenter().Gap(Space.S3)
            .Px(Space.S4).Background(p.Surface);
        var muted = new TextStyle(11, Color: p.Muted);
        string where = Model.Error ?? "";
        if (Model.Error is null && Model.CursorFile is { } file)
        {
            var row = Model.Layout.Rows[Model.Cursor];
            int line = RowLayout.LineOf(row);
            where = line < 0 ? file.Path : $"{file.Path}:{LineNumber(file, line).ToString(C.Culture)}";
        }
        else if (Model.Error is null && !Model.Loading && Model.Document.Files.IsEmpty)
            where = "No changes";
        C.Text(where, muted with
        {
            Color = Model.Error is null ? p.Muted : p.RemovedText,
            Overflow = TextOverflow.Ellipsis
        }, key: "where");
        C.Spacer(key: "space");
        C.Text("⌘K projects   j k move   n p hunk   ⇧N ⇧P file   x fold   s split   z zen   / search   ⌘P files   ? keys", muted, key: "hints");
    }

    static int LineNumber(FileDiff file, int line)
    {
        var diff = file.Lines[line];
        return (diff.Kind == LineKind.Removed ? diff.Old : diff.New) + 1;
    }
}
