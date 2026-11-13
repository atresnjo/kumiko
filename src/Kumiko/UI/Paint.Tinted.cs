using Kumiko.Rendering;

namespace Kumiko.UI;

internal static partial class Paint
{
    /// <summary>
    /// Paints a single unwrapped line whose spans only change colours and backgrounds, such as syntax-highlighted
    /// code, from one shaped run of the whole line. Printable ASCII shapes one glyph per character, so a span's
    /// range maps directly onto glyphs: no per-span substrings or runs, and kerning matches the measured line.
    /// Returns false for anything else, which paints through the general span path.
    /// </summary>
    static bool PaintTinted(Context c, Node n, Rect clip, float opacity, float clipRound, List<Instance> instances)
    {
        var text = n.Text ?? "";
        var style = n.TextStyle;
        var spans = n.Spans!;
        if (style.Wrap || n.RightToLeft || text.Length == 0)
            return false;
        foreach (var ch in text)
            if (ch < 32 || ch > 126)
                return false;
        foreach (var span in spans)
            if (span.Weight is not null || span.Italic || span.Mono && !style.Mono || span.Underline || span.Strike || span.Attachment is not null)
                return false;
        var run = c.TextSystem.Shape(text, style);
        if (run.Glyphs.Length != text.Length)
            return false;
        float x = n.Rect.X, y = n.Rect.Y;
        foreach (var span in spans)
        {
            if (span.Background is not { } background || span.End <= span.Start || span.Start >= text.Length)
                continue;
            float left = run.Glyphs[span.Start].X;
            float right = span.End < text.Length ? run.Glyphs[span.End].X : run.Width;
            instances.Add(Instance.Quad(new(x + left, y, right - left, run.Height), background.Alpha(opacity), clip));
        }
        DrawRun(c, run, style, x, y, clip, opacity, clipRound, instances, spans);
        return true;
    }
}
