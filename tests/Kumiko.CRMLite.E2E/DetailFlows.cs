using CRMLite;
using Kumiko.UI;

/// <summary>The company page: navigation, timeline, notes and the details panel.</summary>
static class DetailFlows
{
    public static void Run(CrmSession s, Action<bool, string> check)
    {
        s.Click(Role.Row, "Northwind Labs");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && s.Model.CompanyId == "northwind", "clicking a row opens the company");
        check(s.Tester.HasText("1 of 23") && s.Tester.HasText("northwind.dev"), "the header shows the position in the table");
        check(s.Node(Role.Option, "Activity").Checked && s.Tester.HasText("8"), "the timeline counts its activity");
        check(s.Tester.HasText("Today") && s.Tester.HasText("Yesterday") && s.Tester.HasText("Thursday, Oct 15"),
            "activity is grouped under day headings");
        check(!s.Node(Role.Button, "Previous company").Enabled, "the first company cannot step back");
        s.Click(Role.Button, "Next company");
        check(s.Model.CompanyId == "kestrel" && s.Tester.HasText("2 of 23"), "next follows the table order");
        s.Click(Role.Button, "Previous company");

        var before = s.Model.Activities.Length;
        s.Type(Role.TextField, "Add a note", "Ruth confirmed the PO number");
        s.Key(Key.Enter);
        check(s.Model.Activities.Length == before + 1 && s.Model.Note.Length == 0, "Enter adds the note and clears the field");
        check(s.Tester.HasText("Ruth confirmed the PO number") && s.Tester.HasText("Just now"), "the new note leads today's timeline");
        s.Key(Key.Enter);
        check(s.Model.Activities.Length == before + 1, "an empty note is not added");
        s.Click(Role.Option, "Notes");
        check(s.Tester.HasText("Ruth confirmed the PO number") && !s.Tester.HasText("Security deep dive"), "Notes shows notes only");
        s.Click(Role.Option, "People");
        check(s.Tester.HasText("Marco Silva") && s.Tester.HasText("Head of finance"), "People lists the company's contacts");
        s.Click(Role.Option, "Deals");
        check(s.Tester.HasText("Enterprise billing migration"), "Deals lists the company's deals");

        s.Click(Role.Button, "Change stage");
        s.Click(Role.MenuItem, "Negotiation");
        check(Crm.StageOf(s.Model, "northwind") == Stage.Negotiation, "the details panel moves the deal's stage");
        s.Click(Role.Option, "Activity");
        check(s.Model.Activities.Any(a => a is { CompanyId: "northwind", Kind: ActivityKind.StageChange, To: Stage.Negotiation }),
            "a stage change is recorded on the timeline");
        s.Click(Role.Button, "Change owner");
        s.Click(Role.MenuItem, "Leo Marchetti");
        check(s.Model.Companies[0].OwnerId == "leo" && s.Model.Deals.Single(d => d.Id == "d-northwind").OwnerId == "leo",
            "reassigning the company also reassigns its deals");

        s.Click(Role.Switch, "Hot account");
        check(!s.Model.Companies[0].Hot && Crm.InList(s.Model, CompanyList.Hot).Count() == 5, "the hot switch updates hot accounts");
        s.Click(Role.Button, "Remove from favorites");
        check(!s.Exists(Role.Button, "Northwind Labs") && s.Exists(Role.Button, "Add to favorites"), "the star removes a favorite");
        s.Click(Role.Button, "Add to favorites");
        check(s.Exists(Role.Button, "Northwind Labs"), "the star adds it back to the sidebar");
        s.Click(Role.Button, "More actions");
        s.Click(Role.MenuItem, "Mark as hot account");
        check(s.Model.Companies[0].Hot, "the more menu offers the same hot toggle");

        s.Click(Role.Button, "Back to companies");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Node(Role.Row, "Northwind Labs") is not null, "the breadcrumb returns to the table");
        check(s.Model.Companies[0].LastInteraction == s.Model.Now && s.Rows[0] == "Northwind Labs", "a note counts as the latest interaction");
    }
}
