using Kumiko.UI;

namespace Kumiko.Rendering;

internal readonly record struct TextRaster(byte[] Bytes, int Width, int Height, float X, float Y);

internal interface INativeTextLayout : IDisposable
{
    float Width
    {
        get;
    }
    float Height
    {
        get;
    }
    float Ascent
    {
        get;
    }
    float Caret(int position);
    int HitTest(float x);
    Rect[] Selection(int start, int end);
    TextRaster Raster(float scale, int slice);
}

internal sealed class NativeText : IDisposable
{
    internal const int SliceWidth = 1024;
    readonly Dictionary<nint, INativeTextLayout> layouts = new();
    nint nextId;
    public ShapedRun Shape(string text, TextStyle style, string? family)
    {
        INativeTextLayout layout = OperatingSystem.IsWindows() ? DirectWrite.Create(text, style, family) : new LinuxText(text, style, family);
        var id = ++nextId;
        layouts.Add(id, layout);
        int count = Math.Max(1, (int)MathF.Ceiling(layout.Width / SliceWidth));
        var glyphs = new ShapedGlyph[count];
        for (int i = 0; i < count; i++)
            glyphs[i] = new(id, checked((ushort)i), i * SliceWidth, 0);
        return new(layout.Width, layout.Height, layout.Ascent, glyphs, layout);
    }
    public TextRaster Raster(ShapedGlyph glyph, float scale) => layouts[glyph.Font].Raster(scale, glyph.Glyph);
    public void Retain(IEnumerable<ShapedRun> runs)
    {
        var live = runs.SelectMany(run => run.Glyphs).Select(g => g.Font).ToHashSet();
        foreach (var id in layouts.Keys.Where(id => !live.Contains(id)).ToArray())
        {
            layouts[id].Dispose();
            layouts.Remove(id);
        }
    }
    public void Dispose()
    {
        foreach (var layout in layouts.Values)
            layout.Dispose();
        layouts.Clear();
    }
}
