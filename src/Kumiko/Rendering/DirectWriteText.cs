using System.Globalization;
using Kumiko.UI;
using static Kumiko.Rendering.DirectWrite;

namespace Kumiko.Rendering;

/// <summary>DirectWrite shaping, bidi, font fallback and hit testing with Direct2D grayscale masks.</summary>
internal sealed unsafe class DirectWriteText : INativeTextLayout
{
    readonly string text;
    nint layout;
    public float Width
    {
        get;
    }
    public float Height
    {
        get;
    }
    public float Ascent
    {
        get;
    }

    public DirectWriteText(string text, TextStyle style, string? family)
    {
        this.text = text;
        try
        {
            layout = Layout(text, style, family);
            TextMetrics metrics;
            Check(((delegate* unmanaged<nint, TextMetrics*, int>)Slot(layout, GetMetrics))(layout, &metrics), "DirectWrite text metrics could not be read.");
            Width = metrics.WidthIncludingTrailingWhitespace;
            // Empty text still has one line carrying the font's ascent and line height.
            var lines = new LineMetrics[Math.Max(1, metrics.LineCount)];
            uint count;
            fixed (LineMetrics* p = lines)
            {
                Check(
                    ((delegate* unmanaged<nint, LineMetrics*, uint, uint*, int>)Slot(layout, GetLineMetrics))(layout, p, (uint)lines.Length, &count),
                    "DirectWrite line metrics could not be read."
                );
            }
            Ascent = lines[0].Baseline;
            Height = lines[0].Height * style.Leading;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public float Caret(int position)
    {
        if (layout == 0 || text.Length == 0)
            return 0;
        position = Math.Clamp(position, 0, text.Length);
        bool atEnd = position == text.Length;
        if (atEnd)
            position = StringInfo.ParseCombiningCharacters(text)[^1];
        float x, y;
        HitTestMetrics metrics;
        Check(
            ((delegate* unmanaged<nint, uint, int, float*, float*, HitTestMetrics*, int>)Slot(layout, HitTestTextPosition))(layout, (uint)position, atEnd ? 1 : 0, &x, &y, &metrics),
            "DirectWrite caret hit test failed."
        );
        return x;
    }

    public int HitTest(float x)
    {
        if (layout == 0 || text.Length == 0)
            return 0;
        int trailing, inside;
        HitTestMetrics metrics;
        Check(
            ((delegate* unmanaged<nint, float, float, int*, int*, HitTestMetrics*, int>)Slot(layout, HitTestPoint))(layout, x, Ascent / 2, &trailing, &inside, &metrics),
            "DirectWrite point hit test failed."
        );
        return Math.Clamp((int)metrics.TextPosition + (trailing != 0 ? (int)metrics.Length : 0), 0, text.Length);
    }

    // Mixed-direction ranges produce one rectangle per visual run; each spans the full line height.
    public Rect[] Selection(int start, int end)
    {
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, 0, text.Length);
        if (layout == 0 || start >= end)
            return [];
        var range = (delegate* unmanaged<nint, uint, uint, float, float, HitTestMetrics*, uint, uint*, int>)Slot(layout, HitTestTextRange);
        uint count;
        int result = range(layout, (uint)start, (uint)(end - start), 0, 0, null, 0, &count);
        if (result != NotSufficientBuffer)
            Check(result, "DirectWrite range hit test failed.");
        if (count == 0)
            return [];
        var metrics = new HitTestMetrics[count];
        fixed (HitTestMetrics* p = metrics)
            Check(range(layout, (uint)start, (uint)(end - start), 0, 0, p, count, &count), "DirectWrite range hit test failed.");
        var rects = new Rect[Math.Min(count, (uint)metrics.Length)];
        for (int i = 0; i < rects.Length; i++)
            rects[i] = new(metrics[i].Left, 0, metrics[i].Width, Height);
        return rects;
    }

    public TextRaster Raster(float scale, int slice)
    {
        float left = slice * NativeText.SliceWidth;
        int width = Math.Max(1, (int)MathF.Ceiling(Math.Min(NativeText.SliceWidth, Width - left) * scale) + 4);
        int height = Math.Max(1, (int)MathF.Ceiling(Height * scale) + 4);
        // The baseline lands on a whole device row, as Paint places it. Horizontal positions stay fractional:
        // glyphs keep DirectWrite's subpixel placement and each slice starts exactly at its logical offset.
        float baseline = MathF.Round(Ascent * scale);
        byte[] bytes = layout == 0 || text.Length == 0
            ? new byte[checked(width * height * 4)]
            : Draw(layout, width, height, scale, 2 / scale - left, (2 + baseline) / scale - Ascent);
        // White premultiplied pixels carry coverage in every channel; atlas masks are white with coverage alpha.
        for (int i = 0; i < bytes.Length; i += 4)
            bytes[i] = bytes[i + 1] = bytes[i + 2] = 255;
        return new(bytes, width, height, -2, -baseline - 2);
    }

    public void Dispose()
    {
        Free(layout);
        layout = 0;
    }
}
