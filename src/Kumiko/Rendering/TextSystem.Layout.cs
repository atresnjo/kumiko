using Kumiko.UI;

namespace Kumiko.Rendering;

public sealed partial class TextSystem
{
    /// <summary>Locates a caret in a wrapped, multiline text block.</summary>
    public Rect CaretRectangle(string text, TextStyle style, int position, float width)
    {
        position = Snap(text, position);
        int offset = 0;
        float y = 0;
        foreach (var line in Lines(text, style, width))
        {
            int end = offset + line.Text.Length;
            if (position <= end)
                return new(CaretPosition(line.Text, style, position - offset), y, 1, line.Run.Height);
            offset = end;
            if (offset < text.Length && text[offset] == '\n')
                offset++;
            y += line.Run.Height;
        }
        return new(0, y, 1, Measure("M", style).Height);
    }

    /// <summary>Hit tests wrapped text, returning a UTF-16 grapheme boundary.</summary>
    public int HitTest(string text, TextStyle style, Point point, float width)
    {
        int offset = 0;
        float y = 0;
        foreach (var line in Lines(text, style, width))
        {
            if (point.Y < y + line.Run.Height)
                return offset + HitTest(line.Text, style, point.X);
            offset += line.Text.Length;
            if (offset < text.Length && text[offset] == '\n')
                offset++;
            y += line.Run.Height;
        }
        return text.Length;
    }

    /// <summary>Returns selected visual rectangles, one per intersected line.</summary>
    public IEnumerable<Rect> SelectionRectangles(string text, TextStyle style, int start, int end, float width)
    {
        start = Snap(text, start);
        end = Snap(text, end);
        int offset = 0;
        float y = 0;
        foreach (var line in Lines(text, style, width))
        {
            int lineEnd = offset + line.Text.Length;
            if (end > offset && start <= lineEnd)
            {
                float a = CaretPosition(line.Text, style, Math.Clamp(start - offset, 0, line.Text.Length));
                float b = CaretPosition(line.Text, style, Math.Clamp(end - offset, 0, line.Text.Length));
                yield return new(Math.Min(a, b), y, Math.Max(1, Math.Abs(a - b)), line.Run.Height);
            }
            offset = lineEnd;
            if (offset < text.Length && text[offset] == '\n')
                offset++;
            y += line.Run.Height;
        }
    }
}
