using System.Globalization;
using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Rendering;

/// <summary>Uniscribe shaping and GDI masks, used only when DirectWrite or Direct2D cannot be created.</summary>
internal sealed unsafe class UniscribeText : INativeTextLayout
{
    readonly string text;
    readonly TextStyle style;
    readonly string? family;
    nint dc, font, oldFont, analysis;
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
    [StructLayout(LayoutKind.Sequential)]
    struct PixelSize
    {
        public int Width, Height;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Metrics
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth, Weight, Overhang, DigitizedX, DigitizedY;
        public char First, Last, Default, Break;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, Charset;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int X, Y;
        public uint Used, Important;
    }
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern int DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern int DeleteObject(nint value);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
        uint underline, uint strike, uint charset, uint outputPrecision, uint clippingPrecision, uint quality, uint pitch, string family);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetTextMetricsW(nint dc, out Metrics metrics);
    [DllImport("gdi32.dll")] static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(nint dc, uint color);
    [DllImport("gdi32.dll")] static extern int GdiFlush();
    [DllImport("usp10.dll", CharSet = CharSet.Unicode)]
    static extern int ScriptStringAnalyse(nint dc, string text, int length, int glyphCount, int charset, uint flags,
        int width, nint control, nint state, nint advances, nint tabs, nint classes, out nint analysis);
    [DllImport("usp10.dll")] static extern nint ScriptString_pSize(nint analysis);
    [DllImport("usp10.dll")] static extern int ScriptStringCPtoX(nint analysis, int position, int trailing, out int x);
    [DllImport("usp10.dll")] static extern int ScriptStringXtoCP(nint analysis, int x, out int position, out int trailing);
    [DllImport("usp10.dll")] static extern int ScriptStringOut(nint analysis, int x, int y, uint options, nint rect, int selectionStart, int selectionEnd, int disabled);
    [DllImport("usp10.dll")] static extern int ScriptStringFree(ref nint analysis);

    public UniscribeText(string text, TextStyle style, string? family)
    {
        this.text = text;
        this.style = style;
        this.family = family;
        try
        {
            dc = CreateCompatibleDC(0);
            font = CreateFontW(-(int)MathF.Round(style.Size), 0, 0, 0,
                style.Weight switch
                {
                    Weight.Bold => 700,
                    Weight.Semibold => 600,
                    Weight.Medium => 500,
                    _ => 400
                },
                style.Italic ? 1u : 0, 0, 0, 1, 0, 0, 4, 0, family ?? (style.Mono ? "Consolas" : "Segoe UI"));
            if (dc == 0 || font == 0)
                throw new InvalidOperationException("Windows text resources could not be created.");
            oldFont = SelectObject(dc, font);
            if (GetTextMetricsW(dc, out var metrics) == 0)
                throw new InvalidOperationException("Windows text metrics could not be read.");
            Ascent = metrics.Ascent;
            Height = (metrics.Height + metrics.ExternalLeading) * style.Leading;
            if (text.Length == 0)
                return;
            int result = ScriptStringAnalyse(dc, text, text.Length, checked(text.Length * 2 + 16), -1,
                0x80 | 0x20 | 0x1000, 0, 0, 0, 0, 0, 0, out analysis);
            if (result != 0)
                Marshal.ThrowExceptionForHR(result);
            var size = *(PixelSize*)ScriptString_pSize(analysis);
            Width = size.Width;
            Height = Math.Max(Height, size.Height * style.Leading);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public float Caret(int position)
    {
        if (analysis == 0)
            return 0;
        position = Math.Clamp(position, 0, text.Length);
        bool atEnd = position == text.Length;
        if (atEnd)
            position = StringInfo.ParseCombiningCharacters(text)[^1];
        int result = ScriptStringCPtoX(analysis, position, atEnd ? 1 : 0, out int x);
        if (result != 0)
            Marshal.ThrowExceptionForHR(result);
        return x;
    }

    public int HitTest(float x)
    {
        if (analysis == 0)
            return 0;
        int result = ScriptStringXtoCP(analysis, (int)MathF.Round(x), out int position, out int trailing);
        if (result != 0)
            Marshal.ThrowExceptionForHR(result);
        return Math.Clamp(position + Math.Max(0, trailing), 0, text.Length);
    }

    public Rect[] Selection(int start, int end)
    {
        if (analysis == 0 || start >= end)
            return [];
        Marshal.ThrowExceptionForHR(ScriptStringCPtoX(analysis, start, 0, out int leading));
        Marshal.ThrowExceptionForHR(ScriptStringCPtoX(analysis, end - 1, 1, out int trailing));
        return [new(Math.Min(leading, trailing), 0, Math.Abs(trailing - leading), Height)];
    }

    public TextRaster Raster(float scale, int slice)
    {
        using var scaled = new UniscribeText(text, style with
        {
            Size = style.Size * scale
        }, family);
        float left = slice * NativeText.SliceWidth * scale;
        int width = Math.Max(1, (int)MathF.Ceiling(Math.Min(NativeText.SliceWidth * scale, scaled.Width - left)) + 4);
        int height = Math.Max(1, (int)MathF.Ceiling(scaled.Height) + 4);
        var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
        var bitmap = CreateDIBSection(scaled.dc, ref info, 0, out var bits, 0, 0);
        if (bitmap == 0 || bits == 0)
            throw new InvalidOperationException("Windows text bitmap allocation failed.");
        var previous = SelectObject(scaled.dc, bitmap);
        var bytes = new byte[checked(width * height * 4)];
        try
        {
            new Span<byte>((void*)bits, bytes.Length).Clear();
            SetBkMode(scaled.dc, 1);
            SetTextColor(scaled.dc, 0xffffff);
            if (scaled.analysis != 0)
            {
                int result = ScriptStringOut(scaled.analysis, 2 - (int)left, 2, 0, 0, 0, 0, 0);
                if (result != 0)
                    Marshal.ThrowExceptionForHR(result);
            }
            GdiFlush();
            Marshal.Copy(bits, bytes, 0, bytes.Length);
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i + 3] = (byte)((bytes[i] + bytes[i + 1] + bytes[i + 2]) / 3);
                bytes[i] = bytes[i + 1] = bytes[i + 2] = 255;
            }
        }
        finally
        {
            SelectObject(scaled.dc, previous);
            DeleteObject(bitmap);
        }
        return new(bytes, width, height, -2, -Ascent * scale - 2);
    }

    public void Dispose()
    {
        if (analysis != 0)
            ScriptStringFree(ref analysis);
        if (dc != 0 && oldFont != 0)
            SelectObject(dc, oldFont);
        if (font != 0)
            DeleteObject(font);
        if (dc != 0)
            DeleteDC(dc);
        font = dc = oldFont = analysis = 0;
    }
}
