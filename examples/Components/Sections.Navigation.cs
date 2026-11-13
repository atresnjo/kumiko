using System.Collections.Immutable;
using Kumiko.Components;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Components;

public sealed partial class ComponentsApp
{
    static readonly ImmutableArray<Choice> SettingsTabs = [new("account", "Account"), new("password", "Password"), new("notifications", "Notifications")];
    static readonly ImmutableArray<Choice> Periods = [new("day", "Day"), new("week", "Week"), new("month", "Month"), new("year", "Year")];
    static readonly ImmutableArray<Choice> Filters = [new("all", "All"), new("active", "Active"), new("archived", "Archived")];

    sealed record Question(string Id, string Title, string Body);

    static readonly ImmutableArray<Question> Questions =
    [
        new("a11y", "Is it accessible?", "Yes. Every component exposes roles and names, and works with the keyboard."),
        new("shipping", "Shipping", "Orders ship within two business days. Tracking arrives by email."),
        new("returns", "Returns", "Return unused items within 30 days for a full refund.")
    ];

    static void Navigation(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        Columns(c, "navigation", () => TabsCard(c, m, send), () => Faq(c, m, send));
        Paging(c, m, send);
    }

    static void TabsCard(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "tabs").FillWidth();
        ui.CardHeader("Tabs", "Segmented, underline and pill variants.", key: "header");
        ui.Tabs("Settings", SettingsTabs, m.Tab, tab => send(new ShowcaseMsg.SelectTab(tab)), new(Fill: true), key: "settings");
        using (c.Div(key: "panel").FillWidth().Gap(12).Padding(16).Rounded(ui.Theme.RadiusLarge).Border(ui.Theme.Border))
        {
            var (title, body) = m.Tab switch
            {
                "password" => ("Password", "Change your password here. After saving, you'll be logged out."),
                "notifications" => ("Notifications", "Configure how you receive notifications."),
                _ => ("Account", "Make changes to your account here. Click save when you're done.")
            };
            ui.Large(title, key: "title");
            ui.P(body, key: "body");
            using var footer = ui.Footer(key: "footer");
            if (ui.Button("Save changes", new(Tone.Primary, Size: ControlSize.Small), key: "save").Clicked)
                send(new ShowcaseMsg.Click("Save changes"));
        }
        ui.Label("Reporting period", key: "period-label");
        ui.Tabs("Period", Periods, m.Period, period => send(new ShowcaseMsg.SetPeriod(period)), new(TabsVariant.Underline),
            key: "period");
        ui.Label("Status filter", key: "filter-label");
        ui.Tabs("Status", Filters, m.Filter, filter => send(new ShowcaseMsg.SetFilter(filter)),
            new(TabsVariant.Pill, ControlSize.Small), key: "filter");
    }

    static void Faq(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "faq").FillWidth().Gap(0);
        ui.CardHeader("Accordion", "One item open at a time.", key: "header");
        foreach (var (id, title, body) in Questions)
        {
            using var item = ui.AccordionItem(title, m.Expanded == id, open => send(new ShowcaseMsg.Expand(open ? id : null)), key: id);
            if (item.Open)
                ui.Muted(body, key: "body");
        }
    }

    static void Paging(Context c, ShowcaseModel m, Dispatch<ShowcaseMsg> send)
    {
        var ui = c.Kit;
        using var card = ui.Card(key: "paging").FillWidth();
        ui.CardHeader("Pagination & rating", $"Page {m.Page} of 10 · {m.Rating} of 5 stars", key: "header");
        ui.Pagination(m.Page, 10, page => send(new ShowcaseMsg.SetPage(page)), key: "pages");
        ui.Rating("Rating", m.Rating, stars => send(new ShowcaseMsg.SetRating(stars)), key: "rating");
    }
}
