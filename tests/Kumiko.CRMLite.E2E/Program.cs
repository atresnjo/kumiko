using System.Text.Json;
using CRMLite;
using Kumiko.UI;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/crmlite");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}

if (args.Contains("--search-content-only", StringComparer.Ordinal))
{
    SearchContentFlows.Run(output, Check);
    Console.WriteLine($"{checks.Count} search checks passed; artifacts: {output}");
    return;
}
SearchContentFlows.Run(output, Check);
RouteGuardFlows.Run(output, Check);
NavigationCompatibilityFlows.Run(output, Check);
RouteFlows.Run(output, Check);

using (var crm = new CrmSession())
    CompanyFlows.Run(crm, Check);
using (var crm = new CrmSession())
    DetailFlows.Run(crm, Check);
using (var crm = new CrmSession())
    DealFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    DialogFlows.Run(crm, Check);
using (var crm = new CrmSession())
    PickerFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    DrawerFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    TabFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    MenuFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    SidebarFlows.Run(crm, output, Check);
using (var crm = new CrmSession())
    SettingsFlows.Run(crm, output, Check);
SettingsScrollFlows.Run(output, Check);
PlanFlows.Run(output, Check);
using (var crm = new CrmSession())
    ArtifactFlows.Run(crm, output, Check);
ComponentFlows.Run(output, Check);
NavigationFlows.Run(output, Check);
NavigationReviewFlows.Run(output, Check);
AuthFlows.Run(output, Check);
BackendFlows.Run(output, Check);
HeldCommandFlows.Run(output, Check);
DesktopFlows.Run(output, Check);
DesktopMenuFlows.Run(output, Check);

File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, ReportJson.Default.ListString));
Console.WriteLine($"{checks.Count} CRMLite checks passed; artifacts: {output}");

/// <summary>Drives CRMLite through Kumiko input only; no window, screen or system input is used.</summary>
sealed class CrmSession : IDisposable
{
    public Kumiko.Runtime.Program<CrmModel, CrmMsg> Program
    {
        get;
    }
    public Tester Tester
    {
        get;
    }
    public CrmModel Model => Program.Model;

    public CrmSession(float width = 1560, float height = 980, CrmModel? model = null, Kumiko.Runtime.KumikoServices? services = null)
    {
        var initial = model ?? Data.Initial();
        Program = CrmApp.CreateProgram(initial, services ?? new FakeCrmBackend(seed: initial).Services());
        Tester = new Tester(Program, new(width, height, DevMode: true));
        Tester.Context.ReduceMotion = true;
        Tester.Context.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        Tester.Frame(0);
        Tester.Settle();
    }

    public AccessibleNode Node(Role role, string name) =>
        Tester.Context.Accessibility.Single(n => n.Role == role && n.Name == name && !n.Offscreen);

    public bool Exists(Role role, string name) =>
        Tester.Context.Accessibility.Any(n => n.Role == role && n.Name == name && !n.Offscreen);

    public IEnumerable<AccessibleNode> All(Role role) => Tester.Context.Accessibility.Where(n => n.Role == role && !n.Offscreen);

    public void Click(Role role, string name)
    {
        Tester.Get(role, name).Click();
        Tester.Settle();
    }

    /// <summary>Waits for running commands, such as directory searches, then renders their results.</summary>
    public void Idle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            if (!SpinWait.SpinUntil(() => Program.PendingCommands == 0, TimeSpan.FromSeconds(10)))
                throw new TimeoutException("CRMLite commands did not finish.");
            Tester.Settle();
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("CRMLite command chain did not finish.");
        } while (Program.PendingCommands != 0);
    }

    public void Key(Key key, Modifiers modifiers = Modifiers.None)
    {
        Tester.Key(key, modifiers);
        Tester.Settle();
    }

    public void Type(Role role, string name, string text)
    {
        Click(role, name);
        Tester.TypeText(text);
        Tester.Settle();
    }

    /// <summary>Presses at the center of one node, moves in steps to another node's center and releases.</summary>
    public void Drag(AccessibleNode from, Rect to)
    {
        var (x, y) = (from.Rect.X + from.Rect.Width / 2, from.Rect.Y + from.Rect.Height / 2);
        var (tx, ty) = (to.X + to.Width / 2, to.Y + Math.Min(to.Height / 2, 120));
        Tester.Press(x, y);
        for (var i = 1; i <= 8; i++)
            Tester.MoveTo(x + (tx - x) * i / 8, y + (ty - y) * i / 8);
        Tester.Release(tx, ty);
        Tester.Settle();
    }

    /// <summary>Company names in the order the table shows them.</summary>
    public string[] Rows => [.. All(Role.Row).OrderBy(r => r.Rect.Y).Select(r => r.Name ?? "")];

    public void Dispose()
    {
        Tester.Dispose();
        Program.Dispose();
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
