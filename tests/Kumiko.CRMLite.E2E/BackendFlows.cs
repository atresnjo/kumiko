using System.Collections.Immutable;
using CRMLite;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Controlled transport failures and late responses through the actual CRM views.</summary>
static class BackendFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var backend = new FakeCrmBackend(TimeSpan.Zero);
        var api = new ControlledApi(backend) { FailLoad = true };
        var services = new KumikoServices().Add<IAuthService>(backend).Add<ICrmApi>(api).Add<ICrmDesktop>(new OfflineDesktop("test"));
        using var s = new CrmSession(model: CrmApp.Init, services: services);
        s.Click(Role.Button, "Sign in");
        s.Idle();
        check(!s.Model.Backend.Loaded && s.Tester.HasText("Demo connection failed."), "API load failures are visible with a retry action");
        AuthFlows.Capture(s, output, "auth-load-error");
        s.Click(Role.Button, "Retry loading");
        s.Idle();
        check(s.Model.Companies.Length == 23 && s.Model.Backend.Loaded, "retry loads customer data through the injected API");
        api.FailSave = true;
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Survives a failed save");
        s.Key(Key.Enter);
        s.Idle();
        check(s.Model.Backend.Dirty && !s.Model.Backend.Saving && s.Tester.HasText("Demo save failed."), "failed autosave keeps optimistic changes and offers retry");
        var revision = s.Model.Backend.Revision;
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.SignOut()));
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.RefreshWorkspace()));
        s.Tester.Settle();
        check(s.Model.Auth.Session is not null && s.Model.Backend.Dirty && s.Model.Backend.Revision == revision,
            "refresh and sign-out cannot discard unsaved edits");
        AuthFlows.Capture(s, output, "auth-save-error");
        s.Click(Role.Button, "Retry save");
        s.Idle();
        check(!s.Model.Backend.Dirty && s.Tester.HasText("All changes saved"), "retry persists the retained document");
        s.Click(Role.Button, "Refresh data");
        s.Idle();
        check(s.Model.Activities.Any(a => a.Body == "Survives a failed save"), "refresh returns the saved data from the backend");

        api.HoldLoad = new(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Click(Role.Button, "Refresh data");
        if (!SpinWait.SpinUntil(() => api.LoadStarted, TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Held load never started.");
        check(s.Model.Backend.Loading && s.Tester.HasText("Loading your CRM…"), "an in-flight API request renders a loading state");
        AuthFlows.Capture(s, output, "auth-loading");
        var oldRequest = s.Model.Backend.Request;
        s.Click(Role.Button, "Sign out");
        s.Tester.Settle();
        check(s.Model.Companies.IsEmpty && s.Model.Navigation.Entries.Length == 1, "sign-out removes retained navigation and private models immediately");
        api.HoldLoad.SetResult();
        s.Idle();
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.WorkspaceLoaded(oldRequest, WorkspaceData.From(Data.Initial()))));
        s.Tester.Settle();
        check(s.Model.Auth.Session is null && s.Model.Companies.IsEmpty, "a late API response cannot restore signed-out data");

        AuthFlows.SetEmail(s, "daniel@harbor.app");
        s.Click(Role.Button, "Sign in");
        s.Idle();
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.WorkspaceSaveFailed(oldRequest, revision, "stale failure")));
        s.Tester.Settle();
        check(s.Model.Backend.Error is null && s.Model.Auth.Session?.User.Id == "daniel", "an old account's response cannot overwrite the new session");
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "First edit");
        s.Key(Key.Enter);
        s.Type(Role.TextField, "Add a note", "Latest edit");
        s.Key(Key.Enter);
        s.Idle();
        s.Click(Role.Button, "Refresh data");
        s.Idle();
        check(s.Model.Activities.Any(a => a.Body == "First edit") && s.Model.Activities.Any(a => a.Body == "Latest edit"),
            "successive edits survive debounced autosave and refresh together");
        // The server expires a session independently of the UI, as a real backend can.
        backend.SignOutAsync(s.Model.Auth.Session!, CancellationToken.None).GetAwaiter().GetResult();
        s.Click(Role.Button, "Refresh data");
        s.Idle();
        check(s.Model.Auth.Session is null && s.Model.Companies.IsEmpty && s.Tester.HasText("Your session ended. Sign in again."),
            "an expired backend session clears customer data and returns to sign-in");
        AuthFlows.Capture(s, output, "auth-expired");

        var viewer = Data.Initial() with
        {
            Auth = new(new("fake-viewer", Data.Team.First(m => m.Id == "nina"), false))
        };
        var viewerBackend = new FakeCrmBackend(TimeSpan.Zero, viewer);
        var forged = viewer with
        {
            Auth = viewer.Auth with
            {
                Session = viewer.Auth.Session! with
                {
                    CanEdit = true
                }
            }
        };
        using var forgedClient = new CrmSession(model: forged, services: viewerBackend.Services());
        forgedClient.Click(Role.Row, "Northwind Labs");
        forgedClient.Click(Role.Switch, "Hot account");
        forgedClient.Idle();
        check(forgedClient.Model.Auth.Session is null && forgedClient.Model.Companies.IsEmpty,
            "the backend rejects edits even when a client alters its permission snapshot");
        using var actualViewer = new CrmSession(model: viewer, services: viewerBackend.Services());
        actualViewer.Click(Role.Button, "Refresh data");
        actualViewer.Idle();
        check(actualViewer.Model.Companies.First(c => c.Id == "northwind").Hot, "rejected writes never change backend data");
    }

    sealed class ControlledApi(ICrmApi inner) : ICrmApi
    {
        public bool FailLoad
        {
            get; set;
        }
        public bool FailSave
        {
            get; set;
        }
        public TaskCompletionSource? HoldLoad
        {
            get; set;
        }
        public volatile bool LoadStarted;

        public async Task<WorkspaceData> LoadAsync(AuthSession session, CancellationToken token)
        {
            if (FailLoad)
            {
                FailLoad = false;
                throw new IOException("Demo connection failed.");
            }
            var data = await inner.LoadAsync(session, token);
            if (HoldLoad is { } hold)
            {
                LoadStarted = true;
                await hold.Task.WaitAsync(token);
            }
            return data;
        }

        public Task SaveAsync(AuthSession session, WorkspaceData data, CancellationToken token)
        {
            if (FailSave)
            {
                FailSave = false;
                throw new IOException("Demo save failed.");
            }
            return inner.SaveAsync(session, data, token);
        }

        public Task<ImmutableArray<string>> SearchAsync(AuthSession session, string query, CancellationToken token) => inner.SearchAsync(session, query, token);
    }
}
