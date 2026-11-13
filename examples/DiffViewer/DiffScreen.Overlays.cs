using DiffViewer.Core;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffScreen
{
    const int FinderRows = 12;

    /// <summary>The search bar; returns its id so diff keys stay quiet while the query field has focus.</summary>
    ulong SearchBar(Palette p, Search search)
    {
        using var bar = C.Div(key: "search").Row().FillWidth().Height(40).ItemsCenter().Gap(Space.S2).Px(Space.S4).Background(p.Surface)
            .Semantics(Role.Group, "Search");
        // Enter and Escape belong to the bar before its field sees them.
        if (C.FocusWithin(bar.Id))
        {
            if (C.Shortcut(Key.Enter, Modifiers.Shift) || C.Shortcut('G', Modifiers.Primary))
                Ui.Dispatch(new Msg.SearchStep(-1));
            else if (C.Shortcut(Key.Enter, Modifiers.None) || C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut('g', Modifiers.Primary))
                Ui.Dispatch(new Msg.SearchStep(1));
            else if (C.Shortcut(Key.Up, Modifiers.None))
                Ui.Dispatch(new Msg.SearchStep(-1));
            else if (C.Shortcut(Key.Escape, Modifiers.None))
                Ui.Dispatch(new Msg.CloseOverlay());
        }
        C.Icon(Hugeicons.Search01, 15, p.Muted, key: "icon");
        Ui.TextField("query", "Search changed lines", search.Query, static text => new Msg.SearchQuery(text),
            new("Search changed lines", 360)
            {
                AutoFocus = true
            });
        var found = search.Found;
        var count = search.Query.Length == 0 ? "" : found.IsEmpty ? "No matches" : $"{(search.Current + 1).ToString(C.Culture)} of {found.Length.ToString(C.Culture)}";
        C.Text(count, new(12, Color: p.Muted), key: "count");
        C.Spacer(key: "space");
        IconButton(p, Hugeicons.ArrowUp01, "Previous match", "previous", new Msg.SearchStep(-1));
        IconButton(p, Hugeicons.ArrowDown01, "Next match", "next", new Msg.SearchStep(1));
        IconButton(p, Hugeicons.Cancel01, "Close search", "close", new Msg.CloseOverlay());
        return bar.Id;
    }

    void FinderOverlay(Palette p, Finder finder)
    {
        var files = Model.Document.Files;
        var matches = FileFinder.Match(files, finder.Query);
        int selected = matches.IsEmpty ? -1 : Math.Clamp(finder.Selected, 0, matches.Length - 1);
        using var layer = C.Overlay("finder");
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.CloseOverlay());
        else if (C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut('n', Modifiers.Control))
            Ui.Dispatch(new Msg.FinderStep(1));
        else if (C.Shortcut(Key.Up, Modifiers.None) || C.Shortcut('p', Modifiers.Control))
            Ui.Dispatch(new Msg.FinderStep(-1));
        else if (C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(new Msg.FinderAccept());
        using var backdrop = Backdrop("finder-backdrop");
        using var panel = C.Div(key: "panel").Absolute().Offset((C.Size.Width - 560) / 2, 72).Width(560).Column().Padding(Space.S2)
            .Gap(Space.S1).Rounded(10).Background(p.Raised).Border(p.Border).Shadow(28, 12, Color.Black.Alpha(.25f))
            .Semantics(Role.Dialog, "Go to file");
        panel.Control();
        Ui.TextField("query", "Go to file", finder.Query, static text => new Msg.FinderQuery(text),
            new("Go to file", 544)
            {
                AutoFocus = true
            });
        int first = Math.Max(0, selected - FinderRows + 1);
        for (int i = first; i < Math.Min(matches.Length, first + FinderRows); i++)
        {
            var file = files[matches[i]];
            using var item = C.Div(key: file.Path).Row().FillWidth().Height(30).ItemsCenter().Gap(Space.S2).Px(Space.S2).Rounded(6)
                .TabStop(false).Semantics(Role.Option, file.Path);
            var hit = item.Control();
            if (hit.Clicked)
                Ui.Dispatch(new Msg.SelectFile(matches[i]));
            item.Background(i == selected ? p.AccentSoft : hit.Hovered ? p.Hover : p.Raised);
            Badge(p, file.Status, "status");
            C.Text(file.Name, new(12.5f, Weight.Semibold, p.Text), key: "name");
            C.Text(file.Directory, new(11.5f, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "directory");
        }
        if (matches.IsEmpty)
            using (C.Div(key: "none").FillWidth().Height(36).Center())
                C.Text("No matching files", new(12, Color: p.Muted), key: "text");
    }

    void HelpOverlay(Palette p)
    {
        using var layer = C.Overlay("help");
        if (C.Shortcut(Key.Escape, Modifiers.None) || C.Shortcut('?') || C.Shortcut('q'))
            Ui.Dispatch(new Msg.SetHelp(false));
        using var backdrop = Backdrop("help-backdrop");
        float width = Math.Min(1180, C.Size.Width - 2 * Space.S5);
        using var panel = C.Div(key: "panel").Absolute().Offset((C.Size.Width - width) / 2, 64).Width(width).Padding(Space.S5)
            .Gap(Space.S4).Rounded(12).Background(p.Raised).Border(p.Border).Shadow(28, 12, Color.Black.Alpha(.25f))
            .Semantics(Role.Dialog, "Keyboard shortcuts");
        panel.Control();
        using (C.Div(key: "title").Row().FillWidth().ItemsCenter())
        {
            C.Text("Keyboard shortcuts", new(16, Weight.Semibold, p.Text), key: "text");
            C.Spacer(key: "space");
            IconButton(p, Hugeicons.Cancel01, "Close", "close", new Msg.SetHelp(false));
        }
        using var columns = C.Div(key: "columns").Row().FillWidth().Gap(Space.S5);
        foreach (var (group, items) in Shortcuts)
        {
            using var column = C.Div(key: group).Flex().Gap(6);
            C.Text(group.ToUpperInvariant(), new(10.5f, Weight.Semibold, p.Muted), key: "heading");
            foreach (var (keys, action) in items)
            {
                using var line = C.Div(key: action).Row().FillWidth().ItemsCenter().Gap(Space.S2);
                C.Text(action, new(12, Color: p.Text), key: "action");
                C.Spacer(key: "space");
                C.Text(keys, new(11.5f, Mono: true, Color: p.Muted), key: "keys");
            }
        }
    }

    /// <summary>A dimmed layer that closes its overlay when pressed; the caller builds the panel inside it.</summary>
    Div Backdrop(string key)
    {
        var backdrop = C.Div(key: key).Absolute().Offset(0, 0).SizeFull().Background(Color.Black.Alpha(C.Theme.Dark ? .45f : .18f))
            .TabStop(false).Semantics(Role.Button, "Close");
        if (backdrop.Control().Clicked)
            Ui.Dispatch(new Msg.CloseOverlay());
        return backdrop;
    }
}
