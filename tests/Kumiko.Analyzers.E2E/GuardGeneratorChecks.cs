using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

static class GuardGeneratorChecks
{
    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        const string header = "using Kumiko.Runtime; using Kumiko.UI; public sealed record State; public sealed record Event; ";
        const string app = "public partial class Fixture : IKumiko<State, Event> { public static State Init => new(); public static (State, Cmd<Event>) Update(State s, Event e) => (s, Cmd<Event>.None); ";
        const string view = "public sealed class Screen(State model) : Screen<State, Event>(model) { protected override void Render() {} }";
        const string sync = "public static RouteDecision<string> BeforeEnter(State m, string route) => new RouteDecision<string>.Allow();";
        const string async = "public static Cmd<RouteGuardReply<string>> BeforeEnterAsync(State m, RouteGuardRequest<string> request) => Cmd<RouteGuardReply<string>>.None;";
        var cases = new (string Name, string Hooks, bool Valid)[]
        {
            ("instance", sync.Replace("static ", "", StringComparison.Ordinal), false),
            ("wrong-model", sync.Replace("State m", "Event m", StringComparison.Ordinal), false),
            ("wrong-result", sync.Replace("RouteDecision<string> BeforeEnter", "object BeforeEnter", StringComparison.Ordinal), false),
            ("nullable-result", sync.Replace("RouteDecision<string> BeforeEnter", "RouteDecision<string>? BeforeEnter", StringComparison.Ordinal), false),
            ("optional-route", sync.Replace("string route)", "string route = \"home\")", StringComparison.Ordinal), false),
            ("property", "public static RouteDecision<string> BeforeEnter => new RouteDecision<string>.Allow();", false),
            ("overloaded", sync + sync.Replace("string route", "int route", StringComparison.Ordinal), false),
            ("async-wrong-request", async.Replace("RouteGuardRequest<string> request", "string request", StringComparison.Ordinal), false),
            ("async-nullable-request", async.Replace("RouteGuardRequest<string> request", "RouteGuardRequest<string>? request", StringComparison.Ordinal), false),
            ("async-wrong-reply", async.Replace("RouteGuardReply<string>", "RouteGuardReply<int>", StringComparison.Ordinal), false),
            ("different-route-types", sync + async.Replace("<string>", "<int>", StringComparison.Ordinal), false),
            ("sync", sync, true),
            ("async-only", async, true),
            ("both", sync + async, true)
        };
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var report = new List<string>();
        foreach (var item in cases)
        {
            var directory = Path.GetFullPath(Path.Combine(output, "guard-generator-" + item.Name));
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "fixture.cs.txt");
            File.WriteAllText(source, header + app + item.Hooks + "}" + view);
            var sources = Path.Combine(directory, "sources.txt");
            var refs = Path.Combine(directory, "references.txt");
            var generated = Path.Combine(directory, "generated.cs.txt");
            File.WriteAllLines(sources, [source]);
            File.WriteAllLines(refs, references.Select(r => r.Display!).Where(File.Exists));
            var start = new ProcessStartInfo(host) { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { typeof(Kumiko.Analyzers.KumikoAnalyzer).Assembly.Location, sources, refs, generated })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new Exception("Could not start guard generator");
            var error = process.StandardError.ReadToEndAsync();
            var standard = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var log = await standard + await error;
            File.WriteAllText(Path.Combine(directory, "build.log"), log);
            if (!item.Valid)
            {
                if (process.ExitCode == 0 || !log.Contains("error KUM017", StringComparison.Ordinal))
                    throw new Exception($"Invalid guard {item.Name} was accepted: {log}");
            }
            else
            {
                if (process.ExitCode != 0)
                    throw new Exception(log);
                // Compile the generated guard adapters here. The navigation E2E app builds
                // the full generated app and its System.Text.Json source generator output.
                var adapters = CSharpSyntaxTree.ParseText(File.ReadAllText(generated)).GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().First(c => c.Identifier.ValueText == "Fixture").ToFullString();
                var compilation = CSharpCompilation.Create("GuardProbe", [CSharpSyntaxTree.ParseText(File.ReadAllText(source).Replace(": IKumiko<State, Event>", "", StringComparison.Ordinal)), CSharpSyntaxTree.ParseText(adapters)], references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
                if (errors.Length != 0 || !File.ReadAllText(generated).Contains("GuardNavigation", StringComparison.Ordinal))
                    throw new Exception("Guard adapters failed to compile: " + string.Join('\n', errors.Select(e => e.ToString())));
            }
            report.Add($"PASS {item.Name}: {(item.Valid ? "compiled typed adapters" : "KUM017")}");
        }
        File.WriteAllLines(Path.Combine(output, "guard-generator-report.txt"), report);
        foreach (var (name, hook) in new[]
        {
            ("sync-io", sync.Replace("=> new RouteDecision<string>.Allow();", "{ System.IO.File.ReadAllText(\"forbidden\"); return new RouteDecision<string>.Allow(); }", StringComparison.Ordinal)),
            ("async-factory-io", async.Replace("=> Cmd<RouteGuardReply<string>>.None;", "{ System.IO.File.ReadAllText(\"forbidden\"); return Cmd<RouteGuardReply<string>>.None; }", StringComparison.Ordinal))
        })
        {
            var source = header + app + hook + "public static IProgram Create() => throw new System.NotSupportedException(); public static void View(Context c, State m, Dispatch<Event> send) {} }";
            File.WriteAllText(Path.Combine(output, "guard-" + name + ".cs.txt"), source);
            var compilation = CSharpCompilation.Create("ImpureGuard", [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new Kumiko.Analyzers.KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
            if (!diagnostics.Any(d => d.Id == "KUM006"))
                throw new Exception("An unused impure guard was silently accepted: " + name);
        }
        const string direct = "using Kumiko.Runtime; using System.Collections.Immutable; sealed record Entry(int Id, string Route) : INavigationEntry<string, Entry> { public static Entry Create(int id, string route) => new(id, route); public static bool IsValidPath(ImmutableArray<Entry> entries) => true; } static class Probe { public static void Check() { new NavigationGuard<string, Entry>().Resolve(0, NavigationPath<string, Entry>.Start(\"home\"), new RouteGuardReply<string>(\"main\", 1, 0, RouteAdmission.Allow), \"main\", static (_, _) => { System.Console.WriteLine(); return new RouteDecision<string>.Allow(); }); } }";
        File.WriteAllText(Path.Combine(output, "guard-direct-resolve.cs.txt"), direct);
        var directCompilation = CSharpCompilation.Create("DirectGuard", [CSharpSyntaxTree.ParseText(direct)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var directDiagnostics = await directCompilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new Kumiko.Analyzers.KumikoAnalyzer())).GetAnalyzerDiagnosticsAsync();
        if (!directDiagnostics.Any(d => d.Id == "KUM006"))
            throw new Exception("Direct Resolve accepted an impure final-admission callback");
        Console.WriteLine($"PASS: {cases.Length} guard build-generator E2E fixtures");
    }
}
