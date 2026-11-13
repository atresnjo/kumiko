using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Rendering;

internal sealed unsafe partial class MacText : IDisposable
{
    internal MacText() => NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
    const string CT = "/System/Library/Frameworks/CoreText.framework/CoreText";
    const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    readonly Dictionary<(float, Weight, bool, bool, string?), nint> fonts = new();
    readonly Dictionary<(TextStyle, string?), MacAscii> ascii = new();
    readonly Dictionary<nint, nint> retained = new();
    readonly nint fontKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CT), "kCTFontAttributeName"));
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Range(nint Location, nint Length);
    [DllImport(CT)]
    static extern nint CTFontCreateWithName(nint name, double size, nint matrix);
    [DllImport(CT)]
    static extern nint CTFontCreateCopyWithSymbolicTraits(nint font, double size, nint matrix, uint value, uint mask);
    [DllImport(CT)]
    static extern nint CTFontCopyFamilyName(nint font);
    [DllImport(CT)]
    static extern nint CTFontDescriptorCreateWithAttributes(nint attributes);
    [DllImport(CT)]
    static extern nint CTFontCreateWithFontDescriptor(nint descriptor, double size, nint matrix);
    [DllImport(CT)]
    static extern double CTFontGetAscent(nint font);
    [DllImport(CT)]
    static extern double CTFontGetDescent(nint font);
    [DllImport(CT)]
    static extern double CTFontGetLeading(nint font);
    [DllImport(CT)]
    static extern nint CTLineCreateWithAttributedString(nint str);
    [DllImport(CT)]
    static extern double CTLineGetTypographicBounds(nint line, double* ascent, double* descent, double* leading);
    [DllImport(CT)]
    static extern double CTLineGetOffsetForStringIndex(nint line, nint index, double* secondary);
    [DllImport(CT)]
    static extern nint CTLineGetStringIndexForPosition(nint line, NativePoint point);
    [DllImport(CT)]
    static extern nint CTLineGetGlyphRuns(nint line);
    [DllImport(CT)]
    static extern nint CTRunGetGlyphCount(nint run);
    [DllImport(CT)]
    static extern void CTRunGetGlyphs(nint run, Range range, ushort* glyphs);
    [DllImport(CT)]
    static extern void CTRunGetPositions(nint run, Range range, NativePoint* points);
    [DllImport(CT)]
    static extern nint CTRunGetAttributes(nint run);
    [DllImport(CF)]
    static extern nint CFArrayGetCount(nint array);
    [DllImport(CF)]
    static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(CF)]
    static extern nint CFDictionaryGetValue(nint dict, nint key);
    [DllImport(CF)]
    static extern nint CFRetain(nint value);
    [DllImport(CF)]
    static extern void CFRelease(nint value);
    [DllImport(CT)]
    static extern NativeRect CTFontGetBoundingRectsForGlyphs(nint font, uint orientation, ushort* glyphs, NativeRect* rects, nint count);
    [DllImport(CT)]
    static extern nint CTFontCreateCopyWithAttributes(nint font, double size, nint matrix, nint attributes);
    [DllImport(CT)]
    static extern void CTFontDrawGlyphs(nint font, ushort* glyphs, NativePoint* points, nuint count, nint context);
    [DllImport(CG)]
    static extern nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bits, nuint stride, nint space, uint bitmap);
    [DllImport(CG)]
    static extern void CGContextRelease(nint context);
    [DllImport(CG)]
    static extern void CGContextSetGrayFillColor(nint context, double gray, double alpha);
    [DllImport(CG)]
    static extern void CGContextSetShouldAntialias(nint context, byte value);
    [DllImport(CG)]
    static extern void CGContextSetShouldSmoothFonts(nint context, byte value);
    [DllImport(CG)]
    static extern void CGContextScaleCTM(nint context, double sx, double sy);
    internal nint Font(TextStyle style, string? family)
    {
        var key = (style.Size, style.Weight, style.Mono, style.Italic, family);
        if (fonts.TryGetValue(key, out var font))
            return font;
        using var pool = new AutoreleasePool();
        double weight = style.Weight switch
        {
            Weight.Medium => .23,
            Weight.Semibold => .3,
            Weight.Bold => .4,
            _ => 0
        };
        font = 0;
        if (family is not null)
        {
            nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(NativeLibrary.Load(CT), name));
            var number = ObjC.O(ObjC.Class("NSNumber"), "numberWithDouble:", weight);
            var traits = ObjC.O(ObjC.Class("NSDictionary"), "dictionaryWithObject:forKey:", number, Constant("kCTFontWeightTrait"));
            nint* keys = stackalloc nint[2]
            {
                Constant("kCTFontFamilyNameAttribute"),
                Constant("kCTFontTraitsAttribute")
            };
            nint* values = stackalloc nint[2]
            {
                ObjC.TempString(family),
                traits
            };
            var attrs = ObjC.O(ObjC.Class("NSDictionary"), "dictionaryWithObjects:forKeys:count:", (nint)values, (nint)keys, (nuint)2);
            var descriptor = CTFontDescriptorCreateWithAttributes(attrs);
            try
            {
                font = CTFontCreateWithFontDescriptor(descriptor, style.Size, 0);
            }
            finally
            {
                CFRelease(descriptor);
            }

            var actual = CTFontCopyFamilyName(font);
            try
            {
                if (!string.Equals(ObjC.Text(actual), family, StringComparison.OrdinalIgnoreCase))
                {
                    CFRelease(font);
                    font = 0;
                }
            }
            finally
            {
                CFRelease(actual);
            }
        }

        if (font == 0)
            font = CFRetain(ObjC.O(
                ObjC.Class("NSFont"),
                style.Mono ? "monospacedSystemFontOfSize:weight:" : "systemFontOfSize:weight:",
                (double)style.Size,
                weight
            ));
        if (style.Italic)
        {
            var copy = CTFontCreateCopyWithSymbolicTraits(font, style.Size, 0, 1, 1);
            if (copy != 0)
            {
                CFRelease(font);
                font = copy;
            }
        }

        fonts[key] = font;
        return font;
    }

    internal float LineHeight(nint font, float leading) => MathF.Ceiling((float)(CTFontGetAscent(font) + CTFontGetDescent(font) + CTFontGetLeading(font))) * leading;
    internal float Ascent(nint font) => (float)CTFontGetAscent(font);
    internal ShapedRun Shape(string text, TextStyle style, string? family, bool fast)
    {
        if (fast)
        {
            var key = (style, family);
            if (!ascii.TryGetValue(key, out var table))
                ascii[key] = table = new(this, style, family);
            if (table.Shape(text) is { } result)
                return result;
        }

        return ShapeNative(text, style, family);
    }

    internal ShapedRun ShapeNative(string text, TextStyle style, string? family)
    {
        using var pool = new AutoreleasePool();
        var font = Font(style, family);
        nint key = fontKey, value = font;
        var attrs = ObjC.O(ObjC.Class("NSDictionary"), "dictionaryWithObjects:forKeys:count:", (nint)(&value), (nint)(&key), (nuint)1);
        var str = ObjC.O(ObjC.O(ObjC.Class("NSAttributedString"), "alloc"), "initWithString:attributes:", ObjC.TempString(text), attrs);
        var line = CTLineCreateWithAttributedString(str);
        ObjC.Release(str);
        try
        {
            var list = new List<ShapedGlyph>(text.Length);
            var runs = CTLineGetGlyphRuns(line);
            for (nint i = 0; i < CFArrayGetCount(runs); i++)
            {
                var run = CFArrayGetValueAtIndex(runs, i);
                var count = checked((int)CTRunGetGlyphCount(run));
                var actualFont = CFDictionaryGetValue(CTRunGetAttributes(run), fontKey);
                if (!retained.ContainsKey(actualFont))
                    retained[actualFont] = CFRetain(actualFont);
                var glyphs = new ushort[count];
                var points = new NativePoint[count];
                fixed (ushort* g = glyphs)
                fixed (NativePoint* p = points)
                {
                    CTRunGetGlyphs(run, default, g);
                    CTRunGetPositions(run, default, p);
                }

                for (int j = 0; j < count; j++)
                    list.Add(new(actualFont, glyphs[j], (float)points[j].X, -(float)points[j].Y));
            }

            return new((float)CTLineGetTypographicBounds(line, null, null, null), LineHeight(font, style.Leading), Ascent(font), list.ToArray());
        }
        finally
        {
            CFRelease(line);
        }
    }

    nint CreateLine(string text, TextStyle style, string? family)
    {
        nint key = fontKey, value = Font(style, family);
        var attrs = ObjC.O(ObjC.Class("NSDictionary"), "dictionaryWithObjects:forKeys:count:", (nint)(&value), (nint)(&key), (nuint)1);
        var str = ObjC.O(ObjC.O(ObjC.Class("NSAttributedString"), "alloc"), "initWithString:attributes:", ObjC.TempString(text), attrs);
        try
        {
            return CTLineCreateWithAttributedString(str);
        }
        finally
        {
            ObjC.Release(str);
        }
    }

    internal float Caret(string text, TextStyle style, string? family, int position)
    {
        using var pool = new AutoreleasePool();
        var line = CreateLine(text, style, family);
        try
        {
            return (float)CTLineGetOffsetForStringIndex(line, position, null);
        }
        finally
        {
            CFRelease(line);
        }
    }

    internal int HitTest(string text, TextStyle style, string? family, float x)
    {
        using var pool = new AutoreleasePool();
        var line = CreateLine(text, style, family);
        try
        {
            return (int)Math.Clamp(CTLineGetStringIndexForPosition(line, new(x, 0)), 0, text.Length);
        }
        finally
        {
            CFRelease(line);
        }
    }

    /// <summary>
    /// Rasterizes one glyph at <paramref name="size"/> points through a <paramref name="scale"/> transform, so the
    /// font keeps the optical size of its point size. The outline is shifted <paramref name="offset"/> device pixels
    /// right of the returned integer bearing. Font smoothing is off: macOS smoothing thickens stems, and per-glyph
    /// bitmaps receive more of it than whole lines, which made text read a weight heavier than requested.
    /// </summary>
    internal (byte[] Bytes, int Width, int Height, float X, float Y) Raster(nint source, ushort glyph, float size, float scale, float offset = 0)
    {
        var font = CTFontCreateCopyWithAttributes(source, size, 0, 0);
        try
        {
            var bounds = CTFontGetBoundingRectsForGlyphs(font, 0, &glyph, null, 1);
            double left = bounds.Origin.X * scale, bottom = bounds.Origin.Y * scale;
            double right = (bounds.Origin.X + bounds.Size.Width) * scale, top = (bounds.Origin.Y + bounds.Size.Height) * scale;
            int x = (int)Math.Floor(left) - 1, y = (int)Math.Floor(bottom) - 1;
            int w = (int)Math.Ceiling(right + offset) - x + 1, h = (int)Math.Ceiling(top) - y + 1;
            if (w <= 0 || h <= 0 || w > 4096 || h > 4096)
                return ([], 0, 0, 0, 0);
            var mask = new byte[w * h];
            fixed (byte* p = mask)
            {
                var ctx = CGBitmapContextCreate(p, (nuint)w, (nuint)h, 8, (nuint)w, 0, 7);
                if (ctx == 0)
                    throw new InvalidOperationException("CoreGraphics bitmap allocation failed.");
                CGContextSetGrayFillColor(ctx, 1, 1);
                CGContextSetShouldAntialias(ctx, 1);
                CGContextSetShouldSmoothFonts(ctx, 0);
                CGContextScaleCTM(ctx, scale, scale);
                var point = new NativePoint((-x + offset) / scale, -y / scale);
                CTFontDrawGlyphs(font, &glyph, &point, 1, ctx);
                CGContextRelease(ctx);
            }

            var rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
                rgba[i * 4 + 3] = mask[i];
            }

            return (rgba, w, h, x, -y - h);
        }
        finally
        {
            CFRelease(font);
        }
    }

    public void Dispose()
    {
        foreach (var font in retained.Values)
            CFRelease(font);
        foreach (var font in fonts.Values)
            CFRelease(font);
        retained.Clear();
        fonts.Clear();
    }
}
