using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Kumiko;
using Kumiko.Rendering;
using Kumiko.UI;

// Direct3D 11 parity with the CPU renderer. WARP ships with every Windows 10+ install, so Windows
// hosts always run these checks; other hosts record them as unavailable in d3d11.json.
static class Direct3DChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var file = File.Create(Path.Combine(output, "d3d11.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        if (!OperatingSystem.IsWindows())
        {
            json.WriteBoolean("available", false);
            json.WriteString("reason", "Direct3D 11 requires Windows; this host is " + RuntimeInformation.OSDescription);
            json.WriteString("coverage", "Windows CI runs the WARP parity, glass and hidden-window checks");
            json.WriteEndObject();
            Console.WriteLine("Direct3D 11 parity unavailable: not a Windows host");
            return;
        }
        json.WriteBoolean("available", true);
        var warp = new OffscreenD3D11(warp: true);
        try
        {
            check(warp.IsWarp && warp.Driver.StartsWith("Direct3D 11 WARP", StringComparison.Ordinal), "a forced WARP device reports the WARP rasterizer");
            json.WriteString("warpDriver", warp.Driver);
            Orientation(warp, check);
            Scene(output, "d3d11", warp, json, check);
            Glass(output, warp, json, check);
        }
        finally
        {
            warp.Dispose();
        }
        json.WriteNumber("warpRemainingDeviceReferences", warp.RemainingDeviceReferences);
        check(warp.RemainingDeviceReferences == 0, "disposing a D3D11 renderer releases every COM object it created");
        using (var preferred = new OffscreenD3D11(warp: false))
        {
            json.WriteString("preferredDriver", preferred.Driver);
            check(preferred.IsWarp ? preferred.Driver.StartsWith("Direct3D 11 WARP", StringComparison.Ordinal) : preferred.Driver.Length > "Direct3D 11 / ".Length,
                "the preferred D3D11 device names its adapter, or WARP when no hardware device exists");
            Scene(output, "d3d11-preferred", preferred, null, check);
        }
        Window(output, json, check);
        json.WriteEndObject();
    }

    // Distinct top/bottom colors and a translucent square catch flipped rows, swapped channels and lost premultiplication.
    static void Orientation(OffscreenD3D11 gpu, Action<bool, string> check)
    {
        static void View(Context c)
        {
            using var root = c.Div(key: "orientation").SizeFull().Background(Color.Black);
            using (c.Div(key: "red").Absolute().Offset(0, 0).Width(64).Height(32).Background(Color.Hex(0xff0000)))
            {
            }
            using (c.Div(key: "blue").Absolute().Offset(0, 32).Width(64).Height(32).Background(Color.Hex(0x0000ff)))
            {
            }
            using (c.Div(key: "half").Absolute().Offset(16, 16).Width(32).Height(32).Background(new Color(1, 1, 1, .5f)))
            {
            }
        }
        using var ui = new Tester(View, new(Width: 64, Height: 64));
        var pixels = gpu.Capture(ui.Context);
        var cpu = ui.Render();
        check(pixels.Bytes[..4] is [0, 0, 255, 255] && pixels.Bytes[^4..] is [255, 0, 0, 255], "D3D11 captures are top-down BGRA like every other renderer");
        int middle = (32 * 64 + 32) * 4;
        bool blended = true;
        for (int i = middle; i < middle + 4; i++)
            blended &= Math.Abs(pixels.Bytes[i] - cpu.Bytes[i]) <= 2;
        check(blended && pixels.Bytes[middle + 3] == 255, "D3D11 blends premultiplied alpha into opaque frame pixels as the CPU renderer does");
        check(GpuChecks.Difference(cpu, pixels) < 1, "D3D11 solid and translucent quads match the CPU renderer");
    }

    static void Scene(string output, string name, OffscreenD3D11 gpu, Utf8JsonWriter? json, Action<bool, string> check)
    {
        var scene = GpuChecks.Scene();
        using var ui = new Tester(scene, new(Width: 420, Height: 300));
        var cpu = ui.Render();
        var native = gpu.Capture(ui.Context);
        double error = GpuChecks.Difference(cpu, native);
        check(error < 2.0, $"{name}: clipping, text, images, shadows and glass agree with CPU (mean channel error {error:F3})");
        Png.Write(Path.Combine(output, name + ".png"), native);
        ui.Resize(580, 340);
        native = gpu.Capture(ui.Context);
        check(native.Width == 580 && native.Height == 340 && GpuChecks.Difference(ui.Render(), native) < 2.0, $"{name}: frame texture resizes without stale pixels");
        using var scaled = new Tester(scene, new(Width: 420, Height: 300, Scale: 2));
        var highDpi = gpu.Capture(scaled.Context);
        check(highDpi.Width == 840 && highDpi.Height == 600 && GpuChecks.Difference(scaled.Render(), highDpi) < 2,
            $"{name}: glyphs, clipping and geometry stay correct at 2x scale");
        Png.Write(Path.Combine(output, name + "-2x.png"), highDpi);
        json?.WriteNumber("meanChannelError", error);
    }

    static void Glass(string output, OffscreenD3D11 gpu, Utf8JsonWriter json, Action<bool, string> check)
    {
        json.WriteStartArray("glass");
        foreach (float scale in new[] { .75f, 1.25f, 2f })
        {
            bool panes = true;
            using var ui = new Tester(c => RenderingFlows.GlassScene(c, panes), new(Width: 359, Height: 259, Scale: scale, Theme: Theme.ShadcnDark));
            int before = gpu.ReadbackCount;
            var actual = gpu.Capture(ui.Context);
            check(gpu.ReadbackCount - before == 1, "D3D11 glass stays on the GPU until the explicitly requested capture");
            var cpu = ui.Render();
            var (mean, outliers) = RenderingFlows.GlassError(cpu, actual);
            check(mean < 1 && outliers < cpu.Bytes.Length * .01, $"D3D11 overlapping, clipped, tinted glass agrees at scale {scale} (mean {mean:F3})");
            string suffix = scale.ToString("0.00", CultureInfo.InvariantCulture);
            Png.Write(Path.Combine(output, $"d3d11-glass-{suffix}.png"), actual);
            json.WriteStartObject();
            json.WriteNumber("scale", scale);
            json.WriteNumber("meanChannelError", mean);
            json.WriteNumber("outlierChannels", outliers);
            json.WriteEndObject();
            panes = false;
            ui.Resize(410, 290);
            actual = gpu.Capture(ui.Context);
            check(actual.Width == (int)MathF.Ceiling(410 * scale) && actual.Height == (int)MathF.Ceiling(290 * scale),
                "ordinary D3D11 rendering resizes after glass without retaining old panes");
            using var fresh = new OffscreenD3D11(warp: true);
            check(actual.Bytes.AsSpan().SequenceEqual(fresh.Capture(ui.Context).Bytes), "D3D11 glass passes restore state for the following ordinary frame");
        }
        json.WriteEndArray();
    }

    // A real hidden window: KUMIKO_RENDERER=warp selects a WARP swap chain without showing it.
    static void Window(string output, Utf8JsonWriter json, Action<bool, string> check)
    {
        string? previous = Environment.GetEnvironmentVariable("KUMIKO_RENDERER");
        Environment.SetEnvironmentVariable("KUMIKO_RENDERER", "warp");
        try
        {
            using var app = new App(new("Direct3D 11 verification", Hidden: true));
            using var window = app.NewWindow(new(Width: 420, Height: 300, Visible: false), GpuChecks.Scene());
            app.RunFor(TimeSpan.FromMilliseconds(30));
            var pixels = window.Capture();
            json.WriteString("windowRenderer", window.Renderer);
            check(window.Renderer.StartsWith("Direct3D 11 WARP", StringComparison.Ordinal) && !window.IsVisible,
                "KUMIKO_RENDERER=warp renders a hidden window through Direct3D 11 WARP");
            var c = window.Context!;
            var reference = CpuRenderer.Render(c.Instances, c.TextSystem.Atlas, c.Size, c.Scale, c.Theme.Background);
            check(GpuChecks.Difference(reference, pixels) < 2, "the D3D11 window capture agrees with the CPU renderer");
            window.Resize(500, 340);
            app.RunFor(TimeSpan.FromMilliseconds(30));
            pixels = window.Capture();
            check(pixels.Width == (int)MathF.Ceiling(c.Size.Width * c.Scale) && pixels.Height == (int)MathF.Ceiling(c.Size.Height * c.Scale),
                "a resized D3D11 window captures its new size");
            Png.Write(Path.Combine(output, "d3d11-window.png"), pixels);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KUMIKO_RENDERER", previous);
        }
    }
}
