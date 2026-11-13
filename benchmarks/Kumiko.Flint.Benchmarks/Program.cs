using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Flint;
using Flint.Git;
using Flint.Services;
using Kumiko.UI;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/flint-performance");
Directory.CreateDirectory(output);
var measurements = new List<Measurement>();
using var fixture = new Fixture();
var registrations = new ServiceCollection();
registrations.AddSingleton<IGitService>(fixture.Git);
registrations.AddSingleton<IGitHubAccounts, NoAccounts>();
registrations.AddSingleton<ILauncher, NoLauncher>();
registrations.AddFlint(options =>
{
    options.StateDirectory = Path.Combine(fixture.Root, "state");
    options.DiscoveryRoots = [fixture.Root];
});
using var services = registrations.BuildServiceProvider(new ServiceProviderOptions() { ValidateScopes = true, ValidateOnBuild = true });
Measure("git-history-4000-cold", "Fresh Git service per load; includes process startup, status/refs/log and graph; warm OS page cache", 6,
    () => new GitService(fixture.Runner, Path.Combine(fixture.Root, "state")).Load(fixture.History, 4000, CancellationToken.None).GetAwaiter().GetResult());
fixture.Load(fixture.History);
Measure("git-history-4000-revalidate", "Existing Git service, unchanged HEAD/refs, reused graph", 10, () => fixture.Load(fixture.History));
var history = fixture.Load(fixture.History, 12000);
var small = fixture.Load(fixture.Small);
var historyTab = fixture.Tab(history);
var smallTab = fixture.Tab(small);
Frames("history-10000", Model(historyTab), 100);
var initialCommit = historyTab with
{
    Detail = historyTab.Detail! with
    {
        Files = [.. Enumerable.Range(0, 2000).Select(i => new FileChange($"src/Module{i / 100:D2}/File{i:D4}.cs", 'A'))]
    }
};
Frames("commit-files-2000", Model(initialCommit), 100);
var tabs = Model(historyTab) with
{
    Workspaces = [new("default", "Default", [history.Path, small.Path], history.Path)],
    Repositories = ImmutableDictionary<string, RepoTab>.Empty.Add(history.Path, historyTab).Add(small.Path, smallTab)
};
using (var app = FlintApp.CreateProgram(tabs, services))
using (var tester = new Tester(app, new(1400, 900)))
{
    try
    {
        tester.Context.ReduceMotion = true;
        var target = false;
        void Switch()
        {
            target = !target;
            tester.Get(Role.Button, "Repository: " + (target ? small.Name : history.Name)).Click();
            tester.Settle();

        }
        for (var i = 0; i < 4; i++)
            Switch();
        Measure("cached-tab-switch-10000", "Real Tester pointer click, pure Elm transition, and settled frames; includes runtime model serialization; does not await debounced workspace persistence", 30, Switch, tester);
    }
    finally
    {
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
var changes = fixture.Load(fixture.Changes);
Frames("changes-5000", Model(fixture.Tab(changes) with { Page = Page.Changes }), 30);
var diff = fixture.Load(fixture.Diff);
var file = diff.Unstaged.Single();
var patch = fixture.Git.Patch(diff.Path, null, file, false, CancellationToken.None).GetAwaiter().GetResult();
var diffTab = fixture.Tab(diff) with { Page = Page.Changes, File = file.Path, Patch = patch, Rows = Diff.Parse(patch) };
Frames("diff-10000", Model(diffTab), 100);
using (var app = FlintApp.CreateProgram(Model(diffTab with
{
    Finding = true
}), services))
using (var tester = new Tester(app, new(1400, 900)))
{
    try
    {
        tester.Context.ReduceMotion = true;
        var alternate = false;
        void Search()
        {
            alternate = !alternate;
            tester.Get(Role.TextField, "Find in diff").Fill(alternate ? "needle" : "line");
            tester.Settle();
        }
        Search();
        Search();
        Measure("diff-search-10000", "Real accessibility edit, Elm search update, result reveal and settled frames", 20, Search, tester);
    }
    finally
    {
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
IdleSample? idle = null;
var pidText = args.FirstOrDefault(a => a.StartsWith("--idle-pid=", StringComparison.Ordinal))?[11..];
if (int.TryParse(pidText, out var pid))
{
    using var process = Process.GetProcessById(pid);
    process.Refresh();
    var cpu = process.TotalProcessorTime;
    var started = Stopwatch.GetTimestamp();
    Thread.Sleep(5000);
    process.Refresh();
    var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
    var milliseconds = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    idle = new(pid, seconds, milliseconds, milliseconds / (seconds * 10), process.WorkingSet64);
}
var report = new Report(RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(), RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
    history.Commits.Length, changes.Unstaged.Length, diffTab.Lines.Length,
    "Single machine; offscreen CPU tree/build/layout/paint, not GPU presentation or visible latency. Allocations are current UI-thread only. "
    + "Git cold means fresh service, not cold filesystem cache. Forced frames do not represent idle wake rate. No original-GPUIX timing comparison. "
    + "Native CPU sampling uses the optional interactive preview; user activity and delayed file notifications are not controlled. No system input is sent. Raw samples retain outliers. Other machine workloads are uncontrolled.",
    [.. measurements], idle);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, PerfJson.Default.Report));
Console.WriteLine(Path.Combine(output, "report.json"));

FlintModel Model(RepoTab tab) => FlintApp.Init with
{
    Workspaces = [new("default", "Default", [tab.Snapshot.Path], tab.Snapshot.Path)],
    Repositories = ImmutableDictionary<string, RepoTab>.Empty.Add(tab.Snapshot.Path, tab),
    Watching = false
};
void Frames(string name, FlintModel model, int samples)
{
    using var app = FlintApp.CreateProgram(model, services);
    using var tester = new Tester(app, new(1400, 900));
    try
    {
        tester.Context.ReduceMotion = true;
        for (var i = 0; i < 15; i++)
            tester.Frame();
        Measure(name, "Forced offscreen UI frames, 1400x900 scale 1; rendering preparation only", samples, () => tester.Frame(), tester);
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
    }
    finally
    {
        app.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
void Measure(string name, string scope, int count, Action action, Tester? tester = null)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var raw = new double[count];
    var before = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < count; i++)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        raw[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    var allocation = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)count;
    var sorted = raw.Order().ToArray();
    var result = new Measurement(name, scope, count, sorted[count / 2], sorted[Math.Min(count - 1, (int)(count * .95))], sorted[^1],
        allocation, tester?.Context.Stats.Nodes ?? 0, tester?.Context.Stats.Instances ?? 0, raw);
    measurements.Add(result);
    Console.WriteLine($"{name}: median {result.MedianMs:F3} ms; p95 {result.P95Ms:F3} ms; {result.AllocatedBytesPerOperation:F0} B/op; {result.Nodes} nodes");
}
