using System.Text.Json;
using CRMLite;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>
/// Desktop notifications through the real CRM host and a recording shell: when they post, how often, what a click
/// routes to, and what makes a click stale. Agent work advances only on explicit ticks.
/// </summary>
static class DesktopFlows
{
    const string Juniper = "d-juniper", Vantage = "d-vantage";

    public static void Run(string output, Action<bool, string> check)
    {
        Background(output, check);
        Foreground(output, check);
        Stale(output, check);
        Precedence(output, check);
        Saves(output, check);
        Access(output, check);
    }

    internal static CrmSession Session(RecordingDesktop desktop, Func<ICrmApi, ICrmApi>? api = null)
    {
        var seed = Data.Initial() with
        {
            LiveAgent = false
        };
        var backend = new FakeCrmBackend(TimeSpan.Zero, seed);
        var services = new KumikoServices().Add<IAuthService>(backend).Add<ICrmApi>(api?.Invoke(backend) ?? backend).Add<ICrmDesktop>(desktop);
        return new CrmSession(model: seed, services: services);
    }

    static void Signal(CrmSession s, RecordingDesktop desktop, DesktopSignal signal)
    {
        desktop.Emit(signal);
        s.Idle();
    }

    static void StartPlan(CrmSession s, string deal)
    {
        s.Program.Send(new CrmMsg.OpenPlan(deal));
        s.Program.Send(new CrmMsg.ApprovePlan());
        s.Program.Send(new CrmMsg.CloseDialog());
        s.Idle();
    }

    static void TickUntil(CrmSession s, Func<CrmModel, bool> done)
    {
        for (var i = 0; i < 80 && !done(s.Model); i++)
        {
            s.Program.Send(new CrmMsg.AgentTick());
            s.Idle();
        }
        if (!done(s.Model))
            throw new TimeoutException("The agent plan did not reach the expected state.");
    }

    static void Background(string output, Action<bool, string> check)
    {
        var desktop = new RecordingDesktop();
        using var s = Session(desktop);
        check(s.Model.Desktop.Access == NoticeAccess.Ready && desktop.Listeners == 1, "the injected shell reports access through one subscription");
        s.Click(Role.Button, "Deals");
        s.Click(Role.Button, "Juniper Retail, Partner billing");
        s.Click(Role.Button, "Plan with agent");
        s.Click(Role.Button, "Approve plan");
        s.Key(Key.Escape);
        s.Click(Role.Button, "Companies");
        Signal(s, desktop, new DesktopSignal.Active(false));
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed);
        check(desktop.Posts.Count == 1 && desktop.Posts[0] is { Id: "plan:" + Juniper, Title: "Agent plan needs attention" } post &&
            post.Body.Contains("Book a discovery call", StringComparison.Ordinal),
            "a background plan failure posts one notice naming the failed step");
        for (var i = 0; i < 4; i++)
            s.Program.Send(new CrmMsg.AgentTick());
        s.Idle();
        s.Tester.Settle();
        check(desktop.Posts.Count == 1, "later ticks and re-renders do not post the failure again");
        AuthFlows.Capture(s, output, "desktop-plan-failed");

        var entries = s.Model.Navigation.Entries.Length;
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        check(s.Model.CompanyId == "juniper" && s.Model.Overlay is Overlay.Plan { DealId: Juniper } && s.Model.Navigation.Entries.Length == entries + 1,
            "a click opens the deal's company and its plan exactly once");
        check(desktop.Activations == 1 && s.Model.Desktop.Posted.IsEmpty && s.Program.History.Count(step => step.Message is CrmMsg.Desktop { Message: DesktopMsg.Clicked }) == 2,
            "a duplicate native click is delivered as a message but routes and brings the app forward once");
        AuthFlows.Capture(s, output, "desktop-plan-routed");

        s.Click(Role.Button, "Retry");
        s.Key(Key.Escape);
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Done);
        check(desktop.Posts.Count == 2 && desktop.Posts[1] is { Id: "plan:" + Juniper, Title: "Agent plan finished" },
            "the retried plan's completion posts one replacing notice");
        Signal(s, desktop, new DesktopSignal.Active(true));
        s.Program.Send(new CrmMsg.OpenPlan(Juniper));
        s.Idle();
        check(desktop.Withdrawn.Contains("plan:" + Juniper) && s.Model.Desktop.Posted.IsEmpty, "opening the plan in the app withdraws its notice");
        var activations = desktop.Activations;
        var model = s.Model;
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        Signal(s, desktop, new DesktopSignal.Clicked("plan:never-posted"));
        check(s.Model == model && desktop.Activations == activations, "withdrawn and unknown clicks are ignored without activation");
        File.WriteAllText(Path.Combine(output, "desktop-background.json"), JsonSerializer.Serialize(desktop.Log, DesktopJson.Default.IReadOnlyListString));
        s.Program.Save(Path.Combine(output, "desktop-background.jsonl"));
        using var replay = Session(new RecordingDesktop());
        replay.Program.Replay(Path.Combine(output, "desktop-background.jsonl"));
        replay.Tester.Settle();
        check(replay.Model.PlanFor(Juniper)?.Status == PlanStatus.Done && replay.Program.PendingCommands == 0,
            "the desktop session replays its decisions without effects");
    }

    static void Foreground(string output, Action<bool, string> check)
    {
        var desktop = new RecordingDesktop();
        using var s = Session(desktop);
        StartPlan(s, Juniper);
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed);
        check(desktop.Posts.Count == 0, "a plan that fails while CRM Lite is frontmost stays an in-app toast");
        var settings = s.Tester.DesktopMenu.Single(item => item.Id == CrmCommands.NotificationSettings);
        settings.Click!(settings);
        s.Tester.Settle();
        check(s.Model.Settings is { Open: true, Section: SettingsSection.Notifications } && s.Exists(Role.Switch, "Desktop notifications"),
            "Notification Settings opens the notifications section");
        AuthFlows.Capture(s, output, "desktop-settings-ready");
        s.Click(Role.Switch, "Desktop notifications");
        check(!s.Model.Preferences.DesktopAlerts, "the desktop notifications switch turns the preference off");
        s.Key(Key.Escape);
        Signal(s, desktop, new DesktopSignal.Active(false));
        s.Program.Send(new CrmMsg.OpenPlan(Juniper));
        s.Program.Send(new CrmMsg.RetryStep());
        s.Program.Send(new CrmMsg.CloseDialog());
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Done);
        check(desktop.Posts.Count == 0, "a background plan posts nothing while the preference is off");
    }

    static void Stale(string output, Action<bool, string> check)
    {
        var desktop = new RecordingDesktop();
        using var s = Session(desktop);
        StartPlan(s, Juniper);
        StartPlan(s, Vantage);
        Signal(s, desktop, new DesktopSignal.Active(false));
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed && m.PlanFor(Vantage)!.Status == PlanStatus.Failed);
        check(desktop.Posts.Select(p => p.Id).Order().SequenceEqual(["plan:" + Juniper, "plan:" + Vantage]),
            "two plans that fail on the same beat post one notice each");
        s.Program.Send(new CrmMsg.DeleteCompany("juniper"));
        s.Idle();
        check(desktop.Withdrawn.Contains("plan:" + Juniper) && s.Model.Desktop.Posted.Single().Id == "plan:" + Vantage,
            "deleting a company withdraws its plan's notice");
        var screen = s.Model.Screen;
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        check(s.Model.Screen == screen && desktop.Activations == 0, "a click for a deleted plan neither routes nor activates");
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.SignOut()));
        s.Idle();
        check(desktop.Clears == 1 && s.Model.Desktop.Posted.IsEmpty, "signing out clears every posted notice and its customer text");
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Vantage));
        check(s.Model.Auth.Session is null && s.Model.Companies.IsEmpty && desktop.Activations == 0,
            "a late click from the signed-out session restores nothing");
        AuthFlows.Capture(s, output, "desktop-signed-out-click");
    }

    static void Precedence(string output, Action<bool, string> check)
    {
        var desktop = new RecordingDesktop();
        using var s = Session(desktop);
        StartPlan(s, Juniper);
        Signal(s, desktop, new DesktopSignal.Active(false));
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed);
        s.Program.Send(new CrmMsg.OpenCompanyDialog());
        s.Program.Send(new CrmMsg.EditCompany(new("Draft Co", "draft.co", "", "maya")));
        s.Idle();
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        check(s.Model.CompanyDraft?.Name == "Draft Co" && s.Model.Screen is Screen.Companies && desktop.Activations == 1,
            "a click while a New company draft is open keeps the draft and only brings the app forward");
        AuthFlows.Capture(s, output, "desktop-click-keeps-draft");
        s.Program.Send(new CrmMsg.CloseDialog());
        s.Program.Send(new CrmMsg.OpenPlan(Juniper));
        s.Program.Send(new CrmMsg.RetryStep());
        s.Program.Send(new CrmMsg.CloseDialog());
        s.Idle();
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Unsaved thought");
        TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Done);
        Signal(s, desktop, new DesktopSignal.Clicked("plan:" + Juniper));
        check(s.Model.CompanyId == "juniper" && s.Model.Overlay is Overlay.Plan { DealId: Juniper } &&
            s.Model.Navigation.Entries.Any(entry => entry.Note == "Unsaved thought") && s.Model.PendingNavigation is null,
            "a click pushes the plan's company and keeps the unsaved note in the visit underneath");
        AuthFlows.Capture(s, output, "desktop-click-keeps-note");
    }

    sealed class FailingApi(ICrmApi inner) : ICrmApi
    {
        public volatile bool Fail;
        public Task<WorkspaceData> LoadAsync(AuthSession session, CancellationToken token) => inner.LoadAsync(session, token);
        public Task SaveAsync(AuthSession session, WorkspaceData data, CancellationToken token) =>
            Fail ? Task.FromException(new IOException("Demo save failed.")) : inner.SaveAsync(session, data, token);
        public Task<System.Collections.Immutable.ImmutableArray<string>> SearchAsync(AuthSession session, string query, CancellationToken token) =>
            inner.SearchAsync(session, query, token);
    }

    static void Saves(string output, Action<bool, string> check)
    {
        var desktop = new RecordingDesktop();
        FailingApi? api = null;
        using var s = Session(desktop, inner => api = new FailingApi(inner)
        {
            Fail = true
        });
        s.Click(Role.Row, "Northwind Labs");
        s.Type(Role.TextField, "Add a note", "Saved in the foreground");
        s.Key(Key.Enter);
        s.Idle();
        check(s.Model.Backend.Error is not null && desktop.Posts.Count == 0, "a failed save while frontmost stays in the sidebar status");
        Signal(s, desktop, new DesktopSignal.Active(false));
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.RetrySave()));
        s.Idle();
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.RetrySave()));
        s.Idle();
        check(desktop.Posts.Count == 1 && desktop.Posts[0] is { Id: "save", Title: "Changes not saved" } &&
            s.Model.Desktop.Posted.Count(p => p.Id == "save") == 1, "repeated background save failures keep one save notice");
        api!.Fail = false;
        s.Program.Send(new CrmMsg.Authentication(new AuthMsg.RetrySave()));
        s.Idle();
        check(!s.Model.Backend.Dirty && desktop.Withdrawn.Contains("save") && s.Model.Desktop.Posted.IsEmpty,
            "a later successful save withdraws the failure notice");
        File.WriteAllText(Path.Combine(output, "desktop-saves.json"), JsonSerializer.Serialize(desktop.Log, DesktopJson.Default.IReadOnlyListString));
    }

    static void Access(string output, Action<bool, string> check)
    {
        var denied = new RecordingDesktop
        {
            Failure = new UnauthorizedAccessException("Notification authorization was denied.")
        };
        using (var s = Session(denied))
        {
            StartPlan(s, Juniper);
            Signal(s, denied, new DesktopSignal.Active(false));
            TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed);
            StartPlan(s, Vantage);
            TickUntil(s, m => m.PlanFor(Vantage)!.Status == PlanStatus.Failed);
            check(denied.Posts.Count == 1 && s.Model.Desktop.Access == NoticeAccess.Denied && s.Model.Desktop.Posted.IsEmpty,
                "a denied permission stops further attempts instead of prompting again");
            s.Program.Send(new CrmMsg.OpenSettings(SettingsSection.Notifications));
            s.Idle();
            check(s.Tester.ContainsText("System Settings"), "settings explain where to allow a denied permission");
            AuthFlows.Capture(s, output, "desktop-settings-denied");
        }
        var offline = new RecordingDesktop(NoticeAccess.Unavailable, "Unbundled host");
        using (var s = Session(offline))
        {
            StartPlan(s, Juniper);
            Signal(s, offline, new DesktopSignal.Active(false));
            TickUntil(s, m => m.PlanFor(Juniper)!.Status == PlanStatus.Failed);
            s.Program.Send(new CrmMsg.OpenSettings(SettingsSection.Notifications));
            s.Idle();
            check(offline.Posts.Count == 0 && s.Tester.ContainsText("packaged CRMLite app"),
                "an unavailable host posts nothing and the settings row says why");
            AuthFlows.Capture(s, output, "desktop-settings-unavailable");
        }
        check(new OfflineDesktop("Offscreen snapshot").Post(new("x", "x", "x"), CancellationToken.None).IsFaulted,
            "the offscreen shell refuses to post");
        check(CrmDesktopHost.Offscreen(["--kumiko-snapshot=a.png"]) && CrmDesktopHost.Offscreen(["--kumiko-tree=a.txt"]) &&
            CrmDesktopHost.Offscreen(["--kumiko-agent"]) && CrmDesktopHost.Offscreen(["--kumiko-replay=a.jsonl"]) &&
            CrmDesktopHost.Offscreen(["--kumiko-audit=a.json"]) && CrmDesktopHost.Offscreen(["--kumiko-step=1"]) &&
            !CrmDesktopHost.Offscreen(["--demo", "--dark"]), "snapshot, tree, audit, agent and replay runs use the offscreen shell");
    }
}
