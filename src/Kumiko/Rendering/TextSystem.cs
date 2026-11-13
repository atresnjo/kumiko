using System.Globalization;
using System.Text;
using Kumiko.UI;

namespace Kumiko.Rendering;

internal readonly record struct ShapedGlyph(nint Font, ushort Glyph, float X, float Y);
internal sealed record ShapedRun(float Width, float Height, float Ascent, ShapedGlyph[] Glyphs, INativeTextLayout? Native = null);
internal readonly record struct TextLine(string Text, ShapedRun Run);
public sealed partial class TextSystem : IDisposable
{
    internal readonly record struct Segment(string Text, TextStyle Style, TextSpan? Span);
    internal static IEnumerable<Segment> Segments(string text, TextStyle style, TextSpan[] spans, int offset = 0)
    {
        int cursor = 0;
        foreach (var span in spans)
        {
            int start = Math.Clamp(span.Start - offset, 0, text.Length), end = Math.Clamp(span.End - offset, 0, text.Length);
            if (end <= start)
                continue;
            if (start > cursor)
                yield return new(text[cursor..start], style, null);
            var attributed = style with
            {
                Color = span.Color ?? style.Color,
                Weight = span.Weight ?? style.Weight,
                Italic = span.Italic || style.Italic,
                Mono = span.Mono || style.Mono
            };
            yield return new(text[start..end], attributed, span);
            cursor = end;
        }

        if (cursor < text.Length)
            yield return new(text[cursor..], style, null);
    }

    readonly MacText? mac = OperatingSystem.IsMacOS() ? new() : null;
    readonly NativeText? native = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() ? new() : null;
    public string Backend => mac is not null ? "CoreText" : OperatingSystem.IsWindows() ? DirectWrite.Backend : native is not null ? "Pango/HarfBuzz" : "Managed TrueType";
    readonly Dictionary<(bool, bool), TrueType> fonts = new();
    Dictionary<(string, TextStyle, string?), ShapedRun> layouts = new(), previousLayouts = new();
    bool fast = true;
    public bool AsciiFastPathEnabled
    {
        get => fast;
        set
        {
            if (fast == value)
                return;
            fast = value;
            layouts.Clear();
            previousLayouts.Clear();
        }
    }

    internal long FrameId
    {
        get; private set;
    }
    internal void BeginFrame()
    {
        FrameId++;
        if (native is null)
            (displays, previousDisplays) = (previousDisplays, displays);
        else
            previousDisplays.Clear();
        displays.Clear();
        if (Atlas.Width >= 4096)
        {
            Atlas.Reset();
            glyphs.Clear();
            images.Clear();
            symbols.Clear();
        }
        if (native is not null)
        {
            var live = layouts.Values.ToArray();
            previousLayouts.Clear();
            native.Retain(live);
        }
        (layouts, previousLayouts) = (previousLayouts, layouts);
        layouts.Clear();
    }

    readonly Dictionary<(nint, ushort, float, float, float), Sprite> glyphs = new();
    readonly Dictionary<Image, Sprite> images = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<(string, float, float), Sprite> symbols = new();
    public Atlas Atlas { get; } = new();
    public float Scale { get; internal set; } = 1;
    string? family, monoFamily;
    public string? Family
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return family;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            family = value;
        }
    }
    public string? MonoFamily
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return monoFamily;
        }
        set
        {
            Runtime.KumikoExecution.CheckUI();
            monoFamily = value;
        }
    }

    static TextStyle Normalize(TextStyle s) => s with
    {
        Size = s.Size <= 0 ? 14 : s.Size,
        Leading = s.Leading <= 0 ? 1 : s.Leading,
        Color = null,
        Wrap = false,
        Overflow = TextOverflow.Visible
    };
    internal ShapedRun Shape(string text, TextStyle style)
    {
        style = Normalize(style);
        string? family = style.Mono ? MonoFamily : Family;
        var key = (text, style, family);
        if (layouts.TryGetValue(key, out var run))
            return run;
        if (previousLayouts.TryGetValue(key, out run))
        {
            layouts[key] = run;
            return run;
        }

        if (mac is not null)
            run = mac.Shape(text, style, family, fast);
        else if (native is not null)
            run = native.Shape(text, style, family);
        else
        {
            var fontKey = (style.Mono, style.Weight >= Weight.Semibold);
            if (!fonts.TryGetValue(fontKey, out var font))
                fonts[fontKey] = font = new(TrueType.FindFont(fontKey.Item1, fontKey.Item2));
            var fontId = (nint)((fontKey.Item1 ? 2 : 0) + (fontKey.Item2 ? 1 : 0) + 1);
            var list = new List<ShapedGlyph>(text.Length);
            float x = 0;
            ushort previous = 0;
            foreach (var rune in text.EnumerateRunes())
            {
                var glyph = font.Glyph(rune.Value);
                x += font.Kern(previous, glyph) * style.Size;
                list.Add(new(fontId, glyph, x, 0));
                x += font.Advance(glyph) * style.Size;
                previous = glyph;
            }

            run = new(
                x,
                MathF.Ceiling((font.Ascent + font.Descent + font.LineGap) * style.Size) * style.Leading,
                font.Ascent * style.Size,
                list.ToArray()
            );
        }

        layouts[key] = run;
        return run;
    }

    internal IEnumerable<TextLine> Lines(string text, TextStyle style, float width)
    {
        foreach (var paragraph in text.Split('\n'))
        {
            if (!style.Wrap || float.IsPositiveInfinity(width) || Shape(paragraph, style).Width <= width)
            {
                yield return new(paragraph, Shape(paragraph, style));
                continue;
            }

            var starts = StringInfo.ParseCombiningCharacters(paragraph);
            int from = 0;
            while (from < paragraph.Length)
            {
                int end = from, lastBreak = -1;
                foreach (int i in starts)
                {
                    if (i < from)
                        continue;
                    int next = i + StringInfo.GetNextTextElementLength(paragraph, i);
                    if (end > from && Shape(paragraph[from..next], style).Width > width)
                        break;
                    end = next;
                    if (char.IsWhiteSpace(paragraph[i]))
                        lastBreak = end;
                }

                if (end < paragraph.Length && lastBreak > from)
                    end = lastBreak;
                if (end <= from)
                    end = from + StringInfo.GetNextTextElementLength(paragraph, from);
                var line = paragraph[from..end];
                yield return new(line, Shape(line, style));
                from = end;
            }
        }
    }

    public Size Measure(string text, TextStyle style, float width = float.PositiveInfinity)
    {
        if (!style.Wrap && !text.Contains('\n'))
        {
            var run = Shape(text, style);
            return new(MathF.Ceiling(run.Width), run.Height);
        }

        float w = 0, h = 0;
        foreach (var line in Lines(text, style, width))
        {
            w = Math.Max(w, line.Run.Width);
            h += line.Run.Height;
        }

        return new(MathF.Ceiling(w), h);
    }

    /// <summary>Horizontal positions per device pixel that glyph bitmaps are rasterized at.</summary>
    internal const int SubpixelSteps = 4;

    /// <summary>True when <see cref="Glyph"/> honors a fractional offset; other rasterizers draw at whole pixels.</summary>
    internal bool Subpixel => native is null && mac is not null;

    /// <summary>
    /// A glyph bitmap whose outline starts <paramref name="offset"/> device pixels (0 to 1, in quarter steps)
    /// to the right of its integer bearing. Rasterizers without subpixel positioning ignore the offset.
    /// </summary>
    internal Sprite Glyph(ShapedGlyph glyph, TextStyle style, float offset = 0)
    {
        style = Normalize(style);
        if (!Subpixel)
            offset = 0;
        var key = (glyph.Font, glyph.Glyph, style.Size, Scale, offset);
        if (glyphs.TryGetValue(key, out var sprite))
            return sprite;
        if (native is not null)
        {
            var raster = native.Raster(glyph, Scale);
            sprite = Atlas.Insert(raster.Bytes, raster.Width, raster.Height, raster.X, raster.Y);
        }
        else if (mac is not null)
        {
            var raster = mac.Raster(glyph.Font, glyph.Glyph, style.Size, Scale, offset);
            sprite = Atlas.Insert(raster.Bytes, raster.Width, raster.Height, raster.X, raster.Y);
        }
        else
        {
            int id = (int)glyph.Font - 1;
            var font = fonts[((id & 2) != 0, (id & 1) != 0)];
            var raster = font.Raster(glyph.Glyph, style.Size * Scale);
            sprite = Atlas.Insert(raster.Rgba, raster.Width, raster.Height, raster.X, raster.Y);
        }

        glyphs[key] = sprite;
        return sprite;
    }

    internal Sprite Picture(Image image)
    {
        if (images.TryGetValue(image, out var sprite))
            return sprite;
        sprite = Atlas.Insert(image.Pixels.Bytes, image.Pixels.Width, image.Pixels.Height);
        images[image] = sprite;
        return sprite;
    }

    internal Sprite Symbol(string name, float size)
    {
        var key = (name, size, Scale);
        if (symbols.TryGetValue(key, out var sprite))
            return sprite;
        if (SvgIcons.Raster(name, size, Scale) is { } svg)
        {
            sprite = Atlas.Insert(svg.Bytes, svg.Width, svg.Height);
            symbols[key] = sprite;
            return sprite;
        }
        if (mac is not null && MacImages.Symbol(name, size, Scale) is { } native)
        {
            sprite = Atlas.Insert(native.Bytes, native.Width, native.Height);
            symbols[key] = sprite;
            return sprite;
        }

        var raster = Icons.Raster(name, size, Scale);
        sprite = Atlas.Insert(raster.Bytes, raster.Width, raster.Height);
        symbols[key] = sprite;
        return sprite;
    }

    internal void TrimCache()
    {
        if (layouts.Count > 4096)
            layouts.Clear();
    }

    public void Dispose()
    {
        displays.Clear();
        previousDisplays.Clear();
        mac?.Dispose();
        native?.Dispose();
        layouts.Clear();
        previousLayouts.Clear();
        glyphs.Clear();
        images.Clear();
    }
}
