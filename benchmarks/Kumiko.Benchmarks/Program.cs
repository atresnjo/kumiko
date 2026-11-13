using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Rendering;
using Kumiko.UI;

string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/benchmarks.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var results = new List<Result>();
using var metal = OperatingSystem.IsMacOS() ? new MetalRenderer() : null;
foreach (var scene in new (string, Action<Context>)[]
{
    ("boxes", Scenes.Boxes),
    ("text", Scenes.Text),
    ("scroll", Scenes.Scroll),
    ("list", Scenes.List),
    ("dashboard", Scenes.Dashboard),
    ("retained-pages", NavigationScene.Render)
}

)
{
    using var tester = new Tester(scene.Item2, new(Width: 1200, Height: 800, Scale: 1));
    for (int i = 0; i < 40; i++)
        tester.Frame();
    const int iterations = 300;
    var samples = new double[iterations];
    var builds = new double[iterations];
    var layouts = new double[iterations];
    var paints = new double[iterations];
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        tester.Frame();
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var stats = tester.Context.Stats;
        builds[i] = stats.BuildMilliseconds;
        layouts[i] = stats.LayoutMilliseconds;
        paints[i] = stats.PaintMilliseconds;
    }

    var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
    var draws = new double[30];
    if (metal is not null)
        for (int i = 0; i < draws.Length; i++)
        {
            var c = tester.Context;
            var start = Stopwatch.GetTimestamp();
            metal.Render(c.Instances, c.TextSystem.Atlas, c.Size, c.Scale, c.Theme.Background);
            draws[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

    var result = new Result(
        scene.Item1,
        Median(samples),
        Percentile(samples, .95),
        Median(builds),
        Median(layouts),
        Median(paints),
        bytes,
        tester.Context.Stats.Nodes,
        tester.Context.Stats.Instances,
        metal is null ? null : Median(draws)
    );
    results.Add(result);
    Console.WriteLine($"{result.Scene,-10} frame {result.MedianMs:F4} ms, p95 {result.P95Ms:F4} ms, {result.AllocatedBytesPerFrame:F0} B/frame");
}

File.WriteAllText(
    output,
    JsonSerializer.Serialize(
    new BenchmarkReport(
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(),
    RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT",
    results.ToArray()
),
    BenchmarkJson.Default.BenchmarkReport
)
);
Console.WriteLine(output);
static double Median(double[] values) => Percentile(values, .5);
static double Percentile(double[] values, double p)
{
    Array.Sort(values);
    return values[Math.Min(values.Length - 1, (int)(values.Length * p))];
}

record Result(
    string Scene,
    double MedianMs,
    double P95Ms,
    double BuildMs,
    double LayoutMs,
    double PaintMs,
    double AllocatedBytesPerFrame,
    int Nodes,
    int Instances,
    double? MetalOffscreenIncludingReadbackMs
);
record BenchmarkReport(string Runtime, string OS, string Architecture, string Compilation, Result[] Results);
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BenchmarkReport))]
partial class BenchmarkJson : JsonSerializerContext;
static class Scenes
{
    static int counter;
    internal static void Boxes(Context c)
    {
        using var root = c.Div().SizeFull();
        for (int i = 0; i < 600; i++)
            using (c.Div(key: i)
                .Absolute()
                .Offset((i % 30) * 39 + 4, (i / 30) * 39 + 4)
                .Width(32)
                .Height(28)
                .Rounded(5)
                .Background(Color.Hex(0x3478f6))
                .Shadow(3, 2))
            {
            }
    }

    internal static void Text(Context c)
    {
        using var root = c.Div().SizeFull();
        counter++;
        for (int i = 0; i < 300; i++)
        {
            using var box = c.Div(key: i).Absolute().Offset(i % 5 * 235, i / 5 * 13).Width(230).Height(14);
            c.Text($"Label {i}: {counter % 100}", new(Size: 11), key: "label");
        }
    }

    internal static void Scroll(Context c)
    {
        using var root = c.Div().SizeFull().Scroll();
        for (int i = 0; i < 1000; i++)
        {
            using var row = c.Div(key: i).Row().Width(Length.Fill).Height(28).ItemsCenter().Gap(12);
            c.Text($"Row {i}", key: "label");
            c.Text("Scrollable content", key: "value");
        }
    }

    internal static void List(Context c) => c.UniformList("100k", 100_000, 28, static (ctx, i) => ctx.Text($"Row {i}"));
    internal static void Dashboard(Context c)
    {
        using var root = c.Div().SizeFull();
        for (int i = 0; i < 12; i++)
        {
            using var card = c.Card(key: i).Absolute().Offset(i % 4 * 296 + 8, i / 4 * 260 + 8).Width(280).Height(244);
            c.Text($"Metric {i + 1}", key: "label");
            c.Heading("72.4", 40, key: "value");
            c.Progress(.724f, 240, key: "progress");
            c.Label("Compared with last period", key: "comparison");
        }
    }
}
