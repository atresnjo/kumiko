using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

static class RouteGeneratorChecks
{
    const string Header = "using Kumiko.Runtime; using Kumiko.UI; public sealed record State; public sealed record Event; ";
    const string Routes = "[RouteUnion(\"screen\")] public abstract partial record Destination { [Route(\"items\", \"items\", 0)] public sealed record Items : Destination; [Route(\"items/{Id}\", \"item\", 1)] public sealed record Item(string Id) : Destination; } ";
    const string Items = "public sealed class ItemsPage(State model, Destination.Items route) : View<State, Event>(model) { protected override void Render() => C.Text(\"Items\"); } ";
    const string Item = "public sealed class ItemPage(State model, Destination.Item route) : View<State, Event>(model) { protected override void Render() => C.Text(route.Id); } ";

    public static async Task Run(string output, IEnumerable<MetadataReference> references)
    {
        var valid = Routes + Items + Item;
        var cases = new (string Name, string Source, string? Error)[]
        {
            ("missing-page", Routes + Items, "KUM014"),
            ("duplicate-page", valid + Item.Replace("ItemPage", "OtherItemPage", StringComparison.Ordinal), "KUM015"),
            ("missing-template", valid.Replace("[Route(\"items/{Id}\", \"item\", 1)]", "", StringComparison.Ordinal), "KUM016"),
            ("duplicate-kind", valid.Replace("\"item\", 1", "\"item\", 0", StringComparison.Ordinal), "KUM016"),
            ("duplicate-tag", valid.Replace("\"item\", 1", "\"items\", 1", StringComparison.Ordinal), "KUM016"),
            ("wrong-parameter", valid.Replace("{Id}", "{Missing}", StringComparison.Ordinal), "KUM016"),
            ("overlap", valid.Replace("\"items\", \"items\"", "\"items/fixed\", \"items\"", StringComparison.Ordinal), "KUM016"),
            ("unsupported-payload", valid.Replace("string Id", "System.DateTime Id", StringComparison.Ordinal).Replace("C.Text(route.Id)", "C.Text(route.Id.ToString())", StringComparison.Ordinal), "KUM016"),
            ("integer", valid.Replace("string Id", "int Id", StringComparison.Ordinal).Replace("C.Text(route.Id)", "C.Text(route.Id.ToString())", StringComparison.Ordinal) +
                " public static class Probe { public static bool Run() => DestinationRoutes.Parse(DestinationRoutes.Format(new Destination.Item(-2147483648))) == new Destination.Item(-2147483648) && DestinationRoutes.Parse(\"items/2147483648\") is null && DestinationRoutes.Parse(\"items/no\") is null; }", null),
            ("long", valid.Replace("string Id", "long Id", StringComparison.Ordinal).Replace("C.Text(route.Id)", "C.Text(route.Id.ToString())", StringComparison.Ordinal) +
                " public static class Probe { public static bool Run() => DestinationRoutes.Parse(DestinationRoutes.Format(new Destination.Item(long.MaxValue))) == new Destination.Item(long.MaxValue) && DestinationRoutes.Parse(\"items/9223372036854775808\") is null; }", null),
            ("guid", valid.Replace("string Id", "System.Guid Id", StringComparison.Ordinal).Replace("C.Text(route.Id)", "C.Text(route.Id.ToString())", StringComparison.Ordinal) +
                " public static class Probe { public static bool Run() => DestinationRoutes.Parse(DestinationRoutes.Format(new Destination.Item(System.Guid.Empty))) == new Destination.Item(System.Guid.Empty) && DestinationRoutes.Parse(\"items/no\") is null; }", null),
            ("valid", valid + " public static class Probe { public static bool Run() { var id = \"a/b % 東京\"; var route = new Destination.Item(id); return DestinationRoutes.Parse(DestinationRoutes.Format(route)) == route && DestinationRoutes.Parse(\"/items\") is Destination.Items && DestinationRoutes.Parse(\"items/x/extra\") is null && DestinationRoutes.Parse(\"items/%GG\") is null && DestinationRoutes.Parse(\"items/x?q=1\") is null && DestinationRoutes.Parse(\"https://host/items\") is null && DestinationRoutes.Parse(\"items/\") is null && DestinationRoutes.Parse(\"items/%\") is null && DestinationRoutes.Kind(route) == DestinationKind.Item && DestinationRoutes.Root(DestinationKind.Items) is Destination.Items; } }", null)
        };
        var host = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "../../..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var report = new List<string>();
        foreach (var item in cases)
        {
            var directory = Path.GetFullPath(Path.Combine(output, "route-" + item.Name));
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "fixture.cs.txt");
            File.WriteAllText(source, Header + item.Source);
            var sources = Path.Combine(directory, "sources.txt");
            var refs = Path.Combine(directory, "references.txt");
            var generated = Path.Combine(directory, "generated.cs.txt");
            File.WriteAllLines(sources, [source]);
            File.WriteAllLines(refs, references.Select(r => r.Display!).Where(File.Exists));
            var start = new ProcessStartInfo(host) { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var argument in new[] { typeof(Kumiko.Analyzers.KumikoAnalyzer).Assembly.Location, sources, refs, generated })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new Exception("Could not start route generator");
            var error = process.StandardError.ReadToEndAsync();
            var standard = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var log = await standard + await error;
            File.WriteAllText(Path.Combine(directory, "build.log"), log);
            if (item.Error is { } diagnostic)
            {
                if (process.ExitCode == 0 || !log.Contains("error " + diagnostic, StringComparison.Ordinal))
                    throw new Exception($"Route {item.Name} expected {diagnostic}: {log}");
            }
            else
            {
                if (process.ExitCode != 0)
                    throw new Exception(log);
                var compilation = CSharpCompilation.Create("RouteProbe", [CSharpSyntaxTree.ParseText(File.ReadAllText(source)), CSharpSyntaxTree.ParseText(File.ReadAllText(generated))], references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var bytes = new MemoryStream();
                var result = compilation.Emit(bytes);
                if (!result.Success)
                    throw new Exception(string.Join('\n', result.Diagnostics));
                var assembly = System.Reflection.Assembly.Load(bytes.ToArray());
                if (assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null) is not true)
                    throw new Exception("Generated route parsing/formatting lost or accepted an invalid target");
            }
            report.Add($"PASS {item.Name}: {item.Error ?? "compiled and executed"}");
        }
        File.WriteAllLines(Path.Combine(output, "route-generator-report.txt"), report);
        Console.WriteLine($"PASS: {cases.Length} route build-generator E2E fixtures");
    }
}
