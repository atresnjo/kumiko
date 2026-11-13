using CRMLite;
using Kumiko.UI;

/// <summary>New deal and New company forms: validation, keyboard submit and dismissal.</summary>
static class DialogFlows
{
    public static void Run(CrmSession s, Action<bool, string> check)
    {
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, "New deal");
        check(s.Model.DealDraft is { Stage: Stage.Qualified, OwnerId: "maya" }, "New deal defaults to Qualified and the signed-in owner");
        check(s.Tester.HasText("Maya Brooks (you)"), "the owner field marks the signed-in owner");
        s.Click(Role.Button, "Create deal");
        check(s.Model.DealDraft is { Submitted: true } && s.Tester.HasText("Choose a company.") && s.Tester.HasText("Name the deal."),
            "submitting an empty form explains every missing field");
        var count = s.Model.Deals.Length;
        s.Click(Role.ComboBox, "Company");
        s.Tester.TypeText("glacier");
        s.Tester.Settle();
        s.Idle();
        s.Click(Role.Option, "Glacier Cloud");
        check(s.Model.DealDraft?.CompanyId == "glacier" && s.Model.DealDraft is not null, "choosing a company keeps the dialog open");
        s.Type(Role.TextField, "Deal name", "Cold archive expansion");
        s.Type(Role.TextField, "Value", "$120k");
        s.Click(Role.ComboBox, "Stage");
        s.Key(Key.Escape);
        check(s.Model.DealDraft is not null && s.Model.Popup == Popup.None, "Escape closes an open select before the dialog");
        s.Click(Role.ComboBox, "Stage");
        s.Click(Role.MenuItem, "Demo");
        s.Key(Key.Enter, Modifiers.Primary);
        var deal = s.Model.Deals[^1];
        check(s.Model.Deals.Length == count + 1 && s.Model.DealDraft is null, "Command-Enter creates the deal and closes the dialog");
        check(deal is { CompanyId: "glacier", Value: 120_000, Stage: Stage.Demo, OwnerId: "maya" }, "the deal keeps every field");
        check(s.Exists(Role.Button, "Glacier Cloud, Cold archive expansion") && Crm.StageOf(s.Model, "glacier") == Stage.Demo,
            "the new deal lands on the board in its stage");

        s.Click(Role.Button, "Companies");
        s.Click(Role.Button, "New company");
        s.Type(Role.TextField, "Company name", "Lighthouse Maps");
        s.Type(Role.TextField, "Domain", "not a domain");
        s.Click(Role.Button, "Create company");
        check(s.Tester.HasText("Enter a domain such as acme.com.") && s.Model.Companies.Length == 23, "an invalid domain blocks creation");
        s.Click(Role.TextField, "Domain");
        s.Tester.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText("lighthouse.dev");
        s.Tester.Settle();
        s.Click(Role.Button, "Create company");
        check(s.Model.Companies.Length == 24 && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && Crm.Company(s.Model, s.Model.CompanyId)?.Name == "Lighthouse Maps",
            "a valid company is created and opened");
        s.Click(Role.Button, "New deal");
        check(s.Model.DealDraft?.CompanyId == s.Model.CompanyId, "New deal on a company page preselects that company");
        s.Key(Key.Escape);
        check(s.Model.DealDraft is null, "Escape dismisses the dialog");
        s.Click(Role.Button, "Back to companies");
        check(s.Tester.HasText("24 companies"), "the table includes the new company");
    }
}
