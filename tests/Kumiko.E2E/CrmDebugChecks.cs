using CRMLite;
using Kumiko.UI;

static class CrmDebugChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var initial = Data.Initial();
        using var program = CrmApp.CreateProgram(initial, new FakeCrmBackend(TimeSpan.Zero, initial).Services());
        program.DebugEnabled = true;
        using var tester = new Tester(program, new(1560, 980, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Frame(0);
        tester.Get(Role.Button, "Search").Click();
        tester.Get(Role.Button, "Pause").Click();
        check(program.Model.Popup == Popup.Search && program.IsPaused,
            "debugger clicks do not dismiss the app's open search palette");
        tester.Get(Role.Button, "Resume").Click();
        tester.Get(Role.TextField, "Search companies, people and pages").Click();
        tester.TypeText("Northwind");
        var count = program.History.Count;
        var searchStep = program.History.Last(e => e.Message is CrmMsg.SetQuery).Id;
        while (program.Position > searchStep)
            tester.Get(Role.Button, "Back").Click();
        tester.Get(Role.Button, "Back").Click();
        tester.Get(Role.Button, "Forward").Click();
        tester.Key(Key.Enter);
        check(program.Position == searchStep && program.History.Count == count &&
            program.Model.Popup == Popup.Search && program.Model.Query == "Northwind" && program.Model.CompanyId is null,
            "app autofocus and Enter shortcuts cannot steal debugger navigation");
        tester.Get(Role.Button, "Show events").Click();
        tester.Screenshot(Path.Combine(output, "debug-crm.png"));
        File.WriteAllText(Path.Combine(output, "debug-crm.txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, "debug-crm.warnings.json"));
        program.Save(Path.Combine(output, "debug-crm.jsonl"));
        check(!tester.Context.Warnings.Any(w => w.Path.Contains("kumiko.debugger", StringComparison.Ordinal)),
            "the debugger adds no diagnostics to the CRMLite view");
    }
}
