using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

string Option(string name, string fallback) => args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..] ?? fallback;
string output = Path.GetFullPath(Option("--output", "artifacts/footprint"));
Directory.CreateDirectory(output);
if (Option("--native", "") is not { Length: > 0 } nativeOption || Option("--managed", "") is not { Length: > 0 } managedOption)
{
    Console.Error.WriteLine("Usage: Kumiko.Footprint --native=<published AOT app> --managed=<published app>|skip [--zig=<app>] [--child-arg=<arg>...] [--scenario=<text>] [--output=<folder>]");
    return 2;
}
string native = Path.GetFullPath(nativeOption);
string managed = Path.GetFullPath(managedOption);
string dotnetRoot = Option("--dotnet-root", Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? "");
var results = new List<ModeResult>();
var modes = new List<(string Mode, string Executable)>
{
    ("NativeAOT", native)
};
if (Option("--managed", "") != "skip")
    modes.Add(("Framework-dependent Release", managed));
if (Option("--zig", "") is { Length: > 0 } zig)
    modes.Add(("Zig ReleaseFast", Path.GetFullPath(zig)));
foreach (var (mode, executable) in modes)
{
    var runs = new List<RunResult>();
    for (int run = 1; run <= 3; run++)
    {
        string prefix = mode switch
        {
            "NativeAOT" => "aot",
            "Zig ReleaseFast" => "zig",
            _ => "jit"
        };
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in args.Where(a => a.StartsWith("--child-arg=", StringComparison.Ordinal)))
            start.ArgumentList.Add(argument[12..]);
        start.ArgumentList.Add("--probe");
        start.ArgumentList.Add("--snapshot=" + Path.Combine(output, $"{prefix}-{run}.png"));
        if (dotnetRoot.Length > 0)
            start.Environment["DOTNET_ROOT"] = dotnetRoot;
        var startup = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string? frame = await process.StandardOutput.ReadLineAsync(timeout.Token);
            double externalStartup = startup.Elapsed.TotalMilliseconds;
            if (frame?.StartsWith("FRAME ", StringComparison.Ordinal) != true)
                throw new IOException("No rendered frame: " + frame);
            string? ready = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (ready?.StartsWith("READY ", StringComparison.Ordinal) != true)
                throw new IOException("Probe did not become ready: " + ready);
            var fields = ready.Split(' ');
            double frameMs = double.Parse(frame[6..], CultureInfo.InvariantCulture);
            long managedHeap = long.Parse(fields[2], CultureInfo.InvariantCulture);
            var rss = new List<long>();
            process.Refresh();
            var cpuStart = process.TotalProcessorTime;
            var idle = Stopwatch.StartNew();
            for (int i = 0; i < 4; i++)
            {
                process.Refresh();
                rss.Add(process.WorkingSet64);
                await Task.Delay(250);
            }

            process.Refresh();
            double idleCpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / idle.Elapsed.TotalMilliseconds * 100;
            long? physical = null, peak = null;
            if (OperatingSystem.IsMacOS())
            {
                string json = Path.Combine(output, $"{prefix}-{run}-footprint.json");
                string raw = await Execute(
                    "/usr/bin/footprint",
                    "-p",
                    process.Id.ToString(CultureInfo.InvariantCulture),
                    "-f",
                    "bytes",
                    "--noCategories",
                    "-j",
                    json
                );
                File.WriteAllText(Path.Combine(output, $"{prefix}-{run}-footprint.txt"), raw);
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                var p = doc.RootElement.GetProperty("processes")[0];
                physical = p.GetProperty("auxiliary").GetProperty("phys_footprint").GetInt64();
                peak = p.GetProperty("auxiliary").GetProperty("phys_footprint_peak").GetInt64();
                File.WriteAllText(
                    Path.Combine(output, $"{prefix}-{run}-vmmap.txt"),
                    await Execute("/usr/bin/vmmap", "-summary", process.Id.ToString(CultureInfo.InvariantCulture))
                );
            }

            rss.Sort();
            var result = new RunResult(process.Id, externalStartup, frameMs, rss[rss.Count / 2], physical, peak, managedHeap, idleCpu);
            runs.Add(result);
            Console.WriteLine(FormattableString.Invariant($"{prefix} {run}: RSS {result.ResidentBytes / 1048576d:F2} MiB, footprint {physical / 1048576d:F2} MiB, idle CPU {idleCpu:F2}%, first frame {externalStartup:F1} ms"));
        }
        finally
        {
            // Only the dedicated child created above is terminated. No windows or other processes are controlled.
            if (!process.HasExited)
                process.Kill();
            await process.WaitForExitAsync();
            File.WriteAllText(Path.Combine(output, $"{prefix}-{run}-stderr.txt"), await stderr);
        }
    }

    var directory = Path.GetDirectoryName(executable)!;
    long deployed = mode == "Zig ReleaseFast" ? new FileInfo(executable).Length : Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Where(p => !p.Contains(".dSYM" + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !p.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
        .Sum(p => new FileInfo(p).Length);
    var resultMode = new ModeResult(mode, new FileInfo(executable).Length, deployed, runs.ToArray());
    results.Add(resultMode);
}

long? runtimeBytes = dotnetRoot.Length > 0 ? Directory.EnumerateFiles(Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App"), "*", SearchOption.AllDirectories)
    .Sum(p => new FileInfo(p).Length) : null;
var report = new FootprintReport(
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(),
    Option("--scenario", "unnamed scenario") + "; real native window, hidden; offscreen Metal frames at native backing scale; sampled after five seconds; three independent launches; no forced GC. RSS includes resident shared pages. Physical footprint is the macOS charge; peak includes startup. Binary sizes exclude debug symbols. Framework-dependent deployment additionally requires the shared .NET runtime.",
    runtimeBytes,
    results.ToArray()
);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, FootprintJson.Default.FootprintReport));
if (OperatingSystem.IsMacOS())
    File.WriteAllText(
        Path.Combine(output, "hardware.txt"),
        await Execute("/usr/sbin/sysctl", "-n", "hw.model", "machdep.cpu.brand_string", "hw.memsize", "hw.ncpu")
    );
Console.WriteLine(Path.Combine(output, "report.json"));
return 0;
static async Task<string> Execute(string file, params string[] args)
{
    var start = new ProcessStartInfo(file)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    foreach (var arg in args)
        start.ArgumentList.Add(arg);
    using var process = Process.Start(start) ?? throw new IOException(file);
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    string text = await output + await error;
    if (process.ExitCode != 0)
        throw new IOException(file + ": " + text);
    return text;
}

record RunResult(
    int Pid,
    double ProcessStartToFirstFrameMs,
    double MainToFirstFrameMs,
    long ResidentBytes,
    long? PhysicalFootprintBytes,
    long? PeakPhysicalFootprintBytes,
    long ManagedHeapBytes,
    double IdleCpuPercent
);
record ModeResult(string Mode, long ExecutableBytes, long DeploymentBytes, RunResult[] Runs);
record FootprintReport(string OS, string Architecture, string Method, long? SharedRuntimeBytes, ModeResult[] Modes);
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FootprintReport))]
partial class FootprintJson : JsonSerializerContext;
