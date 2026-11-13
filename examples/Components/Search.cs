using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    /// <summary>Every component in the showcase; IDs are "section:component".</summary>
    static readonly ImmutableArray<CommandItem> Catalog =
    [
        new("overview:button", "Button", "Overview", "plus", "Actions", "primary secondary outline ghost link"),
        new("overview:icon-button", "Icon button", "Overview", "pencil"),
        new("overview:badge", "Badge", "Overview", "checkmark.circle.fill", Keywords: "tag label status"),
        new("overview:kbd", "Kbd", "Overview", "command", Keywords: "keyboard shortcut key"),
        new("overview:alert", "Alert", "Overview", "info.circle", Keywords: "callout banner"),
        new("overview:avatar", "Avatar", "Overview", "person.fill", Keywords: "user profile group"),
        new("overview:progress", "Progress", "Overview", "gauge.with.dots.needle.33percent", Keywords: "bar loading"),
        new("overview:skeleton", "Skeleton", "Overview", "rectangle.on.rectangle", Keywords: "placeholder loading"),
        new("forms:field", "Field", "Forms", "text.cursor", Keywords: "input text label error"),
        new("forms:checkbox", "Checkbox", "Forms", "checklist"),
        new("forms:switch", "Switch", "Forms", "circle.lefthalf.filled", Keywords: "toggle"),
        new("forms:radio", "Radio group", "Forms", "circle", Keywords: "option choice"),
        new("forms:slider", "Slider", "Forms", "slider.horizontal.3", Keywords: "range volume"),
        new("forms:toggle-group", "Toggle group", "Forms", "square.grid.2x2", Keywords: "segmented"),
        new("navigation:tabs", "Tabs", "Navigation", "rectangle.split.2x1", Keywords: "segmented underline pill"),
        new("navigation:accordion", "Accordion", "Navigation", "list.bullet", Keywords: "collapsible disclosure"),
        new("navigation:breadcrumb", "Breadcrumb", "Navigation", "chevron.right"),
        new("navigation:pagination", "Pagination", "Navigation", "arrow.right", Keywords: "pages"),
        new("navigation:rating", "Rating", "Navigation", "star.fill", Keywords: "stars"),
        new("overlays:dialog", "Dialog", "Overlays", "rectangle.on.rectangle", Keywords: "modal confirm"),
        new("overlays:popover", "Popover", "Overlays", "ellipsis.circle", Keywords: "menu dropdown"),
        new("overlays:command", "Command menu", "Overlays", "magnifyingglass", Keywords: "search palette"),
        new("overlays:notification", "Notification", "Overlays", "info.circle", Keywords: "toast"),
        new("theme:theme", "Theme", "Theme", "slider.horizontal.3", Keywords: "color accent radius dark")
    ];

    static void SearchBox(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        var t = ui.Theme;
        if (c.Shortcut(Key.F))
            send(new ShowcaseMsg.SetSearchOpen(true));
        Rect? anchor;
        using (var box = c.Div(key: "search").Row().Width(240).Height(32).ItemsCenter().Gap(8).Px(10).Rounded(t.Radius)
            .Border(t.Input).Semantics(Role.Button, "Search components"))
        {
            var hit = box.Control();
            anchor = hit.Rect;
            if (hit.Hovered || m.SearchOpen)
                box.Background(t.Accent);
            if (hit.Focused)
                box.Border(t.Ring, t.Metrics.FocusWidth);
            if (hit.Hovered)
                c.Cursor = Cursor.Hand;
            c.Icon("magnifyingglass", 14, t.MutedForeground, key: "icon");
            using (c.Div(key: "hint").Flex())
                c.Text("Search components…", new(Size: t.FontSize - 1, Color: t.MutedForeground), key: "text");
            ui.Kbd(Kit.ShortcutLabel("F"), key: "shortcut");
            if (hit.Clicked)
                send(new ShowcaseMsg.SetSearchOpen(!m.SearchOpen));
        }
        if (!m.SearchOpen)
            return;
        ui.CommandMenu(anchor, m.Query, text => send(new ShowcaseMsg.SetQuery(text)), Catalog,
            id => send(new ShowcaseMsg.SearchResult(id[..id.IndexOf(':')])), () => send(new ShowcaseMsg.SetSearchOpen(false)),
            new("Search components…", Align: PopoverAlign.End), key: "search-menu");
    }

    static void AccountMenu(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        var t = ui.Theme;
        var name = m.Name.Length > 0 ? m.Name : "Guest";
        Rect? anchor;
        using (var row = c.Div(key: "account").Row().FillWidth().ItemsCenter().Gap(10).Padding(new Edges(8, 8, 8, 8))
            .Rounded(t.Radius).Semantics(Role.Button, "Account").Accessibility(new(Expanded: m.AccountOpen)))
        {
            var hit = row.Control();
            anchor = hit.Rect;
            if (hit.Hovered || m.AccountOpen)
                row.Background(t.SidebarAccent);
            if (hit.Focused)
                row.Border(t.Ring, t.Metrics.FocusWidth);
            if (hit.Hovered)
                c.Cursor = Cursor.Hand;
            ui.Avatar(name, key: "avatar");
            using (c.Div(key: "who").Flex().Column().Gap(2))
            {
                c.Text(name, new(Size: t.FontSize - 1, Weight: Weight.Medium, Color: t.SidebarForeground), key: "name");
                c.Text("ada@example.com", new(Size: t.FontSize - 2, Color: Color.Mix(t.MutedForeground, t.SidebarForeground, .2f)),
                    key: "email");
            }
            c.Icon("chevron.up.chevron.down", 14, t.MutedForeground, key: "chevron");
            if (hit.Clicked)
                send(new ShowcaseMsg.SetAccountOpen(!m.AccountOpen));
        }
        if (!m.AccountOpen)
            return;
        using var menu = ui.Popover(anchor, () => send(new ShowcaseMsg.SetAccountOpen(false)),
            new(Side.Top, MatchAnchorWidth: true, Name: "Account menu"), key: "account-menu");
        using (c.Div(key: "identity").Row().FillWidth().ItemsCenter().Gap(10).Padding(8))
        {
            ui.Avatar(name, new(ControlSize.Small), key: "avatar");
            using (c.Div(key: "who").Flex().Column().Gap(2))
            {
                ui.Small(name, key: "name");
                c.Text("ada@example.com", new(Size: t.FontSize - 2, Color: Color.Mix(t.MutedForeground, t.PopoverForeground, .25f)),
                    key: "email");
            }
        }
        ui.MenuSeparator(key: "rule1");
        (string Label, string Icon, string Shortcut)[] items =
        [
            ("Profile", "person.fill", Kit.ShortcutLabel("P", Modifiers.Primary | Modifiers.Shift)),
            ("Billing", "doc.text", Kit.ShortcutLabel("B")),
            ("Settings", "slider.horizontal.3", Kit.ShortcutLabel(",")),
            ("Keyboard shortcuts", "command", "")
        ];
        foreach (var (label, icon, shortcut) in items)
        {
            if (ui.MenuItem(label, new(icon, shortcut.Length > 0 ? shortcut : null), key: label).Clicked)
                send(new ShowcaseMsg.AccountAction(label));
        }
        ui.MenuSeparator(key: "rule2");
        if (ui.MenuItem("Log out", new("arrow.right.square", Destructive: true), key: "logout").Clicked)
            send(new ShowcaseMsg.AccountAction("Log out"));
    }
}
