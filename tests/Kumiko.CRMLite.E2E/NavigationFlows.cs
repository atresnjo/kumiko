using CRMLite;
using Kumiko.UI;

static class NavigationFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        using var s = new CrmSession();
        s.Program.Record(Path.Combine(output, "navigation-session.jsonl"));
        s.Click(Role.Row, "Northwind Labs");
        var first = s.Model.Navigation.Top.Id;
        s.Type(Role.TextField, "Add a note", "First draft");
        s.Program.Send(new CrmMsg.OpenCompany("kestrel"));
        s.Tester.Settle();
        s.Program.Send(new CrmMsg.OpenCompany("northwind"));
        s.Tester.Settle();
        var second = s.Model.Navigation.Top.Id;
        check(first != second && s.Model.Note == "", "repeated company has an independent entry and draft");
        var request = s.Model.BackTo(s.Model.Navigation.Entries[0].Id);
        s.Program.Send(new CrmMsg.Back(request));
        s.Tester.Settle();
        check(s.Model.PendingNavigation is not null && s.Model.Navigation.Top.Id == second,
            "multi-pop protects unsaved intermediate draft");
        s.Click(Role.Button, "Keep editing");
        s.Program.Send(new CrmMsg.Back(s.Model.BackTo(s.Model.Navigation.Entries[^2].Id)));
        s.Tester.Settle();
        check(s.Model.CompanyId == "kestrel", "Back pops one entry");
        s.Program.Send(new CrmMsg.SetNote(second, "stale"));
        s.Tester.Settle();
        check(s.Model.Note == "", "late edits to removed entries are ignored");
        s.Program.Send(new CrmMsg.Back(request));
        s.Tester.Settle();
        check(s.Model.CompanyId == "kestrel", "stale Back cannot discard newer state");
        s.Tester.Navigate(NavigationDirection.Back);
        s.Tester.Settle();
        check(s.Model.Note == "First draft", "native Back intent reaches the existing guarded stack and restores the original draft");
        s.Tester.Screenshot(Path.Combine(output, "navigation-restored.png"));
        var guarded = s.Model.BackTo(s.Model.Navigation.Entries[0].Id);
        s.Program.Send(new CrmMsg.Back(guarded));
        s.Tester.Settle();
        s.Program.Send(new CrmMsg.OpenCompany("kestrel"));
        s.Tester.Settle();
        s.Program.Send(new CrmMsg.ConfirmNavigation(guarded));
        s.Tester.Settle();
        check(s.Model.CompanyId == "kestrel", "stale confirmation cannot remove a newly opened page");
        s.Program.Send(new CrmMsg.Back(s.Model.BackTo(s.Model.Navigation.Entries[0].Id)));
        s.Tester.Settle();
        s.Click(Role.Button, "Discard and leave");
        check(ScreenRoutes.Kind(s.Model.Screen) == ScreenKind.Companies, "confirmation removes the requested suffix");
        s.Tester.MoveTo(-10, -10);
        s.Tester.Context.Blur();
        s.Tester.Settle();
        s.Tester.Screenshot(Path.Combine(output, "navigation-root.png"));
        File.WriteAllText(Path.Combine(output, "navigation-root.txt"), s.Tester.Dump());
        s.Program.Save(Path.Combine(output, "navigation-replay-session.jsonl"));
        var saved = Path.Combine(output, "navigation-model.json");
        s.Program.SaveModel(saved);
        var invalid = Path.Combine(output, "navigation-invalid.json");
        var json = File.ReadAllText(saved);
        File.WriteAllText(invalid, json.Replace("\"NextId\":" + s.Model.Navigation.NextId, "\"NextId\":0", StringComparison.Ordinal));
        var unchanged = s.Program.History.Count;
        check(!s.Program.RestoreModel(invalid) && s.Program.History.Count == unchanged,
            "invalid restored path is rejected atomically");
        var history = s.Program.History.Count;
        s.Program.Step(history - 2);
        s.Tester.Settle();
        check(s.Program.History.Count == history, "time travel emits no navigation messages");
        using var replay = new CrmSession();
        replay.Program.Replay(Path.Combine(output, "navigation-replay-session.jsonl"));
        replay.Tester.MoveTo(-10, -10);
        replay.Tester.Context.Blur();
        replay.Tester.Settle();
        replay.Tester.Screenshot(Path.Combine(output, "navigation-replay.png"));
        check(File.ReadAllBytes(Path.Combine(output, "navigation-root.png")).SequenceEqual(
            File.ReadAllBytes(Path.Combine(output, "navigation-replay.png"))), "navigation replay pixels match");
        using var deletion = new CrmSession();
        deletion.Click(Role.Row, "Northwind Labs");
        deletion.Type(Role.TextField, "Add a note", "Unsaved before deletion");
        deletion.Program.Send(new CrmMsg.DeleteCompany("northwind"));
        deletion.Tester.Settle();
        check(deletion.Model.PendingNavigation is not null && deletion.Model.Companies.Any(c => c.Id == "northwind"),
            "domain deletion protects dirty visits before removing the company");
        var deletionModel = Path.Combine(output, "navigation-deletion-model.json");
        deletion.Program.SaveModel(deletionModel);
        check(deletion.Program.RestoreModel(deletionModel), "a pending domain deletion round trips through generic confirmation metadata");
        deletion.Tester.Settle();
        deletion.Click(Role.Button, "Discard and leave");
        check(!deletion.Model.Companies.Any(c => c.Id == "northwind") &&
            !deletion.Model.Navigation.Entries.Any(e => e.Route is Screen.CompanyDetails { Id: "northwind" }),
            "confirmed domain deletion removes dangling navigation visits");
        deletion.Tester.Screenshot(Path.Combine(output, "navigation-deleted.png"));
    }
}
