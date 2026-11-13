using System.Globalization;
using System.Runtime.CompilerServices;
using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>
/// Frame memory scales with what is visible: a clipped megabyte line paints only the glyphs inside its clip, and
/// buffers that grew for a spike frame return to a small size once frames are small again.
/// </summary>
internal static class LongLineChecks
{
    static readonly TextStyle Style = new(Size: 18, Mono: true, Color: Color.Black);
    static readonly string Long = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 4500));

    internal static void Run(string output, Action<bool, string> check)
    {
        foreach (var shift in new[] { 0f, 1234f })
        {
            using var actual = Clipped(Long, shift, rich: false, overflow: TextOverflow.Visible);
            using var expected = Clipped(Long[..400], shift, rich: false, overflow: TextOverflow.Visible);
            check(Glyphs(actual) == Glyphs(expected) && Glyphs(actual) < 100,
                string.Create(CultureInfo.InvariantCulture, $"a clipped {Long.Length:N0}-character line paints only visible glyphs (shift {shift})"));
            check(Equal(actual, expected), $"culling keeps the visible part of a long line identical (shift {shift})");
            actual.Screenshot(Path.Combine(output, $"long-line-{shift}.png"));
        }
        using (var actual = Clipped(Long, 1234, rich: true, overflow: TextOverflow.Visible))
        using (var expected = Clipped(Long[..400], 1234, rich: true, overflow: TextOverflow.Visible))
            check(Glyphs(actual) < 100 && Equal(actual, expected), "rich text spans on a long line paint only visible glyphs");
        using (var actual = Clipped(Long, 0, rich: false, overflow: TextOverflow.Clip))
        using (var expected = Clipped(Long[..400], 0, rich: false, overflow: TextOverflow.Clip))
            check(Glyphs(actual) < 100 && Equal(actual, expected), "overflow-clipped long text paints only visible glyphs");
        Retention(check);
    }

    /// <summary>A long line inside a 300-point clipping box, moved left by <paramref name="shift"/> points.</summary>
    static Tester Clipped(string text, float shift, bool rich, TextOverflow overflow) => new(c =>
    {
        using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8);
        using var box = c.Div(key: "box").Width(300).Height(40).Clip();
        using var line = c.Div(key: "line").Offset(-shift, 0).Row();
        var style = Style with
        {
            Overflow = overflow
        };
        if (rich)
            c.RichText(text, [new(120, 124, Background: Color.Hex(0xffe08a)), new(text.Length - 20, text.Length - 10, Background: Color.Hex(0xffe08a))], style, key: "text");
        else
            c.Text(text, style, key: "text");
    }, new(Width: 360, Height: 60));

    static void Retention(Action<bool, string> check)
    {
        var big = true;
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Row().Wrap();
            if (!big)
                return;
            for (var i = 0; i < 40_000; i++)
                using (c.Div(key: "cell" + i).Width(2).Height(2).Background(Color.Hex((uint)(i * 2654435761) & 0xffffff)))
                {
                }
        }, new(Width: 400, Height: 400));
        var spike = t.Context.RetainedInstanceBytes;
        check(t.Context.Stats.Instances > 40_000 && spike >= 40_000L * Unsafe.SizeOf<Instance>(),
            "a spike frame paints every visible cell");
        using var metal = new MetalRenderer();
        for (var i = 0; i < 3; i++)
            metal.Render(t.Context.Instances, t.Context.TextSystem.Atlas, t.Context.Size, 1, Color.White);
        var gpuSpike = metal.RetainedBufferBytes;
        big = false;
        t.Frame();
        t.Frame();
        for (var i = 0; i < 3; i++)
            metal.Render(t.Context.Instances, t.Context.TextSystem.Atlas, t.Context.Size, 1, Color.White);
        const long ceiling = 4L << 20;
        check(t.Context.RetainedInstanceBytes < ceiling && t.Context.RetainedInstanceBytes < spike / 4,
            string.Create(CultureInfo.InvariantCulture, $"paint buffers shrink after a spike ({spike:N0} → {t.Context.RetainedInstanceBytes:N0} bytes)"));
        check(gpuSpike >= 3 * 40_000L * Unsafe.SizeOf<Instance>() && metal.RetainedBufferBytes < 3 * ceiling,
            string.Create(CultureInfo.InvariantCulture, $"Metal instance buffers shrink after a spike ({gpuSpike:N0} → {metal.RetainedBufferBytes:N0} bytes)"));
    }

    static int Glyphs(Tester t) => t.Context.Instances.Count(i => i.Kind == 1);

    static bool Equal(Tester a, Tester b)
    {
        var actual = a.Render();
        var expected = b.Render();
        if (actual.Bytes.SequenceEqual(expected.Bytes))
            return true;
        var differences = Enumerable.Range(0, actual.Bytes.Length).Where(i => actual.Bytes[i] != expected.Bytes[i]).ToArray();
        Console.WriteLine($"Pixel mismatch: {differences.Length} channels; first at ({differences[0] / 4 % actual.Width}, {differences[0] / 4 / actual.Width})");
        return false;
    }
}
