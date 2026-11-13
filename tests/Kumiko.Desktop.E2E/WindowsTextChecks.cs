using System.Globalization;
using System.Text.Json;
using Kumiko.Rendering;
using Kumiko.UI;

// DirectWrite is the Windows text backend; Uniscribe stays compiled as its fallback. Every check uses
// offscreen layouts, Direct2D WIC targets and Tester frames, so nothing is shown on screen.
static class WindowsTextChecks
{
    const string Latin = "Kumiko text, shaped";
    const string Mixed = "abc אבג def";

    public static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var file = File.Create(Path.Combine(output, "windows-text.json"));
        using var json = new Utf8JsonWriter(file, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("backend", DirectWrite.Backend);
        check(DirectWrite.FallbackReason is null, $"DirectWrite is the active Windows text backend ({DirectWrite.Backend})");
        using (var native = new NativeText())
        {
            var run = native.Shape(Latin, new(Size: 14), null);
            check(run.Native is DirectWriteText, "NativeText shapes Windows text through DirectWrite");
        }
        Metrics(json, check);
        HitTesting(check);
        Rasters(json, check);
        Fallback(json, check);
        Frames(output, check);
        json.WriteEndObject();
    }

    static void Metrics(Utf8JsonWriter json, Action<bool, string> check)
    {
        using var regular = new DirectWriteText(Latin, new(Size: 14), null);
        using var spaced = new DirectWriteText(Latin, new(Size: 14, Leading: 1.5f), null);
        using var bold = new DirectWriteText(Latin, new(Size: 14, Weight: Weight.Bold), null);
        using var trailing = new DirectWriteText(Latin + "   ", new(Size: 14), null);
        using var empty = new DirectWriteText("", new(Size: 14), null);
        json.WriteNumber("width", regular.Width);
        json.WriteNumber("height", regular.Height);
        json.WriteNumber("ascent", regular.Ascent);
        check(regular.Width > 60 && regular.Ascent > 8 && regular.Ascent < regular.Height && regular.Height < 30,
            "a 14 DIP Segoe UI line reports plausible width, baseline and line height");
        check(Math.Abs(spaced.Height - regular.Height * 1.5f) < .01f && spaced.Ascent == regular.Ascent,
            "leading multiplies the line height and keeps the first baseline");
        check(bold.Width > regular.Width, "bold text is wider than regular text");
        check(trailing.Width > regular.Width + 5, "measured width includes trailing whitespace");
        check(empty.Width == 0 && empty.Ascent > 8 && empty.Height > empty.Ascent, "empty text keeps the font's line metrics");
        using var narrow = new DirectWriteText("iiiiiiii", new(Size: 14, Mono: true), null);
        using var wide = new DirectWriteText("MMMMMMMM", new(Size: 14, Mono: true), null);
        check(Math.Abs(narrow.Width - wide.Width) < .01f, "Mono uses Consolas, a fixed-pitch face");
        using var cjk = new DirectWriteText("日本語 · 👩‍💻", new(Size: 14), null);
        check(cjk.Width > 40 && cjk.Raster(1, 0).Bytes.Where((_, i) => i % 4 == 3).Any(a => a > 128),
            "font fallback measures and draws CJK and emoji absent from Segoe UI");
    }

    static void HitTesting(Action<bool, string> check)
    {
        using var layout = new DirectWriteText("iiiiiiiiii", new(Size: 13), null);
        var carets = Enumerable.Range(0, 11).Select(layout.Caret).ToArray();
        check(carets.Zip(carets.Skip(1)).All(pair => pair.Second > pair.First) && carets.Any(x => MathF.Abs(x - MathF.Round(x)) > .05f),
            "caret positions keep fractional subpixel advances instead of whole-pixel GDI advances");
        check(Math.Abs(carets[^1] - layout.Width) < .01f, "the end caret is the trailing edge of the line");
        bool roundTrip = true;
        for (int i = 0; i < carets.Length; i++)
            roundTrip &= layout.HitTest(carets[i] + .1f) == i;
        check(roundTrip && layout.HitTest(-20) == 0 && layout.HitTest(layout.Width + 20) == 10,
            "hit testing returns the nearest caret position and clamps outside the line");
        using var cluster = new DirectWriteText("ab👩‍💻", new(Size: 14), null);
        check(Math.Abs(cluster.Caret(7) - cluster.Width) < .01f && cluster.Caret(2) < cluster.Width - 4,
            "the end caret follows the trailing edge of a final emoji grapheme");
        using var mixed = new DirectWriteText(Mixed, new(Size: 14), null);
        var rects = mixed.Selection(2, 5);
        check(rects.Length >= 2 && rects.All(r => r.Y == 0 && Math.Abs(r.Height - mixed.Height) < .01f),
            "a selection across a direction change returns one full-height rectangle per visual run");
        check(mixed.Caret(5) < mixed.Caret(4) && mixed.Caret(8) > mixed.Caret(4),
            "right-to-left text inside a left-to-right line is laid out in visual order");
        check(mixed.Selection(5, 5).Length == 0 && mixed.Selection(0, 99).Length >= 1, "empty selections are empty and ranges clamp to the text");
    }

    static void Rasters(Utf8JsonWriter json, Action<bool, string> check)
    {
        using var layout = new DirectWriteText(Latin, new(Size: 14), null);
        foreach (float scale in new[] { 1f, 1.25f, 2f })
        {
            var raster = layout.Raster(scale, 0);
            int expected = (int)MathF.Ceiling(layout.Width * scale) + 4;
            check(raster.Width == expected && raster.Height == (int)MathF.Ceiling(layout.Height * scale) + 4,
                $"a {scale.ToString(CultureInfo.InvariantCulture)}x raster covers the scaled line plus 2px padding");
            check(raster.X == -2 && raster.Y == -MathF.Round(layout.Ascent * scale) - 2,
                "the raster origin puts the baseline on a whole device row");
            bool white = true;
            int left = int.MaxValue, right = -1;
            for (int i = 0; i < raster.Bytes.Length; i += 4)
            {
                white &= raster.Bytes[i] == 255 && raster.Bytes[i + 1] == 255 && raster.Bytes[i + 2] == 255;
                if (raster.Bytes[i + 3] > 32)
                {
                    int column = i / 4 % raster.Width;
                    left = Math.Min(left, column);
                    right = Math.Max(right, column);
                }
            }
            check(white && right > left, "raster pixels are white with coverage in alpha, as atlas masks expect");
            check(left >= 1 && left <= 4 && Math.Abs(right - (2 + layout.Width * scale)) < 3 * scale,
                "ink spans the measured advance at device scale");
            json.WriteNumber("rasterWidth" + scale.ToString("0.00", CultureInfo.InvariantCulture), raster.Width);
        }
        string longText = string.Concat(Enumerable.Repeat("Sliced DirectWrite line ", 80));
        using var native = new NativeText();
        var run = native.Shape(longText, new(Size: 14), null);
        check(run.Glyphs.Length == (int)MathF.Ceiling(run.Width / NativeText.SliceWidth) && run.Glyphs.Length > 1, "long lines are split into 1024 DIP slices");
        var second = native.Raster(run.Glyphs[1], 1.5f);
        float remaining = Math.Min(NativeText.SliceWidth, run.Width - NativeText.SliceWidth);
        check(second.Width == (int)MathF.Ceiling(remaining * 1.5f) + 4 && second.Bytes.Where((_, i) => i % 4 == 3).Any(a => a > 128),
            "a later slice rasterizes its own part of the line at fractional scale");
    }

    static void Fallback(Utf8JsonWriter json, Action<bool, string> check)
    {
        using var direct = new DirectWriteText(Latin, new(Size: 14), null);
        using var uniscribe = new UniscribeText(Latin, new(Size: 14), null);
        json.WriteNumber("uniscribeWidth", uniscribe.Width);
        json.WriteNumber("uniscribeAscent", uniscribe.Ascent);
        check(Math.Abs(direct.Width - uniscribe.Width) < uniscribe.Width * .1f && Math.Abs(direct.Ascent - uniscribe.Ascent) < 2,
            "DirectWrite keeps the logical size the Uniscribe fallback uses");
        var raster = uniscribe.Raster(1, 0);
        check(raster.Bytes.Where((_, i) => i % 4 == 3).Any(a => a > 128) && uniscribe.HitTest(uniscribe.Width + 10) == Latin.Length,
            "the Uniscribe fallback still shapes, rasterizes and hit-tests");
        var disposed = new DirectWriteText(Latin, new(Size: 14), null);
        disposed.Dispose();
        disposed.Dispose();
        check(disposed.Caret(3) == 0 && disposed.Selection(0, 3).Length == 0, "disposed layouts release their COM object once and answer safely");
    }

    static void Frames(string output, Action<bool, string> check)
    {
        static void View(Context c)
        {
            using var root = c.Div(key: "windows-text").SizeFull().Padding(16).Gap(8).Background(Color.White);
            c.Text("DirectWrite · مرحبا · नमस्ते · 日本語", new(Size: 22, Color: Color.Black), key: "scripts");
            c.Text(string.Concat(Enumerable.Repeat("Long sliced line ", 90)), new(Size: 13, Color: Color.Black), key: "long");
        }
        using var ui = new Tester(View, new(Width: 1600, Height: 120, Scale: 1.5f));
        check(ui.Context.TextSystem.CaretPosition("abc", new(Size: 22), 3) > 0, "the text system measures carets through the native layout");
        var pixels = ui.Render();
        int Dark(int x0, int x1, int y0, int y1)
        {
            int count = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    count += pixels.Bytes[(y * pixels.Width + x) * 4] < 128 ? 1 : 0;
            return count;
        }
        int boundary = (int)((16 + NativeText.SliceWidth) * 1.5f);
        check(Dark(24, 600, 24, (int)((16 + 22) * 1.5f)) > 200, "a Tester frame paints DirectWrite masks for mixed scripts");
        check(Dark(boundary - 60, boundary, 0, pixels.Height) > 20 && Dark(boundary, boundary + 60, 0, pixels.Height) > 20,
            "a long line paints ink on both sides of a slice boundary");
        ui.Screenshot(Path.Combine(output, "windows-text-directwrite.png"));
    }
}
