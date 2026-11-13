using CRMLite;
using Kumiko.UI;

/// <summary>Real recordings frozen before the framework extraction, not regenerated expectations.</summary>
static class NavigationCompatibilityFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        using var s = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend().Services());
        s.Program.Replay(Path.Combine(fixtures, "before-navigation-library.jsonl"));
        s.Tester.Settle();
        check(s.Model.CompanyId == "northwind" && s.Model.Navigation.Entries.Length == 2 && s.Program.PendingCommands == 0,
            "a pre-library auth and navigation recording replays through the generic path without effects");
        AuthFlows.Capture(s, output, "navigation-compatible-replay");
        check(s.Program.RestoreModel(Path.Combine(fixtures, "before-navigation-library.json")),
            "a pre-library model and pending confirmation restore without a JSON migration");
        s.Tester.Settle();
        check(s.Model.PendingNavigation is not null && s.Model.Note == "draft",
            "legacy confirmation fields and per-visit state retain their original JSON meaning");
        s.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Model.PendingNavigation is null,
            "a restored pre-library pending confirmation commits through the public path machinery");
        AuthFlows.Capture(s, output, "navigation-compatible-confirm");
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Intent draft");
        s.Click(Role.Button, "Hot accounts");
        var intent = Path.Combine(output, "navigation-list-intent.json");
        s.Program.SaveModel(intent);
        check(s.Program.RestoreModel(intent), "a concrete app intent restores over its generic pending base");
        s.Tester.Settle();
        check(s.Model.PendingNavigation?.List == CompanyList.Hot && s.Model.Note == "Intent draft",
            "generic pending JSON preserves a non-null list intent and unsaved state");
        s.Click(Role.Button, "Discard and leave");
        check(s.Model.List == CompanyList.Hot && ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies,
            "a deserialized app intent applies only with its confirmed move");
        AuthFlows.Capture(s, output, "navigation-compatible-list");
    }
}
