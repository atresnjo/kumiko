using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.UI;
using TraceWaterfall;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/tracewaterfall/e2e");
Directory.CreateDirectory(output);
var checks = new List<string>();
var hashes = new Dictionary<string, string>();
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
void Artifact(Tester tester, string name)
{
    tester.Context.Blur();
    tester.MoveTo(0, 0);
    tester.Settle();
    var stem = Path.Combine(output, name);
    tester.Screenshot(stem + ".png");
    File.WriteAllText(stem + ".txt", tester.Dump());
    tester.WriteWarnings(stem + ".warnings.json");
    Check(tester.Context.Warnings.Count == 0, name + " has no layout, identity or contrast warnings");
    hashes[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stem + ".png")));
}

using var program = TraceApp.CreateProgram(TraceApp.Init with { Playing = false });
using var tester = new Tester(program, new(1360, 1040, DevMode: true));
tester.Context.ReduceMotion = true;
tester.Frame(0);
Artifact(tester, "live");
var now = program.Model.Now;
program.Send(new TraceMsg.Tick());
tester.Frame(0);
Check(program.Model.Now == now && !TraceApp.Subscriptions(program.Model).Any(), "pause ignores queued ticks and removes the timer");
tester.Get(Role.Button, "Resume").Click();
Check(TraceApp.Subscriptions(program.Model).Count() == 1, "resume installs one live timer");
program.Send(new TraceMsg.Tick());
tester.Frame(0);
Check(program.Model.Now > now && tester.HasText("Live"), "live tick advances the rendered trace");
tester.Get(Role.Button, "Pause").Click();
tester.Get(Role.Button, "Show completed trace").Click();
Check(program.Model.Now == 92000 && !TraceApp.Subscriptions(program.Model).Any(), "completed trace stops its timer at the final span");
Check(tester.HasText("Run checkout tests") && tester.HasText("Fix coupon handling") && tester.HasText("SLOWEST"),
    "completed waterfall retains the failure, repair and slowest finished call");
Artifact(tester, "light");
var rowCount = tester.Context.Accessibility.Count(n => n.Role == Role.TreeItem);
tester.Screenshot(Path.Combine(output, "light-repeat.png"));
Check(hashes["light"] == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "light-repeat.png")))),
    "unchanged offscreen rendering produces the same PNG hash");

tester.Get(Role.Button, "Collapse Researcher").Click();
Check(!tester.HasText("Linear pricing") && tester.HasText("Read brand voice") && tester.HasText("Run checkout tests"),
    "folding the researcher hides only its descendants");
Artifact(tester, "collapsed");
tester.Get(Role.Button, "Collapse Orchestrator").Click();
Check(!tester.HasText("Researcher") && tester.HasText("Orchestrator"), "folding the root hides all descendants");
tester.Get(Role.Button, "Expand Orchestrator").Click();
Check(!tester.HasText("Linear pricing") && tester.HasText("Researcher"), "opening the root preserves the researcher's nested fold");
tester.Get(Role.Button, "Expand Researcher").Click();
Check(tester.Context.Accessibility.Count(n => n.Role == Role.TreeItem) == rowCount && tester.HasText("Linear pricing"),
    "opening a branch restores every child row");

tester.Get(Role.TreeItem, "Run checkout tests").Click();
tester.Settle();
Check(tester.HasText("pnpm test checkout") && tester.ContainsText("coupon codes aren't applied to seat prices"),
    "failed span inspector shows its input and actionable error");
Artifact(tester, "failed-detail");
tester.Key(Key.Escape);
tester.Settle();
Check(program.Model.OpenId is null && !tester.HasText("pnpm test checkout"), "Escape dismisses details");

tester.Get(Role.TreeItem, "Researcher").Click();
tester.Key(Key.Escape);
tester.Key(Key.Left);
Check(program.Model.Collapsed.Contains("research"), "Left folds the keyboard-focused branch");
tester.Key(Key.Right);
Check(!program.Model.Collapsed.Contains("research"), "Right opens the keyboard-focused branch");
tester.Key(Key.Down);
tester.Key(Key.Down);
tester.Key(Key.Enter);
tester.Settle();
Check(program.Model.OpenId == "notion" && tester.HasText("Notion pricing plans"),
    "Down traverses identically named search rows and Enter opens the second call's own details");
tester.Key(Key.Escape);
tester.Get(Role.Button, "Dark theme").Click();
Check(program.Model.Dark && program.Model.Now == 92000, "theme switching preserves the trace clock");
Artifact(tester, "dark");
tester.Resize(920, 720);
Artifact(tester, "compact");
tester.Resize(1360, 1040);

var session = Path.Combine(output, "session.jsonl");
program.Save(session);
Artifact(tester, "recorded");
using (var replay = TraceApp.CreateProgram())
using (var replayTester = new Tester(replay, new(1360, 1040, DevMode: true)))
{
    replayTester.Context.ReduceMotion = true;
    replayTester.Replay(session);
    Artifact(replayTester, "replay");
    Check(hashes["recorded"] == hashes["replay"], "recorded Elm messages reproduce the same rendered PNG");
}
tester.Get(Role.Button, "Restart").Click();
Check(program.Model is { Now: 0, Playing: true, OpenId: null } && program.Model.Collapsed.Count == 0,
    "restart clears old UI state and starts the trace from zero");
tester.Get(Role.Button, "Pause").Click();
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new Report(checks, hashes), ReportJson.Default.Report));
Console.WriteLine($"{checks.Count} trace waterfall checks passed; artifacts: {output}");

sealed record Report(List<string> Checks, Dictionary<string, string> PngSha256);
[JsonSerializable(typeof(Report))]
partial class ReportJson : JsonSerializerContext;
