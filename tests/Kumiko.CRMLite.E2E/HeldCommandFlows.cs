using CRMLite;
using Kumiko.UI;

/// <summary>
/// Signing in and searching with every command held: the test answers each request itself, so the in-between states
/// (busy, loading, failed, searching) are checked through the real view without fake latency or gated services.
/// </summary>
static class HeldCommandFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var backend = new FakeCrmBackend(TimeSpan.Zero);
        using var s = new CrmSession(model: CrmApp.Init, services: backend.Services());
        s.Program.HoldCommands = true;
        var held = s.Program.Held;

        // When signing in, the form stays busy until the sign-in request answers.
        s.Tester.Get(Role.TextField, "Work email").Fill("daniel@harbor.app");
        check(s.Tester.Get(Role.TextField, "Work email").Value == "daniel@harbor.app", "the email field shows what was typed");
        s.Click(Role.Button, "Sign in");
        held.Expect("sign-in");
        check(!s.Tester.Get(Role.Button, "Signing in…").Enabled && !s.Tester.Get(Role.TextField, "Work email").Enabled,
            "while sign-in is held the form is busy and locked");
        AuthFlows.Capture(s, output, "held-signing-in");
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.SignIn()));
        s.Tester.Settle();
        held.Expect("sign-in");
        var attempt = held.Get("sign-in");
        attempt.Fail(new InvalidOperationException("Harbor is offline."));
        s.Tester.Settle();
        held.ExpectNone();
        check(s.Tester.HasText("Harbor is offline.") && s.Tester.Get(Role.Button, "Sign in").Enabled,
            "a failed sign-in shows the service's error and unlocks the form");
        check(Rejects(() => attempt.Resolve(new CrmMsg.Authentication(new AuthMsg.SignedIn(1, null!)))),
            "a settled sign-in cannot answer again");

        // A released command runs against the real fake backend; the workspace load it starts is held in turn.
        s.Click(Role.Button, "Sign in");
        held.Get("sign-in").Run();
        s.Idle();
        held.Expect("load-workspace");
        check(s.Model.Auth.Session?.User.Id == "daniel" && s.Tester.HasText("Loading your CRM…")
            && s.Tester.Get(Role.Group, "Workspace status").HasText("Loading workspace…"),
            "after sign-in answers, the workspace shows its loading state while the load is held");
        AuthFlows.Capture(s, output, "held-loading");
        held.Get("load-workspace").Fail(new HttpRequestException("Demo connection failed."));
        s.Tester.Settle();
        held.ExpectNone();
        check(s.Tester.HasText("Your workspace could not be loaded. Try again.")
            && s.Tester.Get(Role.Group, "Workspace status").HasText("Demo connection failed."),
            "a failed load explains itself in the page and the workspace status");
        AuthFlows.Capture(s, output, "held-load-failed");

        // Retrying answers with a fixture instead of the backend's data.
        s.Click(Role.Button, "Retry loading");
        var fixture = WorkspaceData.From(Data.Initial()) with
        {
            Companies = [.. Data.Initial().Companies.Take(3)]
        };
        held.Get("load-workspace").Resolve(new CrmMsg.Authentication(new AuthMsg.WorkspaceLoaded(s.Model.Backend.Request, fixture)));
        s.Tester.Settle();
        held.ExpectNone();
        var first = s.Tester.Nth(Role.Row, 0);
        var company = fixture.Companies.Single(c => c.Name == first.Name);
        check(s.Rows.Length == 3 && first.HasText(company.NextStep) && !s.Tester.Nth(Role.Row, 1).HasText(company.NextStep),
            "the fixture's companies fill the table, and the first row shows its own next step");
        check(s.Tester.Get(Role.Group, "Workspace status").HasText("All changes saved"), "a loaded workspace reports no pending changes");

        // Searching for a company: only the latest search can answer.
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, "New deal");
        s.Click(Role.ComboBox, "Company");
        held.Expect("search-companies");
        var opened = held.Get("search-companies");
        check(s.Tester.HasText("Searching…"), "opening the company picker shows a held directory search");
        s.Tester.TypeText("kes");
        s.Tester.Settle();
        held.Expect("search-companies");
        check(Rejects(() => opened.Resolve(new CrmMsg.CompaniesFound(1, ["northwind"]))), "typing replaces the held search that opening started");
        var request = ((Overlay.NewDeal)s.Model.Overlay).Search!.Request;
        held.Get("search-companies").Resolve(new CrmMsg.CompaniesFound(request, ["kestrel"]));
        s.Tester.Settle();
        held.ExpectNone();
        var list = s.Tester.Get(Role.List, "Company");
        check(list.Get(Role.Option, "Kestrel Freight").Enabled && !s.Tester.HasText("Searching…"), "the latest search's answer fills the picker");
        AuthFlows.Capture(s, output, "held-company-search");
        s.Click(Role.Option, "Kestrel Freight");
        check(s.Tester.Get(Role.ComboBox, "Company").Value == "Kestrel Freight", "choosing the answer fills the deal's company");
        s.Key(Key.Escape);

        // Refreshing lets the real backend answer.
        s.Click(Role.Button, "Refresh data");
        held.Get("load-workspace").Run();
        s.Idle();
        held.ExpectNone();
        check(s.Model.Companies.Length == 23 && s.Program.PendingCommands == 0, "a released refresh loads the backend's full workspace");
    }

    static bool Rejects(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
