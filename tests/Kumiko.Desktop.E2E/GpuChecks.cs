using System.Text.Json;
using Kumiko.Rendering;
using Kumiko.UI;

static class GpuChecks
{
    // Text, rounded clipping, borders, images, shadows, alpha, a turned card and a glass pane in one view.
    internal static Action<Context> Scene()
    {
        var pixels = Pixels.Create(16, 16);
        for (int i = 0; i < pixels.Bytes.Length; i += 4)
        {
            pixels.Bytes[i] = 100;
            pixels.Bytes[i + 2] = 80;
            pixels.Bytes[i + 3] = 128;
        }
        var image = new Image(pixels);
        void View(Context c)
        {
            using var root = c.Div(key: "gpu").SizeFull().Padding(20).Gap(16).Background(Color.Hex(0xeff2f6));
            c.Text("GPU · مرحبا · नमस्ते · 日本語", new(Size: 22), key: "text");
            using (c.Div(key: "round").Row().Width(260).Height(120).Padding(12).Gap(12)
                .Background(Color.Hex(0x2563eb)).Border(Color.Hex(0x153a96), 2).Rounded(22).Clip().Shadow(8, 4))
            {
                c.Image(image, 80, 80, 10, key: "image");
                c.Text("Rounded clipping", new(Color: Color.White, Wrap: true), key: "label");
            }
            using (c.Div(key: "alpha").Width(180).Height(36).Background(new Color(.8f, .15f, .3f, .45f)).Rounded(8))
            {
            }
            using (c.Div(key: "glass").Absolute().Offset(190, 110).Width(140).Height(130).Rounded(18).Glass())
            {
            }
            // A turned card clips and turns its text with it.
            using (c.Div(key: "turned").Absolute().Offset(340, 60).Width(64).Height(120).Padding(8).Rounded(12).Clip()
                .Background(Color.Hex(0xf97316)).Border(Color.Hex(0x9a3412), 2).Shadow(10, 4).Rotate(8))
                c.Text("Turned card text", new(Color: Color.White, Wrap: true), key: "text");
        }
        return View;
    }

    public static void Run(string output, Action<bool, string> check)
    {
        var scene = Scene();
        using var ui = new Tester(scene, new(Width: 420, Height: 300));
        OffscreenGpu renderer;
        try
        {
            renderer = new OffscreenGpu();
        }
        catch (InvalidOperationException ex) when (OperatingSystem.IsWindows())
        {
            using var skipped = File.Create(Path.Combine(output, "gpu.json"));
            using var record = new Utf8JsonWriter(skipped);
            record.WriteStartObject();
            record.WriteBoolean("available", false);
            record.WriteString("reason", ex.Message);
            record.WriteString("coverage", "native window software fallback is exercised separately");
            record.WriteEndObject();
            Console.WriteLine("GPU parity unavailable: " + ex.Message);
            return;
        }
        using var gpu = renderer;
        var cpu = ui.Render();
        var native = gpu.Capture(ui.Context);
        double error = Difference(cpu, native);
        check(error < 2.0, $"GPU clipping, text, images, shadows, turns and glass agree with CPU (mean channel error {error:F3})");
        Png.Write(Path.Combine(output, "gpu.png"), native);
        Png.Write(Path.Combine(output, "gpu-cpu-reference.png"), cpu);
        ui.Resize(580, 340);
        native = gpu.Capture(ui.Context);
        check(native.Width == 580 && native.Height == 340, "GPU framebuffer resizes without stale pixels");
        Png.Write(Path.Combine(output, "gpu-resized.png"), native);
        using var scaled = new Tester(scene, new(Width: 420, Height: 300, Scale: 2));
        var highDpi = gpu.Capture(scaled.Context);
        check(highDpi.Width == 840 && highDpi.Height == 600 && Difference(scaled.Render(), highDpi) < 2,
            "GPU rendering keeps glyphs, clipping and geometry correct at 2x scale");
        Png.Write(Path.Combine(output, "gpu-2x.png"), highDpi);
        using var file = File.Create(Path.Combine(output, "gpu.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteBoolean("available", true);
        json.WriteString("driver", gpu.Driver);
        json.WriteNumber("meanChannelError", error);
        json.WriteString("glass", "GPU downsample, separable blur and optical filter");
        json.WriteEndObject();
    }
    internal static double Difference(Pixels a, Pixels b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return double.PositiveInfinity;
        long error = 0;
        for (int i = 0; i < a.Bytes.Length; i++)
            error += Math.Abs(a.Bytes[i] - b.Bytes[i]);
        return error / (double)a.Bytes.Length;
    }
}
