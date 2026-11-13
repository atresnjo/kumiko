using System.Diagnostics;
using System.Text.Json;
using Kumiko.Rendering;
using Kumiko.UI;

static class RenderPerformance
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var ui = new Tester(View, new(Width: 1000, Height: 850, Scale: 2));
        using var gpu = new OffscreenGpu();
        using var file = File.Create(Path.Combine(output, "render-performance.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        json.WriteString("runtime", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
        json.WriteString("compilation", System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "NativeAOT");
        json.WriteString("driver", gpu.Driver);
        json.WriteNumber("warmupFrames", 3);
        json.WriteNumber("sampleFrames", 9);
        json.WriteNumber("width", 2000);
        json.WriteNumber("height", 1700);
        Measure(json, "cpuFull", ui.Render);
        Measure(json, "glCaptureWithGlass", () => gpu.Capture(ui.Context));
        var glassCanvas = new CpuCanvas();
        glassCanvas.Render(ui.Context);
        Measure(json, "cpuUnchangedGlass", () => glassCanvas.Render(ui.Context));
        var parallel = Pixels.Create(2000, 1700);
        Measure(json, "cpuParallelGlass", () =>
        {
            CpuRenderer.DrawRegion(parallel, ui.Context.Instances, ui.Context.TextSystem.Atlas, 2,
                ui.Context.Theme.Background, PixelRegion.Whole(parallel));
            return parallel;
        });
        int step = 0;
        using var hover = new Tester(c =>
        {
            using var root = c.Div(key: "hover").SizeFull().Background(Color.Hex(0xeff3f7));
            for (int i = 0; i < 48; i++)
            {
                using var tile = c.Div(key: i).Absolute().Offset(i % 8 * 125, i / 8 * 140)
                    .Width(110).Height(120).Rounded(16)
                    .Background(Color.Hex(i == step % 48 ? 0x32a7bbu : 0xe87842u)).Shadow(6, 3);
            }
        }, new(Width: 1000, Height: 850, Scale: 2));
        var canvas = new CpuCanvas();
        canvas.Render(hover.Context);
        Measure(json, "cpuFullHover", () =>
        {
            step++;
            hover.Frame();
            return hover.Render();
        });
        Measure(json, "cpuIncrementalHover", () =>
        {
            step++;
            hover.Frame();
            return canvas.Render(hover.Context);
        });
        Png.Write(Path.Combine(output, "render-performance.png"), gpu.Capture(ui.Context));
        json.WriteEndObject();
        Console.WriteLine($"Rendering measurements: {output}");
    }

    internal static void Measure(Utf8JsonWriter json, string name, Func<Pixels> draw)
    {
        for (int i = 0; i < 3; i++)
            draw();
        var samples = new double[9];
        long allocated = GC.GetTotalAllocatedBytes(true);
        for (int i = 0; i < samples.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            GC.KeepAlive(draw());
            samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        allocated = GC.GetTotalAllocatedBytes(true) - allocated;
        Array.Sort(samples);
        json.WriteStartObject(name);
        json.WriteNumber("medianMs", samples[samples.Length / 2]);
        json.WriteNumber("p95Ms", samples[^1]);
        json.WriteNumber("allocatedBytesPerFrame", allocated / samples.Length);
        json.WriteEndObject();
    }

    internal static void View(Context c)
    {
        using var root = c.Div(key: "render-perf").SizeFull().Background(Color.Hex(0x132744));
        for (int i = 0; i < 48; i++)
        {
            using var tile = c.Div(key: i).Absolute().Offset(i % 8 * 125, i / 8 * 140)
                .Width(110).Height(120).Rounded(16).Background(Color.Hex(i % 2 == 0 ? 0xe87842u : 0x32a7bbu)).Shadow(6, 3);
        }
        using (c.Div(key: "glass-a").Absolute().Offset(90, 60).Width(450).Height(350).Rounded(30).Glass())
        {
        }
        using (c.Div(key: "glass-b").Absolute().Offset(420, 320).Width(400).Height(400).Rounded(24)
            .Glass(new(Tint: Color.Hex(0x4088d0))))
        {
        }
    }
}
