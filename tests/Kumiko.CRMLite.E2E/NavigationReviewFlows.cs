using System.Text.Json.Nodes;
using CRMLite;
using Kumiko.UI;

/// <summary>Desktop Back through real controls and keys, recorded as review failure scenarios.</summary>
static class NavigationReviewFlows
{
    static (Key Key, Modifiers Modifiers) BackKey => OperatingSystem.IsMacOS() ? (Key.BracketLeft, Modifiers.Meta) : (Key.Left, Modifiers.Alt);

    static void PressBack(CrmSession s)
    {
        s.Key(BackKey.Key, BackKey.Modifiers);
    }

    internal static void Run(string output, Action<bool, string> check)
    {
        Controls(check);
        Input(output, check);
        Confirmation(check);
        Lateral(check);
        Restore(output, check);
    }

    static void Controls(Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        s.Click(Role.Button, "New deal");
        s.Type(Role.TextField, "Deal name", "Big renewal");
        PressBack(s);
        check(s.Model.DealDraft?.Name == "Big renewal" && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company, "Back shortcut leaves an open dialog and its draft intact");
        s.Key(Key.Escape);
        s.Program.Send(new CrmMsg.OpenCompany("kestrel"));
        s.Tester.Settle();
        s.Click(Role.Button, "Back");
        check(s.Model.CompanyId == "northwind" && s.Model.Navigation.Entries.Length == 2, "deeper-page Back button pops one visit");
        PressBack(s);
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Model.Navigation.Entries.Length == 1, "Back shortcut pops a clean visit");
        s.Program.Send(new CrmMsg.Navigate(ScreenKind.People));
        s.Tester.Settle();
        var person = s.All(Role.Row).OrderBy(r => r.Rect.Y).First();
        s.Click(Role.Row, person.Name!);
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company, "a person opens their company");
        s.Click(Role.Button, "Back to companies");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Model.Navigation.Entries.Length == 1, "Back to companies opens Companies from another section");
    }

    static void Input(string output, Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        s.Click(Role.TextField, "Add a note");
        s.Tester.Context.Input.Key = Key.BracketLeft;
        s.Tester.TypeText("[");
        s.Tester.Settle();
        check(s.Model.Note == "[" && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company, "bracket text reaches the focused note");
        s.Program.Send(new CrmMsg.SetNote(s.Model.Navigation.Top.Id, ""));
        s.Tester.Settle();
        s.Tester.Compose("にほんご", false);
        PressBack(s);
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && s.Model.PendingNavigation is null, "Back waits while an input method composes");
        s.Tester.Compose("日本語", true);
        s.Tester.Settle();
        PressBack(s);
        check(s.Model.Note == "日本語" && s.Model.PendingNavigation is not null, "committed composition is guarded by Back");
        s.Tester.Screenshot(Path.Combine(output, "navigation-confirm.png"));
        s.Tester.TypeText("!");
        s.Tester.Settle();
        check(s.Model.Note == "日本語", "the confirmation owns the keyboard");
        s.Key(Key.Escape);
        check(s.Model.PendingNavigation is null && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company, "Escape keeps editing");

        using var same = new CrmSession();
        same.Click(Role.Row, "Northwind Labs");
        same.Click(Role.TextField, "Add a note");
        same.Tester.Context.Input.Text = "x";
        PressBack(same);
        check(same.Model.Note == "x" && same.Model.PendingNavigation is not null, "text typed with Back reaches its visit before the guard");
    }

    static void Confirmation(Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        var visit = s.Model.Navigation.Top.Id;
        s.Program.Send(new CrmMsg.SetNote(visit, "draft"));
        s.Tester.Settle();
        PressBack(s);
        s.Program.Send(new CrmMsg.SetTab(visit, s.Model.Tab));
        s.Tester.Settle();
        check(s.Model.PendingNavigation is not null, "a no-op edit keeps the confirmation valid");
        s.Program.Send(new CrmMsg.SetNote(visit, "draft, continued"));
        s.Tester.Settle();
        check(s.Model.PendingNavigation is null && s.Model.Note == "draft, continued", "editing a draft withdraws its pending discard");
        PressBack(s);
        s.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "the renewed confirmation leaves");
    }

    static void Lateral(Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        for (var i = 0; i < 3; i++)
            s.Click(Role.Button, "Next company");
        check(s.Model.Navigation.Entries.Length == 2, "Next company replaces the visit");
        var current = s.Model.CompanyId!;
        s.Program.Send(new CrmMsg.OpenCompany(current));
        s.Tester.Settle();
        check(s.Model.Navigation.Entries.Length == 2, "reopening the current company keeps its visit");
        s.Program.Send(new CrmMsg.SetNote(s.Model.Navigation.Top.Id, "keep me"));
        s.Tester.Settle();
        s.Click(Role.Button, "Next company");
        check(s.Model.CompanyId == current && s.Model.PendingNavigation is not null, "Next company guards the replaced draft");
        s.Click(Role.Button, "Keep editing");
        s.Program.Send(new CrmMsg.ShowList(CompanyList.Hot));
        s.Tester.Settle();
        check(s.Model.List == CompanyList.All && s.Model.PendingNavigation is not null, "a guarded list change waits");
        s.Click(Role.Button, "Discard and leave");
        check(s.Model.List == CompanyList.Hot && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "the list applies with the confirmed move");
    }

    static void Restore(string output, Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        s.Program.Send(new CrmMsg.SetNote(s.Model.Navigation.Top.Id, "draft"));
        s.Tester.Settle();
        PressBack(s);
        var saved = Path.Combine(output, "navigation-review-model.json");
        s.Program.SaveModel(saved);
        var json = JsonNode.Parse(File.ReadAllText(saved))!;
        var companyRoot = json.DeepClone();
        companyRoot["Navigation"]!["Entries"]![0]!["Route"] = JsonNode.Parse("""{"screen":"company","Id":"northwind"}""");
        var invalid = Path.Combine(output, "navigation-company-root.json");
        File.WriteAllText(invalid, companyRoot.ToJsonString());
        check(!s.Program.RestoreModel(invalid), "a restored company root is rejected");
        var forged = json.DeepClone();
        var desired = forged["PendingNavigation"]!["Desired"]!["Entries"]!.AsArray();
        desired.Add(JsonNode.Parse("""{"Id":0,"Route":{"screen":"company","Id":"kestrel"},"Tab":0,"Note":""}"""));
        desired[0]!["Id"] = 1;
        var malformed = Path.Combine(output, "navigation-forged-pending.json");
        File.WriteAllText(malformed, forged.ToJsonString());
        check(s.Program.RestoreModel(malformed), "a forged pending path can be loaded");
        s.Tester.Settle();
        s.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Company && s.Model.Note == "draft", "a forged pending path cannot apply");
    }
}
