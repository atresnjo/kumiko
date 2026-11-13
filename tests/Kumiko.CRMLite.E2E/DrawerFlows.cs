using CRMLite;
using Kumiko.UI;

/// <summary>The deal drawer, deal-card and owner-cell context menus, and the sidebar's list selection.</summary>
static class DrawerFlows
{
    const string Card = "Juniper Retail, Partner billing";

    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        Sidebar(s, check);
        OwnerCell(s, check);
        s.Click(Role.Button, "Deals");
        Drawer(s, output, check);
        CardMenu(s, check);
    }

    static void Sidebar(CrmSession s, Action<bool, string> check)
    {
        s.Click(Role.Button, "My companies");
        check(Selected(s, "My companies") && !Selected(s, "Companies"),
            "a sidebar list is the selected item while its list shows");
        s.Click(Role.Button, "Companies");
        check(s.Model.List == CompanyList.All && Selected(s, "Companies") && !Selected(s, "My companies"),
            "Companies shows every company and takes the selection back");
    }

    static bool Selected(CrmSession s, string item) => s.Node(Role.Button, item).Properties?.Selected == true;

    static void OwnerCell(CrmSession s, Action<bool, string> check)
    {
        var row = s.Node(Role.Row, "Glacier Cloud").Rect;
        var owner = s.Tester.Context.Accessibility.First(n => n.Role == Role.Text && n.Name == "Sara Lindqvist" && n.Rect.Y >= row.Y &&
            n.Rect.Bottom <= row.Bottom).Rect;
        s.Tester.RightClick(owner.X + 4, owner.Y + owner.Height / 2);
        s.Tester.Settle();
        check(s.Exists(Role.MenuItem, "Leo Marchetti") && s.Exists(Role.MenuItem, "Open company"),
            "right-clicking an owner cell offers owners before the row's actions");
        check(s.Node(Role.MenuItem, "Sara Lindqvist").Checked, "the owner menu checks the current owner");
        s.Click(Role.MenuItem, "Leo Marchetti");
        check(Crm.Company(s.Model, "glacier")?.OwnerId == "leo" && s.Model.Overlay is Overlay.None, "choosing an owner reassigns the company");
        var name = s.Tester.Context.Accessibility.First(n => n.Role == Role.Text && n.Name == "Glacier Cloud").Rect;
        s.Tester.RightClick(name.X + 4, name.Y + name.Height / 2);
        s.Tester.Settle();
        check(!s.Exists(Role.MenuItem, "Leo Marchetti") && s.Exists(Role.MenuItem, "Open company"), "other cells offer only the row's actions");
        s.Key(Key.Escape);
    }

    static void Drawer(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, Card);
        var culture = s.Tester.Context.Culture;
        check(s.Model.Overlay is Overlay.DealDrawer { DealId: "d-juniper" } && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Deals, "clicking a deal card opens its drawer");
        var drawer = s.Node(Role.Dialog, "Partner billing");
        check(drawer.Rect.Right > s.Tester.Context.Size.Width - 20, "the deal drawer docks to the right edge");
        check(s.Tester.HasText("Juniper Retail, juniper.shop") && s.Tester.HasText(Format.Money(392_000, culture)) &&
            s.Tester.HasText("Weighted " + Format.Money((long)(392_000 * Crm.Probability(Stage.Lead)), culture)),
            "the drawer shows the company, value and weighted value");
        foreach (var label in new[] { "Stage", "Probability", "Close date", "Owner", "Contact", "Next step", "Recent activity" })
            check(s.Tester.HasText(label), $"the drawer shows {label}");
        check(s.Tester.HasText("Daniel Okafor") && s.Tester.HasText("Zoe Harper"), "the drawer names the owner and contact");
        check(s.Tester.Context.Warnings.Count == 0, "the deal drawer renders without layout, overlap or contrast warnings: " +
            string.Join("; ", s.Tester.Context.Warnings.Select(w => w.Message)));
        s.Tester.Screenshot(Path.Combine(output, "deal-drawer.png"));
        File.WriteAllText(Path.Combine(output, "deal-drawer.txt"), s.Tester.Dump());

        s.Click(Role.ComboBox, "Stage");
        s.Click(Role.MenuItem, "Demo");
        check(s.Model.Deals.Single(d => d.Id == "d-juniper").Stage == Stage.Demo && s.Model.Overlay is Overlay.DealDrawer { StageOpen: false },
            "changing the stage in the drawer keeps the drawer open");
        check(s.Tester.HasText("Weighted " + Format.Money((long)(392_000 * Crm.Probability(Stage.Demo)), culture)),
            "the weighted value follows the new stage");
        s.Key(Key.Escape);
        check(s.Model.Overlay is Overlay.None, "Escape closes the deal drawer");
        s.Click(Role.Button, Card);
        s.Click(Role.Button, "Open Juniper Retail");
        check(s.Model.CompanyId == "juniper" && s.Model.Overlay is Overlay.None, "the drawer opens the deal's company");
        s.Click(Role.Button, "Deals");
    }

    static void CardMenu(CrmSession s, Action<bool, string> check)
    {
        var card = s.Node(Role.Button, Card).Rect;
        s.Tester.RightClick(card.X + 30, card.Y + 20);
        s.Tester.Settle();
        check(s.Tester.Context.Dragging is null && s.Model.Overlay is Overlay.DealMenu { DealId: "d-juniper" }, "right-clicking a card opens its menu, not a drag");
        check(s.Exists(Role.MenuItem, "Open deal") && s.Exists(Role.MenuItem, "Proposal") && s.Node(Role.MenuItem, "Demo").Checked,
            "the card menu offers the deal's actions and stages");
        s.Click(Role.MenuItem, "Proposal");
        check(s.Model.Deals.Single(d => d.Id == "d-juniper").Stage == Stage.Proposal && s.Model.Overlay is Overlay.None,
            "moving a deal from its menu updates the board");
        card = s.Node(Role.Button, Card).Rect;
        s.Tester.RightClick(card.X + 30, card.Y + 20);
        s.Tester.Settle();
        s.Click(Role.MenuItem, "Open deal");
        check(s.Model.Overlay is Overlay.DealDrawer { DealId: "d-juniper" }, "Open deal opens the drawer");
        s.Key(Key.Escape);
    }
}
