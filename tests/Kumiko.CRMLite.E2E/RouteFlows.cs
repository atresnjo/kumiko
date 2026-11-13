using CRMLite;
using Kumiko.UI;

static class RouteFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        // A real company with path punctuation must be reached intact, recorded, and restored.
        const string id = "partner/a % 東京";
        var seed = Data.Initial();
        seed = seed with
        {
            Companies = seed.Companies.Add(seed.Companies[0] with
            {
                Id = id,
                Name = "Path partner"
            })
        };
        var recording = Path.Combine(output, "routes-session.jsonl");
        using var s = new CrmSession(model: seed);
        var route = RouteInput.Parse(ScreenRoutes.Format(new Screen.CompanyDetails(id)))!;
        s.Program.Send(new CrmMsg.Go(route));
        s.Tester.Settle();
        check(s.Model.CompanyId == id && s.Tester.HasText("Path partner"),
            "a generated escaped route dispatches to its typed company page");
        s.Type(Role.TextField, "Add a note", "Path draft");
        s.Program.Send(new CrmMsg.Go(RouteInput.Parse(null, "people")!));
        s.Tester.Settle();
        check(s.Model.PendingNavigation is not null && s.Model.CompanyId == id,
            "parsed routes use the same unsaved-note confirmation");
        s.Click(Role.Button, "Discard and leave");
        check(s.Model.Screen is Screen.People && s.Tester.HasText("People"),
            "confirming a parsed root reaches its typed people page");
        s.Program.Send(new CrmMsg.Go(RouteInput.Parse(null, company: id)!));
        s.Tester.Settle();
        check(s.Model.CompanyId == id, "legacy company intake uses the same encoded route mapping");
        s.Program.Save(recording);
        var saved = Path.Combine(output, "routes-model.json");
        s.Program.SaveModel(saved);
        AuthFlows.Capture(s, output, "routes-live");
        using var replay = new CrmSession(model: seed);
        replay.Program.Replay(recording);
        replay.Tester.Settle();
        check(replay.Model.Navigation.Top.Route == route && replay.Model.Navigation.Revision == s.Model.Navigation.Revision && replay.Program.PendingCommands == 0,
            "generated route tags and typed Go messages replay without effects");
        check(replay.Program.RestoreModel(saved), "a generated-route model restores through AOT metadata");
        replay.Tester.Settle();
        check(replay.Model.CompanyId == id && replay.Tester.HasText("Path partner"),
            "restored encoded identifiers render the same page");
        AuthFlows.Capture(replay, output, "routes-replay");
    }
}
