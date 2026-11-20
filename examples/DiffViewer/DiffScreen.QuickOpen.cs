using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffScreen
{
    const int QuickRows = QuickOpen.Rows;
    const float QuickWidth = 640;

    /// <summary>
    /// The project palette: a query field over a window of rows around the highlight, with a heading wherever the
    /// group changes. Keys belong to the palette while it is open, so the diff cursor never moves underneath.
    /// </summary>
    void QuickOverlay(Palette p, QuickOpen quick)
    {
        var items = quick.Items;
        int selected = items.IsEmpty ? -1 : Math.Clamp(quick.Selected, 0, items.Length - 1);
        using var layer = C.Overlay("quick");
        if (C.Shortcut(Key.Escape, Modifiers.None))
            Ui.Dispatch(new Msg.CloseOverlay());
        else if (C.Shortcut(Key.Down, Modifiers.None) || C.Shortcut('n', Modifiers.Control))
            Ui.Dispatch(new Msg.QuickStep(1));
        else if (C.Shortcut(Key.Up, Modifiers.None) || C.Shortcut('p', Modifiers.Control))
            Ui.Dispatch(new Msg.QuickStep(-1));
        else if (C.Shortcut(Key.PageDown, Modifiers.None))
            Ui.Dispatch(new Msg.QuickStep(QuickRows));
        else if (C.Shortcut(Key.PageUp, Modifiers.None))
            Ui.Dispatch(new Msg.QuickStep(-QuickRows));
        else if (C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(new Msg.QuickAccept());
        using var backdrop = Backdrop("quick-backdrop");
        float width = MathF.Min(QuickWidth, C.Size.Width - 32);
        using var panel = C.Div(key: "panel").Absolute().Offset((C.Size.Width - width) / 2, 64).Width(width).Column().Padding(Space.S2)
            .Gap(2).Rounded(10).Background(p.Raised).Border(p.Border).Shadow(28, 12, Color.Black.Alpha(.25f))
            .Semantics(Role.Dialog, "Open project");
        panel.Control();
        using (C.Div(key: "input").Row().FillWidth().ItemsCenter().Gap(Space.S2).Px(Space.S1))
        {
            C.Icon(Hugeicons.Search01, 15, p.Muted, key: "icon");
            Ui.TextField("query" + quick.Field.ToString(C.Culture), "Open project", quick.Query, static text => new Msg.QuickQuery(text),
                new("Projects, hosts, # pull requests, @ commits, > commands", width - 150)
                {
                    AutoFocus = true
                });
            // The field shows the host being searched.
            var status = Model.HostRepos.Values.Any(r => r.Loading) ? "Finding repos…" : Model.Pulls.Loading ? "Loading pull requests…"
                : Model.Scanning ? "Scanning…" : $"{Model.Projects.Length.ToString(C.Culture)} projects";
            using (C.Div(key: "status").Flex().MinWidth(0).Clip())
                C.Text(status, new(11, Color: p.Muted)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "text");
        }
        int first = QuickOpen.FirstVisible(selected, items.Length);
        for (int i = first; i < Math.Min(items.Length, first + QuickRows); i++)
        {
            var item = items[i];
            if (i == first || items[i - 1].Group != item.Group)
                using (C.Div(key: "group:" + item.Group + i.ToString(C.Culture)).FillWidth().Height(22).Row().Items(Align.End).Px(Space.S2))
                    C.Text(item.Group.ToUpperInvariant(), new(10.5f, Weight.Semibold, p.Muted), key: "text");
            QuickRow(p, item, i, i == selected);
        }
        if (items.IsEmpty)
            using (C.Div(key: "none").FillWidth().Height(36).Center())
                C.Text(Model.Scanning ? "Scanning for repositories…" : "No matches", new(12, Color: p.Muted), key: "text");
    }

    void QuickRow(Palette p, QuickItem item, int index, bool selected)
    {
        using var row = C.Div(key: item.Id).Row().FillWidth().Height(30).ItemsCenter().Gap(Space.S2).Px(Space.S2).Rounded(6)
            .TabStop(false).Semantics(Role.Option, item.Label);
        var hit = row.Control();
        if (hit.Clicked)
            Ui.Dispatch(new Msg.QuickAccept(index));
        row.Background(selected ? p.AccentSoft : hit.Hovered ? p.Hover : p.Raised);
        C.Icon(item.Icon, 14, selected ? p.Accent : p.Muted, key: "icon");
        using (C.Div(key: "label").MaxWidth(260).Clip())
            C.Text(item.Label, new(12.5f, Weight.Semibold, p.Text)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        using (C.Div(key: "detail").Flex().MinWidth(0).Clip())
            C.Text(item.Detail, new(11.5f, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        if (item.Hint.Length > 0)
            using (C.Div(key: "hint").MaxWidth(200).Clip())
                C.Text(item.Hint, new(11.5f, Mono: true, Color: selected ? p.Accent : p.Muted)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "text");
    }
}
