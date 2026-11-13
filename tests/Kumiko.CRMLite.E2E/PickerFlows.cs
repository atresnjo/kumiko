using CRMLite;
using Kumiko.UI;

/// <summary>Searchable pickers: the New deal company search (remote, sectioned) and the multiple-choice industry filter.</summary>
static class PickerFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        CompanySearch(s, output, check);
        IndustryFilter(s, output, check);
    }

    static void CompanySearch(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, "New deal");
        s.Click(Role.ComboBox, "Company");
        check(s.Tester.HasText("Searching…") && s.Model.DealDraft is not null, "opening the company picker starts a directory search");
        s.Idle();
        check(s.Exists(Role.Heading, "Your accounts") && s.Exists(Role.Heading, "Team accounts"),
            "directory results are grouped into your accounts and the team's");
        check(s.Node(Role.Heading, "Your accounts").Rect.Y < s.Node(Role.Option, "Kestrel Freight").Rect.Y
            && s.Node(Role.Option, "Kestrel Freight").Rect.Y < s.Node(Role.Heading, "Team accounts").Rect.Y,
            "the signed-in owner's accounts are listed first");
        s.Tester.TypeText("cloud");
        s.Tester.Settle();
        check(s.Tester.HasText("Searching…"), "typing searches the directory again");
        s.Idle();
        check(Options(s, "Company").SequenceEqual(["Glacier Cloud"]), "the directory answers with matching companies");
        s.Tester.Screenshot(Path.Combine(output, "crm-company-search.png"));
        var search = ((Overlay.NewDeal)s.Model.Overlay).Search!;
        s.Program.Send(new CrmMsg.CompaniesFound(search.Request - 1, ["northwind", "kestrel"]));
        s.Tester.Settle();
        check(Options(s, "Company").SequenceEqual(["Glacier Cloud"]), "a superseded directory answer is ignored");
        s.Key(Key.Escape);
        check(s.Model.DealDraft is not null && s.Model.Popup == Popup.None, "Escape closes the company search before the dialog");
        s.Click(Role.ComboBox, "Company");
        s.Tester.TypeText("zzz");
        s.Tester.Settle();
        s.Idle();
        check(s.Tester.HasText("No companies match"), "a search without matches says so");
        s.Tester.Key(Key.A, Modifiers.Primary);
        var answered = s.Program.History.Count(step => step.Message is CrmMsg.CompaniesFound);
        foreach (var letter in "glacier")
        {
            s.Tester.TypeText(letter.ToString());
            s.Tester.Frame();
        }
        s.Idle();
        check(s.Program.History.Count(step => step.Message is CrmMsg.CompaniesFound) == answered + 1 && Options(s, "Company").SequenceEqual(["Glacier Cloud"]),
            "typing a word asks the directory once, after the typing pauses");
        s.Click(Role.Option, "Glacier Cloud");
        check(s.Model.DealDraft?.CompanyId == "glacier" && s.Model.Popup == Popup.None, "choosing a result fills the deal's company");
        check(s.Node(Role.ComboBox, "Company").Value == "Glacier Cloud", "the picker shows the chosen company");
        s.Key(Key.Escape);
        check(s.Model.DealDraft is null, "Escape then dismisses the dialog");
        s.Click(Role.Button, "Companies");
        s.Click(Role.Row, "Kestrel Freight");
        s.Click(Role.Button, "New deal");
        check(s.Node(Role.ComboBox, "Company").Value == "Kestrel Freight", "a preselected company is labelled before any search");
        s.Key(Key.Escape);
        s.Click(Role.Button, "Back to companies");
    }

    static void IndustryFilter(CrmSession s, string output, Action<bool, string> check)
    {
        s.Click(Role.ComboBox, "Industry");
        check(s.Exists(Role.Heading, "Technology") && s.Exists(Role.Heading, "Financial services")
            && s.Tester.Context.Accessibility.Any(n => n.Role == Role.Heading && n.Name == "Consumer"), "industries are grouped by sector");
        s.Tester.TypeText("retail");
        s.Tester.Settle();
        s.Click(Role.Option, "Retail");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("fintech");
        s.Tester.Settle();
        s.Click(Role.Option, "Fintech");
        check(s.Model.Industries.SequenceEqual(["Retail", "Fintech"]) && s.Model.Popup == Popup.IndustryFilter,
            "the industry filter takes several industries and stays open for the next search");
        check(s.Rows.Order().SequenceEqual(["Atlas Payments", "Juniper Retail", "Summit Outdoor"]), "the table lists companies in any chosen industry");
        s.Tester.Screenshot(Path.Combine(output, "crm-industry-filter.png"));
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("bank");
        s.Tester.Settle();
        check(Options(s, "Industry").SequenceEqual(["Banking"]), "the industry list is searchable");
        s.Key(Key.Enter);
        check(s.Rows.Length == 4 && s.Model.Industries.Contains("Banking"), "Enter adds the highlighted industry");
        s.Key(Key.Escape);
        check(s.Model.Popup == Popup.None && s.Node(Role.ComboBox, "Industry").Value == "Banking, Fintech, Retail",
            "the closed filter summarizes its industries");
        s.Click(Role.ComboBox, "Industry");
        s.Click(Role.Button, "Clear selection");
        s.Key(Key.Escape);
        check(s.Model.Industries.IsEmpty && s.Rows.Length == 23, "clearing the filter shows every company");
    }

    /// <summary>The visible options in the dropdown list named <paramref name="list"/>, top to bottom.</summary>
    static string[] Options(CrmSession s, string list)
    {
        var nodes = s.Tester.Context.Accessibility;
        var root = nodes.Single(n => n.Role == Role.List && n.Name == list).Id;
        var parents = nodes.ToDictionary(n => n.Id, n => n.Parent);
        bool Inside(AccessibleNode node)
        {
            for (var parent = node.Parent; parents.ContainsKey(parent); parent = parents[parent])
            {
                if (parent == root)
                    return true;
            }
            return false;
        }
        return [.. nodes.Where(n => n.Role == Role.Option && !n.Offscreen && Inside(n)).OrderBy(n => n.Rect.Y).Select(n => n.Name)];
    }
}
