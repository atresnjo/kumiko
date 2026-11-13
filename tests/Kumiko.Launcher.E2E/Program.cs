using System.Security.Cryptography;
using System.Text.Json;

if (InstanceFlows.Child(args))
    return;
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/launcher");
Directory.CreateDirectory(output);
var report = Path.Combine(output, "report.json");
File.Delete(report);
var checks = new List<string>();
void Check(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
    checks.Add(description);
}
BehaviorFlows.Run(output, Check);
AsyncFlows.Run(output, Check);
NativeFlows.Run(output, Check);
ReviewPanelFlows.Run(output, Check);
ReviewLaunchFlows.Run(output, Check);
ReviewGeometryFlows.Run(output, Check);
InstanceFlows.Run(Check);
using (var stream = File.Create(report))
using (var json = new Utf8JsonWriter(stream, new()
{
    Indented = true
}))
{
    json.WriteStartObject();
    json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
    json.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
    json.WriteString("fixtureEvidence", "Real example view, update and controller; deterministic catalog, launch, login and shortcut backends. No desktop publication.");
    json.WriteString("nativeEvidence", "Hidden native window geometry/lifetime and actual host quit/veto/retry; private shortcut dispatch; temporary XDG ownership; real child-process forwarding. On macOS: NSPanel eligibility/collection behavior, NSBundle fixture metadata/icons, private Objective-C startup ABI and delayed open-application callbacks after finishLaunching. Windows coordinate policy and scaled rendering use fixtures on non-Windows hosts.");
    json.WriteString("unverified", "Real hotkeys, trays, application activation/focus/menu restoration, full-screen/Spaces presentation, actual installed catalogs, login registration/approval and real login launch. Other OS native execution is required.");
    json.WriteNumber("passed", checks.Count);
    json.WriteStartArray("checks");
    foreach (var description in checks)
        json.WriteStringValue(description);
    json.WriteEndArray();
    json.WriteStartObject("sha256");
    foreach (var path in Directory.EnumerateFiles(output).Where(p => p.EndsWith(".png", StringComparison.Ordinal) || p.EndsWith(".txt", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        json.WriteString(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    json.WriteEndObject();
    json.WriteEndObject();
}
Console.WriteLine($"{checks.Count} launcher checks passed; artifacts: {output}");
