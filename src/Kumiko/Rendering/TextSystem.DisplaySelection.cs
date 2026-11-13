using System.Globalization;
using Kumiko.UI;

namespace Kumiko.Rendering;

public sealed partial class TextSystem
{
    internal int DisplayHitTest(TextDisplay display, Point point, float width, bool rightToLeft)
    {
        if (display.Lines.Length == 0)
            return 0;
        var line = display.Lines.FirstOrDefault(l => point.Y < l.Y + l.Height) ?? display.Lines[^1];
        float x = point.X - (rightToLeft ? width - line.Width : 0);
        if (x <= 0)
            return rightToLeft ? line.SourceEnd : line.SourceStart;
        if (x >= line.Width)
            return rightToLeft ? line.SourceStart : line.SourceEnd;
        foreach (var part in line.Parts)
        {
            if (x < part.X || x > part.X + part.Run.Width)
                continue;
            if (part.Ellipsis)
                return line.SourceEnd;
            int position = part.Span?.Attachment is not null ? x - part.X < part.Run.Width / 2 ? 0 : part.SourceLength : HitTest(part.Text, part.Style, x - part.X);
            return part.SourceStart + Math.Clamp(position, 0, part.SourceLength);
        }
        return line.SourceStart;
    }

    internal IEnumerable<Rect> DisplaySelection(TextDisplay display, int start, int end, float width, bool rightToLeft)
    {
        foreach (var line in display.Lines)
        {
            float origin = rightToLeft ? width - line.Width : 0;
            foreach (var part in line.Parts)
            {
                var source = part.Text[..part.SourceLength];
                var boundaries = StringInfo.ParseCombiningCharacters(source).Append(source.Length).ToArray();
                for (var i = 0; i + 1 < boundaries.Length; i++)
                {
                    int from = part.SourceStart + boundaries[i];
                    int to = part.SourceStart + boundaries[i + 1];
                    if (end <= from || start >= to)
                        continue;
                    foreach (var range in part.Span?.Attachment is not null ? new[] { new Rect(0, 0, part.Run.Width, line.Height) } :
                        GraphemeSelection(part.Text, part.Style, boundaries[i], boundaries[i + 1]))
                        yield return new(origin + part.X + range.X, line.Y, range.Width, line.Height);
                }
                if (part.Ellipsis && end > part.SourceStart + part.SourceLength && start < line.SourceEnd)
                {
                    yield return new(origin + part.X, line.Y, part.Run.Width, line.Height);
                }
            }
        }
    }

    Rect[] GraphemeSelection(string text, TextStyle style, int start, int end)
    {
        var run = Shape(text, style);
        if (run.Native is { } native)
            return native.Selection(start, end);
        if (mac is not null && text.Any(ch => ch > 127))
            return mac.Selection(text, Normalize(style), style.Mono ? MonoFamily : Family, start, end);
        float a = CaretPosition(text, style, start), b = CaretPosition(text, style, end);
        return [new(Math.Min(a, b), 0, Math.Abs(b - a), run.Height)];
    }
}
