using CRMLite;
using Kumiko.UI;

/// <summary>The Companies table: counts, sorting, list tabs, search and filters.</summary>
static class CompanyFlows
{
    public static void Run(CrmSession s, Action<bool, string> check)
    {
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Rows.Length == 23, "CRMLite opens on all 23 companies");
        check(s.Tester.HasText("23 companies") && s.Tester.HasText("$3.52M"), "the footer counts companies and sums their ARR");
        check(s.Rows[0] == "Northwind Labs" && s.Rows[^1] == "Tidewater Labs", "companies start with the most recent interaction first");
        check(s.Tester.HasText("3h ago") && s.Tester.HasText("Yesterday") && s.Tester.HasText("Sep 21"),
            "last interactions read as relative times, then dates");
        check(s.Node(Role.Button, "Companies").Properties?.Selected == true, "the sidebar marks the current page");

        s.Click(Role.ColumnHeader, "ARR");
        check(s.Rows[0] == "Juniper Retail" && s.Rows[1] == "Kestrel Freight", "ARR sorts largest first on its first click");
        s.Click(Role.ColumnHeader, "ARR");
        check(s.Rows[0] == "Riverstone Legal", "a second click reverses the sort");
        s.Click(Role.ColumnHeader, "Company");
        check(s.Rows[0] == "Atlas Payments" && s.Rows[^1] == "Vantage Energy", "text columns sort A to Z first");

        s.Click(Role.Option, "My companies");
        check(s.Rows.Length == 5 && s.Tester.HasText("5 companies"), "My companies lists the signed-in owner's accounts");
        s.Click(Role.Option, "Hot");
        check(s.Rows.Length == 6 && s.Rows.All(r => s.Model.Companies.Single(c => c.Name == r).Hot), "Hot lists hot accounts only");
        s.Click(Role.Button, "Hot accounts");
        check(s.Model.List == CompanyList.Hot && s.Node(Role.Option, "Hot").Checked, "sidebar lists select the matching tab");
        s.Click(Role.Option, "All companies");

        s.Type(Role.TextField, "Search companies", "bank");
        check(s.Rows is ["Pinecrest Bank"], "search matches names, domains and industries");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.Key(Key.Backspace);
        s.Tester.Settle();
        check(s.Rows.Length == 23, "clearing the search shows every company again");

        s.Click(Role.Button, "Stage filter");
        check(s.Exists(Role.MenuItem, "Demo"), "the stage filter lists every stage");
        s.Click(Role.MenuItem, "Demo");
        check(s.Rows.Length == 4 && s.Model.StageFilter == Stage.Demo && s.Model.Popup == Popup.None, "choosing a stage filters and closes the menu");
        s.Click(Role.Button, "Owner filter");
        s.Click(Role.MenuItem, "Maya Brooks");
        check(s.Rows is ["Orbital Analytics"], "stage and owner filters combine");
        s.Click(Role.Button, "Stage filter");
        s.Click(Role.MenuItem, "All stages");
        s.Click(Role.Button, "Owner filter");
        s.Key(Key.Escape);
        check(s.Model.Popup == Popup.None && s.Model.OwnerFilter == "maya", "Escape closes a filter menu without changing it");
        s.Click(Role.Button, "Owner filter");
        s.Click(Role.MenuItem, "All owners");
        check(s.Rows.Length == 23, "filters clear back to every company");

        s.Key(Key.F, Modifiers.Primary);
        check(s.Model.Popup == Popup.Search, "Command-F opens search");
        s.Tester.TypeText("tide");
        s.Tester.Settle();
        s.Key(Key.Enter);
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && s.Model.CompanyId == "tidewater", "search jumps to the first matching company");
        s.Key(Key.F, Modifiers.Primary);
        var palette = s.Node(Role.Group, "Search").Rect;
        var window = s.Tester.Context.Size;
        check(Math.Abs(palette.X + palette.Width / 2 - window.Width / 2) < 1 && palette.Y < window.Height / 4,
            "search opens as a palette centered near the top of the window");
        check(s.Node(Role.TextField, "Search companies, people and pages").Focused, "the palette focuses its query field");
        check(s.Exists(Role.MenuItem, "Deals") && s.Exists(Role.MenuItem, "Northwind Labs") &&
            s.Tester.Context.Accessibility.Any(n => n is { Role: Role.MenuItem, Name: "Hana Kobayashi" }),
            "an empty query lists pages, companies and people, scrolling past the panel");
        s.Tester.TypeText("finance");
        s.Tester.Settle();
        check(s.Exists(Role.MenuItem, "Ruth Adeyemi") && !s.Exists(Role.MenuItem, "Deals"), "results match titles and descriptions");
        s.Key(Key.Down);
        s.Key(Key.Down);
        s.Key(Key.Enter);
        var second = s.Model.CompanyId;
        check(s.Model.Popup == Popup.None && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && second != "tidewater", "arrow keys and Enter open a result");
        s.Key(Key.F, Modifiers.Primary);
        s.Tester.TypeText("zzz");
        s.Tester.Settle();
        check(s.Tester.ContainsText("No results for"), "an unmatched query explains itself");
        s.Key(Key.Escape);
        check(s.Model.Popup == Popup.None && s.Model.CompanyId == second, "Escape closes the palette without navigating");
        s.Click(Role.Button, "Inbox");
        check(s.Tester.HasText("Order form questions"), "the inbox lists unread threads");
        s.Click(Role.Button, "Jonas Vermeer: Order form questions");
        check(s.Model.CompanyId == "kestrel" && Crm.UnreadCount(s.Model) == 3, "opening a thread marks it read and opens its company");
        s.Click(Role.Button, "People");
        check(s.Tester.Context.Accessibility.Count(n => n.Role == Role.Row) == 28, "People lists every contact, scrolling past the window");
    }
}
