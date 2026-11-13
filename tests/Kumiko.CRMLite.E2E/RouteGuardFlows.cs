using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CRMLite;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Route intake, auth and stale confirmations through the actual offscreen application.</summary>
static class RouteGuardFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        ReturnAfterLoad(output, check);
        MissingAndReplaced(output, check);
        AuthBoundaries(output, check);
        Loading(output, check);
        Confirmations(output, check);
    }

    static void Send(CrmSession s, CrmMsg msg)
    {
        s.Program.Send(msg);
        s.Tester.Settle();
    }

    static void Auth(CrmSession s, AuthMsg msg) => Send(s, new CrmMsg.Authentication(msg));

    static void SignIn(CrmSession s)
    {
        s.Click(Role.Button, "Sign in");
        s.Idle();
    }

    static void ReturnAfterLoad(string output, Action<bool, string> check)
    {
        using var s = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(s, new CrmMsg.OpenCompany("northwind"));
        var pending = Path.Combine(output, "guard-pending-model.json");
        s.Program.SaveModel(pending);
        AuthFlows.Capture(s, output, "guard-sign-in");
        SignIn(s);
        AuthFlows.Capture(s, output, "guard-return");
        s.Program.Save(Path.Combine(output, "guard-session.jsonl"));
        check(s.Model.CompanyId == "northwind" && s.Tester.HasText("Northwind Labs"),
            "a signed-out company request opens its details after sign-in and workspace loading");
        check(s.Model.Navigation.Entries.Length == 2 && s.Model.PendingNavigation is null,
            "deferred company intake creates one ordinary visit above Companies");
        s.Click(Role.Button, "Back to companies");
        s.Click(Role.Button, "Refresh data");
        s.Idle();
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "a consumed return route does not replay on refresh");

        using var replay = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend().Services());
        replay.Program.Replay(Path.Combine(output, "guard-session.jsonl"));
        replay.Tester.Settle();
        check(replay.Model.CompanyId == "northwind" && replay.Program.PendingCommands == 0,
            "deferred route history replays without executing auth or workspace effects");
        replay.Program.Step(1);
        replay.Tester.Settle();
        check(replay.Model.Auth.Session is null && replay.Model.Companies.IsEmpty && replay.Program.PendingCommands == 0,
            "time travel installs the signed-out snapshot without synthetic navigation or service work");
        check(replay.Program.RestoreModel(pending), "a serialized pending return route restores");
        replay.Tester.Settle();
        check(replay.Program.PendingCommands == 0 && replay.Model.Auth.Session is null,
            "restoring a pending route does not start sign-in");
        SignIn(replay);
        check(replay.Model.CompanyId == "northwind", "a restored target is used only after explicit sign-in");
        AuthFlows.Capture(replay, output, "guard-restored");
    }

    static void MissingAndReplaced(string output, Action<bool, string> check)
    {
        using var s = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(s, new CrmMsg.OpenCompany("deleted-company"));
        SignIn(s);
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Rows.Contains("Northwind Labs"),
            "a missing deferred company falls back to the loaded Companies view");
        Send(s, new CrmMsg.OpenCompany(" "));
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Model.PendingNavigation is null,
            "a blank company request falls back safely before constructing a path");
        s.Click(Role.Row, "Northwind Labs");
        Send(s, new CrmMsg.Jump("company:deleted-company"));
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "an authenticated missing company also redirects to Companies");
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Keep this redirect draft");
        Send(s, new CrmMsg.OpenCompany("deleted-company"));
        check(s.Model.PendingNavigation is not null && s.Model.Note == "Keep this redirect draft",
            "a missing-company redirect still guards the note it would discard");
        s.Click(Role.Button, "Keep editing");
        check(s.Model.CompanyId == "northwind", "cancelling a redirect keeps the original visit");
        AuthFlows.Capture(s, output, "guard-redirect-draft");

        using var latest = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(latest, new CrmMsg.OpenCompany("northwind"));
        Send(latest, new CrmMsg.Jump("company:kestrel"));
        SignIn(latest);
        check(latest.Model.CompanyId == "kestrel", "a newer signed-out company request replaces the older target");

        using var elsewhere = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(elsewhere, new CrmMsg.OpenCompany("northwind"));
        Send(elsewhere, new CrmMsg.Navigate(ScreenKind.People));
        SignIn(elsewhere);
        check(ScreenRoutes.Kind(elsewhere.Model.Screen) == ScreenKind.People, "navigation to another section supersedes a deferred company");

        using var root = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(root, new CrmMsg.OpenCompany("northwind"));
        Send(root, new CrmMsg.ShowList(CompanyList.All));
        SignIn(root);
        check(ScreenRoutes.Kind(root.Model.Screen) == ScreenKind.Companies, "choosing Companies at the existing root clears a deferred company");
    }

    static void AuthBoundaries(string output, Action<bool, string> check)
    {
        using var failed = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(failed, new CrmMsg.OpenCompany("northwind"));
        AuthFlows.SetEmail(failed, "unknown@harbor.app");
        SignIn(failed);
        check(failed.Model.Auth.Session is null && failed.Tester.HasText("Choose a demo account listed below."),
            "a rejected sign-in leaves the sign-in view usable");
        AuthFlows.SetEmail(failed, "maya@harbor.app");
        SignIn(failed);
        check(ScreenRoutes.Kind(failed.Model.Screen) == ScreenKind.Companies, "a failed sign-in clears the old return target before a later success");

        using var logout = new CrmSession(model: CrmApp.Init, services: new FakeCrmBackend(TimeSpan.Zero).Services());
        Send(logout, new CrmMsg.OpenCompany("northwind"));
        Auth(logout, new AuthMsg.SignOut());
        Auth(logout, new AuthMsg.SignedIn(0, Data.Initial().Auth.Session!));
        SignIn(logout);
        check(ScreenRoutes.Kind(logout.Model.Screen) == ScreenKind.Companies, "sign-out and a stale auth response cannot replay an old target");
        Send(logout, new CrmMsg.OpenCompany("northwind"));
        var request = logout.Model.Backend.Request;
        Auth(logout, new AuthMsg.SessionExpired(request));
        check(logout.Model.Auth.Session is null && logout.Model.Companies.IsEmpty, "session expiry clears the private workspace");
        SignIn(logout);
        check(ScreenRoutes.Kind(logout.Model.Screen) == ScreenKind.Companies, "session expiry starts a fresh sign-in without resuming the previous route");
        AuthFlows.Capture(logout, output, "guard-fresh-session");
    }

    static void Loading(string output, Action<bool, string> check)
    {
        var backend = new FakeCrmBackend(TimeSpan.Zero);
        var api = new HeldApi(backend);
        using var s = new CrmSession(model: CrmApp.Init,
            services: new KumikoServices().Add<IAuthService>(backend).Add<ICrmApi>(api).Add<ICrmDesktop>(new OfflineDesktop("test")));
        Send(s, new CrmMsg.OpenCompany("northwind"));
        s.Click(Role.Button, "Sign in");
        WaitForLoad(s, api);
        var request = s.Model.Backend.Request;
        Auth(s, new AuthMsg.WorkspaceLoaded(request - 1, WorkspaceData.From(Data.Initial())));
        Auth(s, new AuthMsg.AuthFailed(s.Model.Auth.Request - 1, "stale failure"));
        check(s.Model.Backend.Loading && s.Model.CompanyId is null, "stale completions cannot consume the current loading target");
        Send(s, new CrmMsg.OpenCompany("kestrel"));
        api.Completion.SetException(new IOException("Route test load failed."));
        s.Idle();
        check(s.Tester.HasText("Route test load failed."), "workspace failure is visible before a return route is admitted");
        api.Hold = false;
        s.Click(Role.Button, "Retry loading");
        s.Idle();
        check(s.Model.CompanyId == "kestrel", "navigation during loading survives a failed load and retry");
        AuthFlows.Capture(s, output, "guard-retry-return");

        var nextBackend = new FakeCrmBackend(TimeSpan.Zero);
        var nextApi = new HeldApi(nextBackend);
        using var cancelled = new CrmSession(model: CrmApp.Init,
            services: new KumikoServices().Add<IAuthService>(nextBackend).Add<ICrmApi>(nextApi).Add<ICrmDesktop>(new OfflineDesktop("test")));
        Send(cancelled, new CrmMsg.OpenCompany("northwind"));
        cancelled.Click(Role.Button, "Sign in");
        WaitForLoad(cancelled, nextApi);
        var stale = cancelled.Model.Backend.Request;
        cancelled.Click(Role.Button, "Sign out");
        nextApi.Completion.SetResult();
        cancelled.Idle();
        Auth(cancelled, new AuthMsg.WorkspaceLoaded(stale, WorkspaceData.From(Data.Initial())));
        check(cancelled.Model.Auth.Session is null && cancelled.Model.Companies.IsEmpty,
            "a load finishing after sign-out cannot restore data or navigate");
        nextApi.Hold = false;
        SignIn(cancelled);
        check(ScreenRoutes.Kind(cancelled.Model.Screen) == ScreenKind.Companies, "sign-out during workspace loading also clears the queued target");
    }

    static void WaitForLoad(CrmSession s, HeldApi api)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!api.Started)
        {
            s.Tester.Settle();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Guard workspace load never started.");
            Thread.Sleep(1);
        }
        s.Tester.Settle();
    }

    static void Confirmations(string output, Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Unsaved confirmation draft");
        s.Click(Role.Button, "Next company");
        var target = ((Screen.CompanyDetails)s.Model.PendingNavigation!.Desired.Top.Route).Id;
        var path = Path.Combine(output, "guard-confirm-model.json");
        s.Program.SaveModel(path);
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        var companies = json["Companies"]!.AsArray();
        companies.Remove(companies.First(c => c!["Id"]!.GetValue<string>() == target));
        File.WriteAllText(path, json.ToJsonString());
        check(s.Program.RestoreModel(path), "a pending confirmation can restore after its target company disappears");
        s.Tester.Settle();
        check(s.Model.PendingNavigation is not null, "restore alone does not evaluate entry guards");
        s.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies && s.Model.PendingNavigation is null,
            "confirmed navigation rechecks a now-missing company and redirects safely");
        AuthFlows.Capture(s, output, "guard-stale-confirmation");

        using var signedOut = new CrmSession();
        signedOut.Click(Role.Row, "Northwind Labs");
        signedOut.Type(Role.TextField, "Add a note", "Original draft");
        signedOut.Click(Role.Button, "Next company");
        var pending = signedOut.Model.PendingNavigation!;
        var authPath = Path.Combine(output, "guard-confirm-signed-out.json");
        signedOut.Program.SaveModel(authPath);
        var authJson = JsonNode.Parse(File.ReadAllText(authPath))!;
        authJson["Auth"]!["Session"] = null;
        File.WriteAllText(authPath, authJson.ToJsonString());
        check(signedOut.Program.RestoreModel(authPath), "a signed-out pending snapshot installs without navigation");
        signedOut.Tester.Settle();
        Send(signedOut, new CrmMsg.ConfirmNavigation(pending.Request));
        check(signedOut.Model.Auth.Session is null && signedOut.Model.CompanyId == "northwind" && signedOut.Model.Note == "Original draft"
            && signedOut.Model.PendingNavigation is null,
            "a signed-out confirmation cannot commit its protected path");

        using var covered = new CrmSession();
        covered.Click(Role.Row, "Northwind Labs");
        covered.Type(Role.TextField, "Add a note", "Covered draft");
        Send(covered, new CrmMsg.OpenCompany("kestrel"));
        covered.Type(Role.TextField, "Add a note", "Top draft");
        covered.Click(Role.Button, "Next company");
        var extraPath = Path.Combine(output, "guard-confirm-covered.json");
        covered.Program.SaveModel(extraPath);
        var extra = JsonNode.Parse(File.ReadAllText(extraPath))!;
        var missing = ((Screen.CompanyDetails)covered.Model.PendingNavigation!.Desired.Top.Route).Id;
        var records = extra["Companies"]!.AsArray();
        records.Remove(records.First(c => c!["Id"]!.GetValue<string>() == missing));
        File.WriteAllText(extraPath, extra.ToJsonString());
        check(covered.Program.RestoreModel(extraPath), "a covered draft and changed confirmation target restore");
        covered.Tester.Settle();
        covered.Click(Role.Button, "Discard and leave");
        check(covered.Model.PendingNavigation is not null && covered.Model.CompanyId == "kestrel" && covered.Model.Note == "Top draft",
            "redirecting a confirmed move asks before removing an additional covered draft");
        covered.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(covered.Model.Screen) == ScreenKind.Companies, "confirming the additional discard completes the guarded redirect");
        AuthFlows.Capture(covered, output, "guard-extra-discard");

        using var back = new CrmSession();
        back.Click(Role.Row, "Northwind Labs");
        Send(back, new CrmMsg.OpenCompany("kestrel"));
        var backPath = Path.Combine(output, "guard-back-model.json");
        back.Program.SaveModel(backPath);
        var backJson = JsonNode.Parse(File.ReadAllText(backPath))!;
        var backRecords = backJson["Companies"]!.AsArray();
        backRecords.Remove(backRecords.First(c => c!["Id"]!.GetValue<string>() == "northwind"));
        File.WriteAllText(backPath, backJson.ToJsonString());
        check(back.Program.RestoreModel(backPath), "a path with a removed covered company restores silently");
        back.Tester.Settle();
        back.Click(Role.Button, "Back");
        check(ScreenRoutes.Kind(back.Model.Screen) == ScreenKind.Companies, "Back rechecks retained entries before exposing a missing company");
    }

    sealed class HeldApi(ICrmApi inner) : ICrmApi
    {
        public readonly TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Started;
        public bool Hold = true;

        public async Task<WorkspaceData> LoadAsync(AuthSession session, CancellationToken token)
        {
            var data = await inner.LoadAsync(session, token);
            if (Hold)
            {
                Started = true;
                await Completion.Task.WaitAsync(token);
            }
            return data;
        }

        public Task SaveAsync(AuthSession session, WorkspaceData data, CancellationToken token) => inner.SaveAsync(session, data, token);
        public Task<ImmutableArray<string>> SearchAsync(AuthSession session, string query, CancellationToken token) => inner.SearchAsync(session, query, token);
    }
}
