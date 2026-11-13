using System.Text.Json;
using DiskMap.E2E;

var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/diskmap/e2e");
Directory.CreateDirectory(output);
var h = new Harness(output);
bool Only(string name) => args.Contains("--" + name + "-only", StringComparer.Ordinal);
bool all = !args.Any(a => a.EndsWith("-only", StringComparison.Ordinal));

if (all || Only("scan"))
    ScanChecks.Run(h);
if (all || Only("layout"))
    LayoutChecks.Run(h);
if (all || Only("view"))
    ViewChecks.Run(h);
if (all || Only("perf"))
    PerfChecks.Run(h);

var report = new Dictionary<string, object>
{
    ["checks"] = h.Checks,
    ["timings"] = h.Timings,
    ["png"] = h.Hashes,
    ["runtime"] = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT"
};
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, ReportJson.Default.DictionaryStringObject));
Console.WriteLine($"PASS: {h.Checks.Count} disk map checks; artifacts: {output}");
