using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CRMLite;
using Kumiko;
using Kumiko.Rendering;
using Kumiko.UI;

if (args.Contains("--fixture", StringComparer.Ordinal))
{
    Dev.Run<FormApp>("Agent forms", 640, 640, args);
    return 0;
}
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/agent-readiness/probe");
Directory.CreateDirectory(output);
var results = new List<ProbeResult>();
FormFlows.Run(Path.Combine(output, "forms"));
foreach (var native in OperatingSystem.IsMacOS() ? new[] { false, true } : new[] { false })
{
    Run("create-company", s =>
    {
        s.Press("AXButton", "New company");
        s.Press("AXButton", "Create company");
        s.Require(s.Find("AXTextField", "Company name").Help == "Enter the company name.", "empty form exposes validation");
        s.Set("Company name", "Agent Harbor");
        s.Set("Domain", "invalid");
        s.Press("AXButton", "Create company");
        s.Require(s.Nodes().Any(n => n.Help?.Contains("domain") == true), "invalid domain is associated with the field");
        s.Set("Domain", "agent-harbor.example");
        s.Press("AXButton", "Create company");
        s.Require(s.Nodes().Any(n => n.Name == "Agent Harbor") && !s.Exists("AXButton", "Create company"), "creation opens the new company");
        s.Press("AXButton", "Back to companies");
        s.Set("Search companies", "Agent Harbor");
        s.Require(s.Exists("AXRow", "Agent Harbor"), "new company can be found through search");
    });
    Run("create-deal", s =>
    {
        s.Press("AXButton", "Deals");
        s.Press("AXButton", "New deal");
        s.Press("AXComboBox", "Company");
        s.Set("Search Company", "glacier");
        s.Press("AXRow", "Glacier Cloud");
        s.Set("Deal name", "Agent expansion");
        s.Set("Value", "$120k");
        s.Press("AXComboBox", "Stage");
        s.Press("AXMenuItem", "Demo");
        s.Press("AXButton", "Create deal");
        s.Require(s.Exists("AXButton", "Glacier Cloud, Agent expansion"), "new deal is visible on the board");
    });
    Run("modal-isolation", s =>
    {
        s.Press("AXButton", "New company");
        var background = s.Nodes().Where(n => n.Role == "AXButton" && n.Name == "Deals").ToArray();
        s.Require(background.Length == 0 || !background[0].Enabled, "background navigation is absent or disabled while a modal is open");
    });
    Run("modal-background-action", s =>
    {
        s.Press("AXButton", "New company");
        var background = s.Nodes().SingleOrDefault(n => n.Role == "AXButton" && n.Name == "Deals");
        if (background is null)
            return;
        var accepted = s.Action(background, "press");
        s.Tick();
        s.Save("background-action");
        s.Require(!accepted, "background accessibility action is rejected");
    });
    Run("expand-contract", s =>
    {
        s.Press("AXButton", "New company");
        var owner = s.Find("AXComboBox", "Owner");
        s.Action(owner, "expand");
        s.Action(owner, "expand");
        s.Tick();
        s.Save("expand");
        s.Require(s.Find("AXComboBox", "Owner").Expanded == true && s.Exists("AXMenuItem", "Maya Brooks"), "Expand opens the owner options");
    });
    Run("stale-reference", s =>
    {
        s.Press("AXButton", "New company");
        using var submit = s.RetainPress(s.Find("AXButton", "Create company"));
        s.Press("AXButton", "Close");
        s.Require(!s.Exists("AXButton", "Create company"), "closed dialog removes its controls");
        s.Require(!submit.Invoke(), "retained reference to a removed control is rejected");
        s.Tick();
    });
    Run("offscreen-option", s =>
    {
        s.Press("AXButton", "Deals");
        s.Press("AXButton", "New deal");
        s.Press("AXComboBox", "Company");
        s.Require(s.Exists("AXRow", "Tidewater Labs"), "a later company can be discovered without pointer scrolling");
        s.Reveal(s.Find("AXRow", "Tidewater Labs"));
        s.Press("AXRow", "Tidewater Labs");
        s.Require(s.Find("AXComboBox", "Company").Value == "Tidewater Labs", "revealed option updates the dialog without submitting it");
    });
    if (native && Environment.GetEnvironmentVariable("KUMIKO_AGENT_AX_CLIENT") is { } client)
        Run("external-discovery", s => s.External(client));

    void Run(string scenario, Action<ProbeSession> flow)
    {
        var path = Path.Combine(output, native ? "native" : "semantic", scenario);
        using var s = new ProbeSession(native, path);
        try
        {
            flow(s);
            results.Add(new(native ? "NSAccessibility" : "Context", scenario, "pass", "Completed through tree actions"));
        }
        catch (ProbeBlockedException ex)
        {
            results.Add(new(native ? "NSAccessibility" : "Context", scenario, "blocked", ex.Message));
        }
        catch (Exception ex)
        {
            results.Add(new(native ? "NSAccessibility" : "Context", scenario, "fail", ex.Message));
        }
        s.Save("final", screenshot: true);
        Console.WriteLine($"{results[^1].Provider}: {scenario}: {results[^1].Status} {results[^1].Detail}");
    }
}
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(results, ProbeJson.Default.ListProbeResult));
return results.Any(r => r.Status == "fail") ? 1 : results.Any(r => r.Status == "blocked") ? 2 : 0;

sealed class ProbeSession : IDisposable
{
    readonly Kumiko.Runtime.Program<CrmModel, CrmMsg> program = CrmApp.CreateProgram(Data.Initial(), new FakeCrmBackend().Services());
    readonly Tester? tester;
    readonly App? app;
    readonly Window? window;
    readonly NativeTree? native;
    readonly string output;
    readonly List<ProbeAction> actions = [];
    int step;
    public ProbeSession(bool useNative, string output)
    {
        this.output = output;
        Directory.CreateDirectory(output);
        program.Record(Path.Combine(output, "session.jsonl"));
        if (useNative)
        {
            app = new App(new("Kumiko agent probe", Hidden: true));
            window = app.NewWindow(new(Title: "Kumiko agent probe", Width: 1560, Height: 980, Visible: false), program);
            window.Context!.ReduceMotion = true;
            Tick();
            native = new NativeTree("Kumiko agent probe");
            Require(!window.IsVisible && window.Context.ReadClipboard is null && window.Context.WriteClipboard is null,
                "native probe remains hidden and uses an isolated clipboard");
        }
        else
        {
            tester = new Tester(program, new(1560, 980));
            tester.Context.ReduceMotion = true;
            Tick();
        }
        Save("initial");
    }
    public ProbeNode[] Nodes() => native?.Snapshot() ?? tester!.Context.Accessibility.Where(n => !n.Hidden).Select(n => new ProbeNode(
        n.Id.ToString("x16"), n.Parent.ToString("x16"), NativeTree.RoleName(n.Role), n.Name, n.Value,
        n.Enabled, n.Properties?.Expanded, n.Properties?.Error ?? n.Properties?.Description)).ToArray();
    public ProbeNode Find(string role, string name)
    {
        var found = Nodes().Where(n => n.Role == role && n.Name == name).ToArray();
        Require(found.Length == 1, $"Expected one {role} named '{name}'; found {found.Length}");
        return found[0];
    }
    public bool Exists(string role, string name) => Nodes().Any(n => n.Role == role && n.Name == name);
    public bool Action(ProbeNode node, string action, string? value = null)
    {
        var accepted = native is not null ? native.Action(node.Id, action, value) :
            tester!.Context.PerformAccessibilityAction(ulong.Parse(node.Id, NumberStyles.HexNumber), new(action switch
            {
                "set" => AccessibilityAction.SetValue,
                "expand" => AccessibilityAction.Expand,
                "reveal" => AccessibilityAction.Reveal,
                _ => AccessibilityAction.Invoke
            }, Text: value));
        actions.Add(new(node.Id, node.Role, node.Name, action, value, native is not null && action != "press" ? null : accepted));
        return accepted;
    }
    public void Reveal(ProbeNode node)
    {
        if (native is null)
            Require(Action(node, "reveal"), "semantic Reveal succeeds");
        else
        {
            var nodes = Nodes();
            var ancestor = nodes.Single(n => n.Id == node.Parent);
            // The nearest scroller: a scroll area, or a list (listbox) that scrolls its own rows.
            while (ancestor.Role is not ("AXScrollArea" or "AXList"))
                ancestor = nodes.Single(n => n.Id == ancestor.Parent);
            Require(Action(ancestor, "end"), "native scroll range accepts the end position");
        }
        Tick();
        Save("reveal");
    }
    public RetainedPress RetainPress(ProbeNode node) => native?.RetainPress(node.Id) ?? new(
        () => Action(node, "press"), () => { });
    public void Press(string role, string name)
    {
        Require(Action(Find(role, name), "press"), $"Press rejected: {role} '{name}'");
        Tick();
        Save("press");
    }
    public void Set(string name, string value)
    {
        Require(Action(Find("AXTextField", name), "set", value), $"SetValue rejected: '{name}'");
        Tick();
        Save("set");
        Require(Find("AXTextField", name).Value == value, $"SetValue did not update '{name}'");
    }
    /// <summary>Renders, then lets commands started by that render (such as company directory searches) finish and render.</summary>
    public void Tick()
    {
        Render();
        if (program.PendingCommands == 0)
            return;
        var waited = Stopwatch.StartNew();
        while (program.PendingCommands > 0 && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (app is not null)
                app.RunFor(TimeSpan.FromMilliseconds(20));
            else
                Thread.Sleep(5);
        }
        Render();
    }
    void Render()
    {
        if (app is not null)
        {
            app.RunFor(TimeSpan.FromMilliseconds(80));
            // Hidden windows deliberately suppress scheduled rendering. Honor only
            // requested frames so popovers can finish their initial measurement.
            for (var i = 0; i < 4 && window!.Context!.NextFrame is { } delay; i++)
            {
                app.RunFor(delay);
                window.Invalidate();
                app.RunFor(TimeSpan.FromMilliseconds(20));
            }
        }
        else
            tester!.Settle();
    }
    public void External(string client)
    {
        using var child = Process.Start(new ProcessStartInfo(client)
        {
            ArgumentList = { Environment.ProcessId.ToString(CultureInfo.InvariantCulture) },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        while (!child.HasExited && elapsed.Elapsed < TimeSpan.FromSeconds(10))
            app!.RunFor(TimeSpan.FromMilliseconds(100));
        if (!child.HasExited)
        {
            child.Kill();
            throw new InvalidOperationException("External AX client timed out");
        }
        var json = stdout.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "external.json"), json);
        File.WriteAllText(Path.Combine(output, "external.stderr.txt"), stderr.GetAwaiter().GetResult());
        Require(child.ExitCode == 0, "External client exits successfully");
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.GetProperty("trusted").GetBoolean())
            throw new ProbeBlockedException("External AX client lacks existing accessibility permission; no prompt was requested");
        if (!document.RootElement.GetProperty("nodes").EnumerateArray().Any(n => n.GetProperty("role").GetString() == "AXButton"))
            throw new ProbeBlockedException("Trusted external AX client sees no controls in the hidden app; visible-window behavior remains unverified");
    }
    public void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    public void Save(string label, bool screenshot = false)
    {
        var file = Path.Combine(output, $"{step++:D2}-{label}");
        if (screenshot)
        {
            if (window is not null)
                Png.Write(file + ".png", window.Capture());
            else
                tester!.Screenshot(file + ".png");
        }
        File.WriteAllText(file + ".json", JsonSerializer.Serialize(Nodes(), ProbeJson.Default.ProbeNodeArray));
    }
    public void Dispose()
    {
        File.WriteAllText(Path.Combine(output, "actions.json"), JsonSerializer.Serialize(actions, ProbeJson.Default.ListProbeAction));
        tester?.Dispose();
        window?.Dispose();
        app?.Dispose();
        program.Dispose();
    }
}

sealed record ProbeNode(string Id, string Parent, string Role, string Name, string? Value, bool Enabled, bool? Expanded, string? Help);
sealed record ProbeResult(string Provider, string Scenario, string Status, string Detail);
sealed class ProbeBlockedException(string message) : Exception(message);
sealed record ProbeAction(string Id, string Role, string Name, string Action, string? Value, bool? ReportedSuccess);
sealed class RetainedPress(Func<bool> invoke, Action release) : IDisposable
{
    public bool Invoke() => invoke();
    public void Dispose() => release();
}
[JsonSerializable(typeof(ProbeNode[]))]
[JsonSerializable(typeof(List<ProbeResult>))]
[JsonSerializable(typeof(List<ProbeAction>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
partial class ProbeJson : JsonSerializerContext;
