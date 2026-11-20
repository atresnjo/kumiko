using System.Text.Json;
using DiffViewer.E2E;

var output = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "artifacts/diffviewer/e2e");
Directory.CreateDirectory(output);
var h = new Harness(output);
bool Only(string name) => args.Contains("--" + name + "-only", StringComparer.Ordinal);
bool all = !args.Any(a => a.EndsWith("-only", StringComparison.Ordinal));

if (all || Only("diff"))
    DiffChecks.Run(h.Check, h.Timing);
if (all || Only("lexer"))
    LexerChecks.Run(h.Check, h.Timing);
if (all || Only("sources"))
    SourceChecks.Run(h);
if (all || Only("remote"))
    RemoteChecks.Run(h);
if (all || Only("view"))
    ViewChecks.Run(h);
if (all || Only("selection"))
    SelectionChecks.Run(h);
if (all || Only("zen"))
    ZenChecks.Run(h);
if (all || Only("projects"))
    QuickOpenChecks.Run(h);
if (all || Only("history"))
    HistoryChecks.Run(h);
if (all || Only("context"))
    RepoContextChecks.Run(h);
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
Console.WriteLine($"PASS: {h.Checks.Count} diff viewer checks; artifacts: {output}");
