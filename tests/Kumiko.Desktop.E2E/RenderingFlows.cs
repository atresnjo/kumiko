using System.Security.Cryptography;
using System.Text.Json;
using Kumiko;
using Kumiko.Rendering;
using Kumiko.UI;

static class RenderingFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var file = File.Create(Path.Combine(output, "render-flows.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        Cpu(output, json, check);
        Gpu(output, json, check);
        Fallback(output, check);
        json.WriteEndObject();
    }

    static void Cpu(string output, Utf8JsonWriter json, Action<bool, string> check)
    {
        int step = 0;
        var image = new Image(Pixels.Create(12, 12));
        Array.Fill(image.Pixels.Bytes, (byte)160);
        void View(Context c)
        {
            using var root = c.Div(key: "damage").SizeFull().Background(c.Theme.Background);
            if (c.Button("Next", key: "next").Clicked)
                step++;
            using (c.Div(key: "back").Absolute().Offset(80, 70).Width(180).Height(150)
                .Rounded(12).Background(Color.Hex(0x3297b6)).Shadow(8, 3))
                c.Text($"Step {step}", key: "step");
            if (step % 3 != 2)
            {
                using var moving = c.Div(key: "moving").Absolute().Offset(100 + step * 7, 95)
                    .Width(65).Height(60).Rounded(10).Background(new Color(.9f, .2f, .3f, .6f)).Shadow(5, 2);
            }
            using (c.Div(key: "overlap").Absolute().Offset(155, 100).Width(80).Height(90)
                .Rounded(18).Clip().Background(new Color(.2f, .8f, .4f, .4f)))
                c.Image(image, 110, 70, key: "image");
            if (step % 2 == 1)
                c.Text("Inserted content 日本語", key: "inserted");
        }
        using var ui = new Tester(View, new(Width: 400, Height: 300));
        var canvas = new CpuCanvas();
        long redrawn = 0;
        for (int i = 0; i < 12; i++)
        {
            var actual = canvas.Render(ui.Context);
            check(actual.Bytes.AsSpan().SequenceEqual(ui.Render().Bytes), $"incremental CPU frame {i} matches fresh rendering after interaction");
            redrawn += canvas.LastDamage.Area;
            ui.Get(Role.Button, "Next").Click();
        }
        check(redrawn < 12L * 400 * 300, "small interactions redraw fewer software pixels than full frames");
        var stable = canvas.Render(ui.Context);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++)
            check(ReferenceEquals(stable, canvas.Render(ui.Context)), "software frame retains its framebuffer");
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        check(allocated < 4096 && canvas.LastDamage.Area == 0, "unchanged software frames avoid drawing and framebuffer allocation");
        ui.Context.Size = new(200, 150);
        ui.Context.Scale = 2;
        ui.Context.TextSystem.Scale = 2;
        ui.Settle();
        check(canvas.Render(ui.Context).Bytes.AsSpan().SequenceEqual(ui.Render().Bytes), "scale change with equal pixel dimensions invalidates software pixels");
        ui.Context.Theme = Theme.ShadcnDark;
        ui.Settle();
        check(canvas.Render(ui.Context).Bytes.AsSpan().SequenceEqual(ui.Render().Bytes), "background and text-atlas changes invalidate software pixels");
        Png.Write(Path.Combine(output, "render-cpu-interactions.png"), canvas.Render(ui.Context));

        // Atlas consumers can replace/reset texels without changing instance geometry.
        var atlas = ui.Context.TextSystem.Atlas;
        var instances = ui.Context.Instances.ToArray();
        atlas.Reset();
        atlas.Insert(new byte[64 * 64 * 4], 64, 64);
        var reset = canvas.Render(instances, atlas, ui.Context.Size, 2, Color.Black);
        var reference = CpuRenderer.Render(instances, atlas, ui.Context.Size, 2, Color.Black);
        check(reset.Bytes.AsSpan().SequenceEqual(reference.Bytes), "atlas reset redraws instances whose geometry did not change");
        var replacement = new Atlas();
        replacement.Insert(Enumerable.Repeat((byte)255, 64 * 64 * 4).ToArray(), 64, 64);
        reset = canvas.Render(instances, replacement, ui.Context.Size, 2, Color.Black);
        reference = CpuRenderer.Render(instances, replacement, ui.Context.Size, 2, Color.Black);
        check(reset.Bytes.AsSpan().SequenceEqual(reference.Bytes), "a replacement atlas cannot reuse another atlas's cached pixels");
        json.WriteStartObject("cpu");
        json.WriteNumber("interactionPixelsRedrawn", redrawn);
        json.WriteNumber("unchangedAllocatedBytes", allocated);
        json.WriteString("sha256", Convert.ToHexString(SHA256.HashData(reset.Bytes)));
        json.WriteEndObject();

        using var large = new Tester(RenderPerformance.View, new(Width: 1000, Height: 850, Scale: 1.25f));
        var serial = large.Render();
        var parallel = Pixels.Create(serial.Width, serial.Height);
        CpuRenderer.DrawRegion(parallel, large.Context.Instances, large.Context.TextSystem.Atlas, 1.25f,
            large.Context.Theme.Background, PixelRegion.Whole(parallel), parallel: true);
        check(parallel.Bytes.AsSpan().SequenceEqual(serial.Bytes), "row-band rendering preserves overlapping glass and blending byte-for-byte");
        Png.Write(Path.Combine(output, "render-cpu-glass.png"), parallel);
    }

    static void Gpu(string output, Utf8JsonWriter json, Action<bool, string> check)
    {
        OffscreenGpu renderer;
        try
        {
            renderer = new();
        }
        catch (InvalidOperationException) when (OperatingSystem.IsWindows())
        {
            json.WriteBoolean("gpuAvailable", false);
            return;
        }
        using var gpu = renderer;
        json.WriteString("driver", gpu.Driver);
        json.WriteStartArray("glass");
        foreach (float scale in new[] { .75f, 1.25f, 2f })
        {
            bool panes = true;
            using var ui = new Tester(c => GlassScene(c, panes), new(Width: 359, Height: 259, Scale: scale, Theme: Theme.ShadcnDark));
            int before = gpu.ReadbackCount;
            var actual = gpu.Capture(ui.Context);
            check(gpu.ReadbackCount - before == 1, "GL glass stays on GPU until the explicitly requested capture");
            var cpu = ui.Render();
            var (mean, outliers) = GlassError(cpu, actual);
            check(mean < 1 && outliers < cpu.Bytes.Length * .01, $"GPU overlapping, clipped, tinted glass agrees at scale {scale} (mean {mean:F3})");
            string suffix = scale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Png.Write(Path.Combine(output, $"render-glass-{suffix}.png"), actual);
            Png.Write(Path.Combine(output, $"render-glass-reference-{suffix}.png"), cpu);
            json.WriteStartObject();
            json.WriteNumber("scale", scale);
            json.WriteNumber("meanChannelError", mean);
            json.WriteNumber("outlierChannels", outliers);
            json.WriteEndObject();
            panes = false;
            ui.Resize(410, 290);
            actual = gpu.Capture(ui.Context);
            check(actual.Width == (int)MathF.Ceiling(410 * scale) && actual.Height == (int)MathF.Ceiling(290 * scale),
                "ordinary GPU rendering resizes after glass without retaining old panes");
            using var fresh = new OffscreenGpu();
            check(actual.Bytes.AsSpan().SequenceEqual(fresh.Capture(ui.Context).Bytes),
                "glass passes restore state for the following ordinary frame");
        }
        json.WriteEndArray();
    }

    // Mean channel error and channels off by more than 8; glass parity allows mean < 1 and < 1% outliers.
    internal static (double Mean, int Outliers) GlassError(Pixels cpu, Pixels actual)
    {
        if (cpu.Width != actual.Width || cpu.Height != actual.Height)
            return (double.PositiveInfinity, cpu.Bytes.Length);
        long difference = 0;
        int outliers = 0;
        for (int i = 0; i < cpu.Bytes.Length; i++)
        {
            int d = Math.Abs(cpu.Bytes[i] - actual.Bytes[i]);
            difference += d;
            if (d > 8)
                outliers++;
        }
        return (difference / (double)cpu.Bytes.Length, outliers);
    }

    // Overlapping clipped, frosted, tinted and clear panes over stripes; without panes, stripes only.
    internal static void GlassScene(Context c, bool panes)
    {
        using var root = c.Div(key: "glass-scene").SizeFull().Background(Color.Hex(0x254565));
        for (int i = 0; i < 12; i++)
        {
            using var stripe = c.Div(key: i).Absolute().Offset(i * 30, 0).Width(17).Height(260)
                .Background(Color.Hex(i % 2 == 0 ? 0xf5a340u : 0x228fa3u));
        }
        if (!panes)
            return;
        using (c.Div(key: "clip").Absolute().Offset(-15, 8).Width(225).Height(220).Rounded(24).Clip())
        using (c.Div(key: "frost").Absolute().Offset(0, 0).Width(240).Height(230).Rounded(30).Glass())
        {
        }
        using (c.Div(key: "tint").Absolute().Offset(140, 95).Width(230).Height(180).Rounded(28)
            .Glass(new(Tint: Color.Hex(0x499dce))))
        {
        }
        using (c.Div(key: "clear").Absolute().Offset(220, 15).Width(115).Height(100).Rounded(22).Glass(new(Clear: true)))
        {
        }
    }

    static void Fallback(string output, Action<bool, string> check)
    {
        if (OperatingSystem.IsMacOS())
            return;
        string? previous = Environment.GetEnvironmentVariable("KUMIKO_RENDERER");
        Environment.SetEnvironmentVariable("KUMIKO_RENDERER", "cpu");
        try
        {
            using var app = new App(new("Software renderer verification", Hidden: true));
            using var window = app.NewWindow(new(Width: 300, Height: 200, Visible: false), c =>
            {
                using var root = c.Div(key: "fallback").SizeFull();
                c.Text("Forced software", key: "label");
            });
            app.RunFor(TimeSpan.FromMilliseconds(30));
            var pixels = window.Capture();
            check(window.Renderer.Contains("Software", StringComparison.Ordinal) && !window.IsVisible,
                "forced CPU rendering reports the software path and keeps the window hidden");
            check(pixels.Width == (int)MathF.Ceiling(window.Context!.Size.Width * window.Context.Scale) &&
                pixels.Height == (int)MathF.Ceiling(window.Context.Size.Height * window.Context.Scale),
                "forced CPU native capture renders the complete window");
            var saved = (byte[])pixels.Bytes.Clone();
            Array.Clear(pixels.Bytes);
            pixels = window.Capture();
            check(pixels.Bytes.AsSpan().SequenceEqual(saved), "changing a captured snapshot cannot corrupt retained software pixels");
            Png.Write(Path.Combine(output, "render-native-cpu.png"), pixels);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KUMIKO_RENDERER", previous);
        }
    }
}
