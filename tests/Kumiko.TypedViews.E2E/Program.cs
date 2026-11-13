using System.Text.Json;
using Kumiko.UI;
using TypedViews;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/typed-views/demo");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
using (var program = DemoApp.CreateProgram())
using (var tester = new Tester(program, new(640, 480, DevMode: true)))
{
    tester.Context.ReduceMotion = true;
    tester.Settle();
    program.Record(Path.Combine(output, "demo-recording.jsonl"));
    Capture.Write(tester, output, "01-auth");
    Check(!tester.Get(Role.Button, "Sign in").Enabled, "blank names cannot sign in");
    tester.Get(Role.TextField, "Display name").Fill("Ada");
    Check(program.Model.SignInName == "Ada" && program.History[^1].Source is { Role: Role.TextField, Name: "Display name", Path: var path } &&
        path.Contains("view[\"entry:0\"]", StringComparison.Ordinal), "deferred accessible edits route from the auth child with their origin");
    tester.Get(Role.TextField, "Display name").Click();
    tester.Key(Key.Enter);
    tester.Settle();
    Check(program.Model.UserName == "Ada" && tester.HasText("Welcome, Ada"), "Enter submits auth and opens the main screen");
    tester.Navigate(NavigationDirection.Back);
    tester.Settle();
    Check(program.Model.Navigation.Current.Route == DemoRoute.Main && !program.Model.Navigation.CanGoBack,
        "sign-in replaces auth so Back cannot reopen the auth screen");
    Capture.Write(tester, output, "02-main");
    tester.Get(Role.Button, "Edit name").Click();
    tester.Settle();
    Check(tester.Get(Role.TextField, "New display name").Enabled, "main output opens the single edit dialog");
    tester.Get(Role.TextField, "New display name").Fill("Grace");
    Capture.Write(tester, output, "03-dialog");
    tester.Get(Role.Button, "Cancel").Click();
    tester.Settle();
    Check(program.Model is { UserName: "Ada", Dialog: null } && tester.HasText("Welcome, Ada"), "Cancel discards the dialog draft");
    tester.Get(Role.Button, "Edit name").Click();
    tester.Settle();
    Check(program.Model.Dialog?.Name == "Ada", "reopened dialog starts with the saved name");
    tester.Get(Role.TextField, "New display name").Fill("");
    Check(!tester.Get(Role.Button, "Save").Enabled, "blank dialog names cannot save");
    tester.Get(Role.TextField, "New display name").Fill("  Grace  ");
    tester.Get(Role.Button, "Save").Invoke();
    tester.Settle();
    Check(program.Model is { UserName: "Grace", Dialog: null } && tester.HasText("Welcome, Grace"), "Save maps the dialog output and updates main");
    Capture.Write(tester, output, "04-saved");
    var session = Path.Combine(output, "demo-session.jsonl");
    program.Save(session);
    using (var replay = DemoApp.CreateProgram())
    {
        replay.Replay(session);
        using var replayTester = new Tester(replay, new(640, 480, DevMode: true));
        replayTester.Context.ReduceMotion = true;
        replayTester.Settle();
        Check(replay.Model.UserName == program.Model.UserName && replay.Model.Navigation.Current == program.Model.Navigation.Current && replayTester.HasText("Welcome, Grace"), "generated child message metadata replays the complete flow");
        Capture.Write(replayTester, output, "05-replay");
    }
    tester.Get(Role.Button, "Edit name").Click();
    tester.Settle();
    tester.Key(Key.Escape);
    tester.Settle();
    Check(program.Model.Dialog is null, "Escape cancels the dialog through its typed output");
    tester.Get(Role.Button, "Sign out").Click();
    tester.Settle();
    Check(program.Model is { UserName: null, SignInName: "", Dialog: null } && !program.Model.Navigation.CanGoBack && !program.Model.Navigation.CanGoForward && tester.Get(Role.TextField, "Display name").Enabled, "sign-out returns to a clean auth screen");
    tester.Navigate(NavigationDirection.Forward);
    tester.Settle();
    Check(program.Model.UserName is null && program.Model.Navigation.Current.Route == DemoRoute.Auth,
        "sign-out resets history so Forward cannot reopen the authenticated screen");
    Capture.Write(tester, output, "06-signed-out");
    program.Save(Path.Combine(output, "signed-out-session.jsonl"));
}
IdentityFlows.Run(output, Check);
NavigationFlows.Run(output, Check);
ShellFlows.Run(output, Check);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
Console.WriteLine($"PASS: {checks.Count} typed-view E2E checks; artifacts: {output}");

static class Capture
{
    public static void Write(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
        if (tester.Context.Warnings.Count != 0)
            throw new InvalidOperationException(name + ": " + string.Join("; ", tester.Context.Warnings.Select(w => w.Message)));
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
internal partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
