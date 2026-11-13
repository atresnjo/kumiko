using System.Globalization;
using Kumiko.UI;

namespace Kumiko.Rendering;

// Display runs retain source offsets. The ellipsis is a separate shaped token;
// node text, accessibility and clipboard stay intact.
internal sealed record DisplayPart(string Text, TextStyle Style, TextSpan? Span, ShapedRun Run,
    int SourceStart, int SourceLength, bool Ellipsis, float X);
internal sealed record DisplayLine(DisplayPart[] Parts, int SourceStart, int SourceEnd,
    float Width, float Height, float Ascent, float Y);
internal sealed record TextDisplay(DisplayLine[] Lines, Size Size);

public sealed partial class TextSystem
{
    readonly record struct DisplayKey(string Text, TextStyle Style, float Width, bool RightToLeft,
        TextSpan[]? Spans, string? Family, string? MonoFamily, float Scale, bool Fast);
    // Frame-local where runs own native layouts (released by BeginFrame). CoreText runs own none, so macOS keeps
    // the previous frame's displays: unchanged labels are not segmented and measured again every frame.
    // Keys compare spans by content and own a copy, so a caller rewriting its array still misses.
    Dictionary<DisplayKey, TextDisplay> displays = new(DisplayKeys.Instance), previousDisplays = new(DisplayKeys.Instance);

    sealed class DisplayKeys : IEqualityComparer<DisplayKey>
    {
        internal static readonly DisplayKeys Instance = new();

        public bool Equals(DisplayKey a, DisplayKey b) => a.Text == b.Text && a.Style == b.Style && a.Width.Equals(b.Width) &&
            a.RightToLeft == b.RightToLeft && a.Family == b.Family && a.MonoFamily == b.MonoFamily && a.Scale.Equals(b.Scale) &&
            a.Fast == b.Fast && a.Spans.AsSpan().SequenceEqual(b.Spans.AsSpan());

        public int GetHashCode(DisplayKey key) => HashCode.Combine(key.Text, key.Style, key.Width, key.RightToLeft, key.Spans?.Length ?? 0,
            key.Spans is { Length: > 0 } spans ? spans[0].GetHashCode() : 0, key.Family, key.Scale);
    }

    internal TextDisplay Display(string text, TextStyle style, float width, bool rightToLeft, TextSpan[]? spans = null)
    {
        var key = new DisplayKey(text, style, width, rightToLeft, spans, Family, MonoFamily, Scale, fast);
        if (displays.TryGetValue(key, out var cached))
            return cached;
        if (previousDisplays.Remove(key, out cached))
        {
            displays[key] = cached;
            return cached;
        }
        var lines = new List<DisplayLine>();
        float y = 0;
        float maximum = 0;
        int offset = 0;
        // Wrapped clipping retains the existing line-breaking contract. Ellipsis
        // is deliberately limited to unwrapped lines until MaxLines is defined.
        foreach (var source in spans is { Length: > 0 } ? RichLines(text, style, width, spans) : Lines(text, style, width))
        {
            int visibleLength = style.Wrap && rightToLeft ? source.Text.TrimEnd().Length : source.Text.Length;
            var full = DisplayLine(source.Text, style, spans, offset, visibleLength, false, rightToLeft, y, width);
            var line = full;
            if (style.Overflow == TextOverflow.Ellipsis && full.Width > width)
            {
                var boundaries = StringInfo.ParseCombiningCharacters(source.Text);
                var marker = DisplayLine(source.Text, style, spans, offset, 0, true, rightToLeft, y);
                if (marker.Width > width || width <= 0)
                    line = full with
                    {
                        Parts = [],
                        Width = 0
                    };
                else
                {
                    line = marker;
                    int low = 0;
                    int high = boundaries.Length - 1;
                    while (low < high)
                    {
                        int middle = (low + high + 1) / 2;
                        var candidate = DisplayLine(source.Text, style, spans, offset, boundaries[middle], true, rightToLeft, y);
                        // Shape the retained text again at the cut and include the
                        // independently shaped marker's actual advance.
                        if (candidate.Width <= width)
                        {
                            low = middle;
                            line = candidate;
                        }
                        else
                            high = middle - 1;
                    }
                    // Removing a tall fallback glyph must not collapse the line.
                    line = line with
                    {
                        Height = full.Height,
                        Ascent = full.Ascent
                    };
                }
            }
            lines.Add(line);
            maximum = Math.Max(maximum, Math.Min(Math.Max(0, width), line.Width));
            y += line.Height;
            offset += source.Text.Length;
            if (offset < text.Length && text[offset] == '\n')
                offset++;
        }
        var result = new TextDisplay(lines.ToArray(), new(maximum, y));
        if (displays.Count >= 512)
            displays.Clear();
        displays[key with
        {
            Spans = spans?.ToArray()
        }] = result;
        return result;
    }

    DisplayLine DisplayLine(string source, TextStyle style, TextSpan[]? spans, int offset, int length,
        bool ellipsis, bool rightToLeft, float y, float available = float.PositiveInfinity)
    {
        var prefix = source[..length];
        var segments = spans is { Length: > 0 } ? Segments(prefix, style, spans, offset).ToArray()
            : [new Segment(prefix, style, null)];
        if (segments.Length == 0)
            segments = [new Segment("", style, null)];
        var parts = new DisplayPart[segments.Length + (ellipsis ? 1 : 0)];
        var metrics = Shape(source, style);
        float height = metrics.Height;
        float ascent = metrics.Ascent;
        float width = 0;
        int cursor = offset;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var run = Shape(segment.Text, segment.Style);
            if (segment.Span?.Attachment is { } attachment)
            {
                float scale = Math.Min(1, Math.Max(1, available) / attachment.Width);
                float h = attachment.Height * scale;
                run = new(attachment.Width * scale, h, h, []);
            }
            parts[i] = new(segment.Text, segment.Style, segment.Span, run, cursor, segment.Text.Length, false, width);
            width += run.Width;
            height = Math.Max(height, run.Height);
            ascent = Math.Max(ascent, run.Ascent);
            cursor += segment.Text.Length;
        }
        if (ellipsis)
        {
            // A separate token avoids introducing direction-control characters
            // into the source run (which can change Unicode normalization and
            // shaping). Its position is the paragraph's visual trailing edge.
            var last = segments[^1];
            var marker = Shape("…", last.Style);
            parts[^1] = new("…", last.Style, last.Span, marker, cursor, 0, true, width);
            width += marker.Width;
            height = Math.Max(height, marker.Height);
            ascent = Math.Max(ascent, marker.Ascent);
        }
        if (rightToLeft)
            for (var i = 0; i < parts.Length; i++)
                parts[i] = parts[i] with
                {
                    X = width - parts[i].X - parts[i].Run.Width
                };
        return new(parts, offset, offset + source.Length, width, height, ascent, y);
    }

    internal Size MeasureDisplay(string text, TextStyle style, TextSpan[]? spans = null)
    {
        var size = Display(text, style, float.PositiveInfinity, false, spans).Size;
        return new(MathF.Ceiling(size.Width), size.Height);
    }
}
