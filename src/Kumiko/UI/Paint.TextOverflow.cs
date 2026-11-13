using Kumiko.Rendering;

namespace Kumiko.UI;

internal static partial class Paint
{
    static void PaintOverflow(Context c, int index, Node node, Rect clip, float opacity, float clipRound, List<Instance> instances)
    {
        var box = node.Rect;
        clip = clip.Intersect(box);
        var display = c.TextSystem.Display(node.Text ?? "", node.TextStyle, box.Width, node.RightToLeft, node.Spans);
        if (c.SelectedRange(index) is { } selection)
            foreach (var rect in c.TextSystem.DisplaySelection(display, selection.Start, selection.End, box.Width, node.RightToLeft))
                instances.Add(Instance.Quad(rect with
                {
                    X = box.X + rect.X,
                    Y = box.Y + rect.Y
                },
                    c.Theme.Accent.Alpha(.3f * opacity), clip));
        foreach (var line in display.Lines)
        {
            float origin = node.RightToLeft ? box.Right - line.Width : box.X;
            foreach (var part in line.Parts)
            {
                float x = origin + part.X;
                float y = box.Y + line.Y + line.Ascent - part.Run.Ascent;
                if (part.Span?.Background is { } background)
                    instances.Add(Instance.Quad(new(x, box.Y + line.Y, part.Run.Width, line.Height), background.Alpha(opacity), clip));
                if (part.Span?.Attachment is { } attachment)
                    PaintAttachment(c, attachment, new(x, y, part.Run.Width, part.Run.Height), clip, opacity, instances);
                else
                    DrawRun(c, part.Run, part.Style, x, y, clip, opacity, clipRound, instances);
                if (part.Span is { Underline: true } || part.Span is { Strike: true })
                {
                    float yy = part.Span.Value.Strike ? y + part.Run.Ascent * .6f : y + part.Run.Ascent + 1;
                    instances.Add(Instance.Quad(new(x, yy, part.Run.Width, 1),
                        (part.Style.Color ?? c.Theme.Text).Alpha(opacity), clip));
                }
            }
        }
    }
}
