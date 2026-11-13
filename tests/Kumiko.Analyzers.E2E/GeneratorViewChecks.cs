using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

static class GeneratorViewChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        const string header = "using Kumiko.Runtime; using Kumiko.UI; public sealed record State(int Count = 0); public sealed record Event; ";
        const string app = "public partial class Fixture : IKumiko<State, Event> { public static State Init => new(); public static (State, Cmd<Event>) Update(State s, Event e) => (s, Cmd<Event>.None); ";
        const string view = "public sealed class Page(State model) : Screen<State, Event>(model) { protected override void Render() {} }";
        const string root = "public static NavigationRoot<Event> Root(State model) => NavigationRoot<Event>.Create(NavigationHistory<string>.Start(\"home\"), \"app\", _ => new Page(model).Map<Event>(static m => m), static _ => new Event()); }";
        var cases = new (string Name, string Source, bool Reject)[]
        {
            ("legacy", app + "public static void View(Ui<Event> ui, State model) {} }", true),
            ("missing", app + "}", false), // The IKumiko.Root compiler contract diagnoses this.
            ("explicit", app + root + view, false),
            ("multiple-views", app + root.Replace("new Page", "new OtherPage", StringComparison.Ordinal) + view + view.Replace("class Page", "class OtherPage", StringComparison.Ordinal), false)
        };
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var generator = typeof(Kumiko.Analyzers.KumikoAnalyzer).Assembly.Location;
        var report = new List<string>();
        foreach (var item in cases)
        {
            var directory = Path.Combine(output, "generator-" + item.Name);
            Directory.CreateDirectory(directory);
            var source = Path.GetFullPath(Path.Combine(directory, "fixture.cs.txt"));
            File.WriteAllText(source, header + item.Source);
            var sources = Path.GetFullPath(Path.Combine(directory, "sources.txt"));
            var refs = Path.GetFullPath(Path.Combine(directory, "references.txt"));
            File.WriteAllLines(sources, [source]);
            File.WriteAllLines(refs, references.Select(r => r.Display!).Where(File.Exists));
            var start = new ProcessStartInfo(host) { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { generator, sources, refs, Path.GetFullPath(Path.Combine(directory, "generated.cs.txt")) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new Exception("Could not start the build generator");
            var error = process.StandardError.ReadToEndAsync();
            var standard = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var log = await standard + await error;
            File.WriteAllText(Path.Combine(directory, "build.log"), log);
            if (item.Reject ? process.ExitCode == 0 || !log.Contains("error KUM011", StringComparison.Ordinal) : process.ExitCode != 0)
                throw new Exception($"Invalid {item.Name} root was accepted: {log}");
            if (!item.Reject)
            {
                var generated = File.ReadAllText(Path.Combine(directory, "generated.cs.txt"));
                if (!generated.Contains("Update, Root,", StringComparison.Ordinal) || generated.Contains(".Render()", StringComparison.Ordinal))
                    throw new Exception("Generator bypassed explicit typed root construction");
            }
            report.Add($"PASS {item.Name}: {(item.Reject ? "KUM011" : "explicit Root wiring")}");
        }
        File.WriteAllLines(Path.Combine(output, "generator-views-report.txt"), report);
        Console.WriteLine($"PASS: {cases.Length} root-view build-generator E2E fixtures");
    }
}
