using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    sealed record Page(string Id, string Label, string Icon, string Summary);

    static readonly ImmutableArray<Page> Pages =
    [
        new("overview", "Overview", "square.grid.2x2", "Buttons, badges, alerts and display primitives."),
        new("forms", "Forms", "checklist", "Fields, checkboxes, switches, radios, sliders and toggle groups."),
        new("navigation", "Navigation", "list.bullet", "Tabs, accordions, breadcrumbs, pagination and ratings."),
        new("overlays", "Overlays", "rectangle.on.rectangle", "Modal dialogs and notification cards."),
        new("data", "Data", "calendar", "Date and time pickers, number input, tree, tooltip and resizable panes."),
        new("theme", "Theme", "slider.horizontal.3", "Restyle every component from one KitTheme.")
    ];

    public static void View(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        ui.Apply(ThemeFor(m));
        var t = ui.Theme;
        var page = Pages.FirstOrDefault(p => p.Id == m.Section) ?? Pages[0];
        using (c.Div(key: "root").Row().SizeFull().Background(t.Background))
        {
            Sidebar(c, m, send);
            using var main = c.Div(key: "main").Flex().FillHeight().Column();
            Header(c, m, send, page);
            using var content = c.Div(key: "content").FillWidth().Grow().Scroll().Padding(32).Gap(24);
            using (c.Div(key: "intro").Column().Gap(6))
            {
                ui.H2(page.Label, key: "title");
                ui.Lead(page.Summary, key: "summary");
            }
            switch (page.Id)
            {
                case "forms":
                    Forms(c, m, send);
                    break;
                case "navigation":
                    Navigation(c, m, send);
                    break;
                case "overlays":
                    Overlays(c, m, send);
                    break;
                case "data":
                    Data(c, m, send);
                    break;
                case "theme":
                    ThemePage(c, m, send);
                    break;
                default:
                    Overview(c, m, send);
                    break;
            }
        }
        if (m.DialogOpen)
            DeleteDialog(c, send);
    }

    static void Sidebar(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        var t = ui.Theme;
        using var sidebar = ui.Sidebar(key: "sidebar");
        using (c.Div(key: "brand").Row().FillWidth().ItemsCenter().Gap(10).Padding(new Edges(4, 8, 12, 8)))
        {
            using (c.Div(key: "logo").Width(28).Height(28).Center().Rounded(t.Radius).Background(t.Primary.Background))
                c.Icon("sparkles", 16, t.Primary.Foreground, key: "glyph");
            using (c.Div(key: "name").Flex().Column())
            {
                c.Text("Kumiko", new(Size: t.FontSize, Weight: Weight.Semibold, Color: t.SidebarForeground), key: "title");
                c.Text("Components", new(Size: t.FontSize - 2, Color: t.MutedForeground), key: "subtitle");
            }
            ui.Badge("v0.1", new(Tone.Default, Size: ControlSize.Small), key: "version");
        }
        ui.SidebarLabel("Components", key: "components");
        foreach (var page in Pages)
        {
            if (page.Id == "theme")
                ui.SidebarLabel("Customize", key: "customize");
            var badge = page.Id == "forms" ? "7" : null;
            if (ui.SidebarItem(page.Label, new(page.Icon, m.Section == page.Id, badge), key: page.Id).Clicked)
                send(new ShowcaseMsg.Navigate(page.Id));
        }
        c.Spacer(key: "space");
        using (c.Div(key: "appearance").FillWidth().Padding(new Edges(8, 8, 12, 8)))
            ui.Switch("Dark mode", m.Dark, on => send(new ShowcaseMsg.SetDark(on)), new(Size: ControlSize.Small), key: "dark");
        ui.Separator(key: "rule");
        AccountMenu(c, m, send);
    }

    static void Header(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send, Page page)
    {
        var ui = c.Kit;
        using (c.Div(key: "header").Row().FillWidth().Height(52).ItemsCenter().Gap(12).Px(24))
        {
            ui.Breadcrumb([new("home", "Components"), new(page.Id, page.Label)], key: "crumbs");
            c.Spacer(key: "space");
            SearchBox(c, m, send);
        }
        ui.Separator(key: "header-rule");
    }

    /// <summary>Two equal columns of cards; each builder receives the column it fills.</summary>
    static void Columns(Context c, NodeKey key, Action left, Action right)
    {
        using var row = c.Div(key: key).Row().FillWidth().Items(Align.Start).Gap(24);
        using (c.Div(key: "left").Flex().Column().Gap(24))
            left();
        using (c.Div(key: "right").Flex().Column().Gap(24))
            right();
    }

    static void DeleteDialog(Context c, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var dialog = ui.Dialog("Delete project?", () => send(new ShowcaseMsg.CloseDialog()),
            new("This permanently deletes Acme Storefront, its deployments and environment variables. This cannot be undone."),
            key: "delete-dialog");
        ui.Alert("3 deployments are live", "Visitors will see a 404 page after deletion.", new(Tone.Warning), key: "impact");
        using var footer = ui.Footer(key: "footer");
        if (ui.Button("Cancel", new(Appearance: Appearance.Outline), key: "cancel").Clicked)
            send(new ShowcaseMsg.CloseDialog());
        if (ui.Button("Delete", new(Tone.Danger), key: "delete").Clicked)
            send(new ShowcaseMsg.ConfirmDelete());
    }
}
