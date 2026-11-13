using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>
/// Glyphs rasterized after the first frame reach the GPU through incremental atlas uploads: text that scrolls into
/// view later must look exactly as the CPU renderer draws it, without re-sending the whole atlas.
/// </summary>
internal static class AtlasUploadChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var text = "abc";
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(8).Background(Color.White);
            c.Text(text, new(14, Color: Color.Black), key: "a");
            c.Text(text.ToUpperInvariant() + " 0123", new(12, Weight.Bold, Color.Black, Mono: true), key: "b");
        }, new(Width: 320, Height: 80, Scale: 2));
        using var metal = new MetalRenderer();
        Pixels Gpu() => metal.Render(t.Context.Instances, t.Context.TextSystem.Atlas, t.Context.Size, t.Context.Scale, Color.White);
        t.Settle();
        Gpu();
        var width = t.Context.TextSystem.Atlas.Width;
        foreach (var next in new[] { "xyz — new glyphs", "{ quick: brown, fox }", "jumps => over(lazy, dog);" })
        {
            text = next;
            t.Settle();
            var gpu = Gpu();
            var cpu = t.Render();
            double error = 0;
            for (int i = 0; i < gpu.Bytes.Length; i++)
                error += Math.Abs(gpu.Bytes[i] - cpu.Bytes[i]);
            error /= gpu.Bytes.Length;
            check(error < 3, $"glyphs added after the first frame render on Metal as on the CPU (\"{next}\", mean byte error {error:F3})");
        }
        check(t.TextSystemWidth() == width, "these frames grew no atlas, so they used row uploads");
    }

    static int TextSystemWidth(this Tester t) => t.Context.TextSystem.Atlas.Width;
}
