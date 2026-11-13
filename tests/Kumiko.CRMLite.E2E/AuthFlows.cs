using CRMLite;
using Kumiko.UI;

/// <summary>Authentication and API behavior through the real offscreen application.</summary>
static class AuthFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var backend = new FakeCrmBackend(TimeSpan.FromMilliseconds(40));
        using var s = new CrmSession(model: CrmApp.Init, services: backend.Services());
        check(s.Model.Companies.IsEmpty && !s.Exists(Role.Button, "Companies"), "signed-out views contain no CRM data");
        Capture(s, output, "auth-sign-in");
        SetEmail(s, "unknown@harbor.app");
        s.Click(Role.Button, "Sign in");
        s.Idle();
        check(s.Model.Auth.Session is null && s.Tester.HasText("Choose a demo account listed below."), "unknown accounts fail visibly without opening the workspace");
        SetEmail(s, "daniel@harbor.app");
        s.Click(Role.Button, "Sign in");
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.SignIn()));
        s.Tester.Settle();
        s.Idle();
        check(s.Model.Auth.Session?.User.Id == "daniel" && s.Model.Companies.Length == 23, "injected auth and API services sign in and load the workspace");
        check(s.Program.History.Count(h => h.Message is CrmMsg.Authentication { Message: AuthMsg.SignedIn }) == 1,
            "duplicate submissions while signing in produce only one completed sign-in");
        s.Click(Role.Button, "My companies");
        check(s.Rows.Length > 0 && Crm.Visible(s.Model).All(c => c.OwnerId == "daniel"), "My companies follows the shared session");
        s.Click(Role.Button, "New company");
        check(s.Model.CompanyDraft?.OwnerId == "daniel", "new records default to the signed-in owner");
        s.Type(Role.TextField, "Company name", "Auth demo customer");
        s.Type(Role.TextField, "Domain", "auth-demo.example");
        s.Click(Role.Button, "Create company");
        s.Idle();
        var companyId = s.Model.CompanyId!;
        s.Type(Role.TextField, "Add a note", "Shared session note");
        s.Key(Key.Enter);
        s.Idle();
        check(s.Model.Activities.Any(a => a.CompanyId == companyId && a.Actor == "Daniel Okafor" && a.Body == "Shared session note"),
            "activity authors use the current authenticated identity");
        s.Click(Role.Button, "Account");
        check(s.Tester.HasText("daniel@harbor.app"), "settings reuse the sidebar's authenticated identity");
        Capture(s, output, "auth-shared-account");
        s.Click(Role.Button, "Sign out");
        s.Idle();
        check(s.Model.Auth.Session is null && s.Model.Companies.IsEmpty && s.Model.Plans.IsEmpty && !s.Model.Settings.Open,
            "sign-out clears private data, overlays and running plans");
        SetEmail(s, "nina@harbor.app");
        s.Click(Role.Button, "Sign in");
        s.Idle();
        check(s.Model.Companies.Any(c => c.Id == companyId), "backend changes survive signing in with another account");
        check(!s.Model.Auth.Session!.CanEdit && s.Tester.HasText("Read-only access"), "the viewer's permissions are visible");
        s.Click(Role.Button, "Inbox");
        var thread = s.Model.Inbox[0];
        s.Click(Role.Button, $"{thread.From}: {thread.Subject}");
        s.Idle();
        check(s.Model.Auth.Session?.User.Id == "nina" && !s.Model.Backend.Dirty && !s.Exists(Role.TextField, "Add a note"),
            "a viewer opens inbox records without triggering a write or exposing the note composer");
        s.Click(Role.Button, "Companies");
        var count = s.Model.Companies.Length;
        s.Program.Send(new CrmMsg.OpenCompanyDialog());
        s.Program.Send(new CrmMsg.SetHot("northwind", false));
        s.Tester.Settle();
        check(s.Model.CompanyDraft is null && s.Model.Companies.Length == count && s.Model.Companies.First(c => c.Id == "northwind").Hot,
            "read-only authorization also guards direct mutation messages");
        Capture(s, output, "auth-viewer");
        s.Program.Save(Path.Combine(output, "auth-session.jsonl"));
        using var replay = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend().Services());
        replay.Program.Replay(Path.Combine(output, "auth-session.jsonl"));
        replay.Tester.Settle();
        check(replay.Model.Auth.Session?.User.Id == "nina" && replay.Model.Companies.Any(c => c.Id == companyId) && replay.Program.PendingCommands == 0,
            "the authenticated flow replays from immutable messages without service work");
        Capture(replay, output, "auth-replay");
        using var narrow = new CrmSession(960, 640, CrmApp.SignedOut(true));
        Capture(narrow, output, "auth-sign-in-dark-narrow");
    }

    internal static void SetEmail(CrmSession s, string email)
    {
        s.Click(Role.TextField, "Work email");
        s.Key(Key.A, Modifiers.Primary);
        s.Tester.TypeText(email);
        s.Tester.Settle();
    }

    internal static void Capture(CrmSession s, string output, string name)
    {
        s.Tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), s.Tester.Dump());
        s.Tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        if (s.Tester.Context.Warnings.Count > 0)
            throw new InvalidOperationException(name + ": " + string.Join("; ", s.Tester.Context.Warnings.Select(w => w.Message)));
    }
}
