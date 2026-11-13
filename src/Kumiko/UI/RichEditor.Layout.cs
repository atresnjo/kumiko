using Kumiko.Rendering;
using Kumiko.RichText;

namespace Kumiko.UI;

internal sealed class RichEditorLayout(RichValue value, float width, TextStyle body)
{
    internal readonly RichValue Value = value;
    internal readonly float Width = width;
    internal readonly TextStyle Body = body;
    internal readonly float[] Heights = Enumerable.Repeat(Math.Max(24, body.Size * 1.5f), value.Paragraphs.Count).ToArray();
    internal readonly float[] Tops = new float[value.Paragraphs.Count + 1];
    readonly Dictionary<int, string> texts = [];
    readonly Dictionary<int, bool> directions = [];
    readonly Dictionary<int, TextSpan[]> spans = [];
    readonly Dictionary<int, TextDisplay> displays = [];
    readonly Dictionary<(int Paragraph, int Position, RichAffinity Affinity), Rect> carets = [];
    readonly Dictionary<(int Paragraph, int Line), RichSelection[]> stops = [];
    Theme? spanTheme;
    long frame = -1;
    string? family, monoFamily;
    float scale;
    string Text(int paragraph)
    {
        if (!texts.TryGetValue(paragraph, out var text))
            texts[paragraph] = text = Value.Paragraphs[paragraph].Text;
        return text;
    }
    void Frame(Context context)
    {
        if (frame == context.TextSystem.FrameId)
            return;
        frame = context.TextSystem.FrameId;
        displays.Clear();
        carets.Clear();
        stops.Clear();
        if (family != context.TextSystem.Family || monoFamily != context.TextSystem.MonoFamily || scale != context.TextSystem.Scale)
        {
            family = context.TextSystem.Family;
            monoFamily = context.TextSystem.MonoFamily;
            scale = context.TextSystem.Scale;
            Array.Clear(measured);
        }
    }
    readonly bool[] measured = new bool[value.Paragraphs.Count];
    internal void Positions()
    {
        for (int i = 0; i < Heights.Length; i++)
            Tops[i + 1] = Tops[i] + Heights[i];
    }
    internal int At(float y)
    {
        int index = Array.BinarySearch(Tops, Math.Max(0, y));
        return Math.Clamp(index < 0 ? ~index - 1 : index, 0, Heights.Length - 1);
    }
    internal TextStyle Style(int paragraph)
    {
        var style = Value.Paragraphs[paragraph].Style;
        return Body with
        {
            Size = style.Heading > 0 ? Body.Size * (1.7f - style.Heading * .1f) : Body.Size,
            Weight = style.Heading > 0 ? Weight.Bold : Body.Weight,
            Mono = style.Code,
            Wrap = true,
            Overflow = TextOverflow.Clip
        };
    }
    internal float Inset(int paragraph) => Value.Paragraphs[paragraph].Style.Indent * 20 +
        (Value.Paragraphs[paragraph].Style.List != RichList.None || Value.Paragraphs[paragraph].Style.Quote ? 28 : 0);
    internal TextSpan[] Spans(int paragraph, Theme theme)
    {
        if (spanTheme != theme)
        {
            spans.Clear();
            spanTheme = theme;
        }
        if (spans.TryGetValue(paragraph, out var cached))
            return cached;
        var result = new List<TextSpan>();
        int offset = 0;
        foreach (var run in Value.Paragraphs[paragraph].Runs)
        {
            var a = run.Attributes;
            result.Add(new(offset, offset + run.Text.Length, a.Link is not null ? theme.Text : null,
                a.Bold ? Weight.Bold : null, a.Italic, a.Code, a.Code ? theme.Surface : null, a.Underline || a.Link is not null, a.Strike)
            {
                Attachment = a.Attachment
            });
            offset += run.Text.Length;
        }
        return spans[paragraph] = result.ToArray();
    }
    internal bool RightToLeft(int paragraph)
    {
        if (!directions.TryGetValue(paragraph, out bool rtl))
            directions[paragraph] = rtl = Text(paragraph).EnumerateRunes().FirstOrDefault(r =>
                r.Value is >= 0x590 and <= 0x8ff || System.Text.Rune.IsLetter(r)).Value is >= 0x590 and <= 0x8ff;
        return rtl;
    }
    internal TextDisplay Display(Context context, int paragraph)
    {
        Frame(context);
        if (!displays.TryGetValue(paragraph, out var display))
            displays[paragraph] = display = context.TextSystem.Display(Text(paragraph), Style(paragraph),
                Math.Max(1, Width - Inset(paragraph)), RightToLeft(paragraph), Spans(paragraph, context.Theme));
        return display;
    }
    internal void Measure(Context context, int first, int last)
    {
        Frame(context);
        for (int i = first; i <= last; i++)
        {
            if (measured[i])
                continue;
            Heights[i] = Math.Max(24, Display(context, i).Size.Height + 8);
            measured[i] = true;
        }
        Positions();
    }
    internal float Alignment(Context context, int paragraph)
    {
        float remaining = Math.Max(0, Width - Inset(paragraph) - Display(context, paragraph).Size.Width);
        return Value.Paragraphs[paragraph].Style.Alignment switch
        {
            RichAlignment.Center => remaining / 2,
            RichAlignment.End => remaining,
            _ => 0
        };
    }
    static int LineAt(TextDisplay display, int position, RichAffinity affinity)
    {
        for (int i = 0; i < display.Lines.Length - 1; i++)
        {
            var line = display.Lines[i];
            if (position < line.SourceEnd || position == line.SourceEnd &&
                (affinity == RichAffinity.Upstream || display.Lines[i + 1].SourceStart != position))
                return i;
        }
        return display.Lines.Length - 1;
    }
    RichSelection Position(int paragraph, DisplayLine line, int local, TextDisplay display)
    {
        int position = Value.Snap(Value.ParagraphStarts[paragraph] + local);
        bool sharedEnd = local == line.SourceEnd && display.Lines.Any(l => l.SourceStart == local && l.Y > line.Y);
        return new(position, position)
        {
            Affinity = sharedEnd ? RichAffinity.Upstream : RichAffinity.Downstream
        };
    }
    internal Rect Caret(Context context, int position, RichAffinity affinity = RichAffinity.Downstream)
    {
        int paragraph = Value.ParagraphAt(position), local = position - Value.ParagraphStarts[paragraph];
        var display = Display(context, paragraph);
        if (carets.TryGetValue((paragraph, local, affinity), out var cached))
            return cached;
        var line = display.Lines[LineAt(display, local, affinity)];
        var part = line.Parts.FirstOrDefault(p => local >= p.SourceStart &&
            (local < p.SourceStart + p.SourceLength || affinity == RichAffinity.Upstream && local == p.SourceStart + p.SourceLength)) ?? line.Parts[^1];
        float width = Math.Max(1, Width - Inset(paragraph));
        float origin = RightToLeft(paragraph) ? width - line.Width : 0;
        float x = origin + part.X + (part.Span?.Attachment is not null ? local > part.SourceStart ? part.Run.Width : 0 :
            context.TextSystem.CaretPosition(part.Text, part.Style, Math.Clamp(local - part.SourceStart, 0, part.SourceLength)));
        var result = new Rect(x + Inset(paragraph) + Alignment(context, paragraph), Tops[paragraph] + line.Y, 1, line.Height);
        carets[(paragraph, local, affinity)] = result;
        return result;
    }
    internal RichSelection Hit(Context context, Point point)
    {
        int paragraph = At(point.Y);
        var display = Display(context, paragraph);
        var line = display.Lines.FirstOrDefault(l => point.Y - Tops[paragraph] < l.Y + l.Height) ?? display.Lines[^1];
        int local = context.TextSystem.DisplayHitTest(display, new(point.X - Inset(paragraph) - Alignment(context, paragraph),
            point.Y - Tops[paragraph]), Math.Max(1, Width - Inset(paragraph)), RightToLeft(paragraph));
        return Position(paragraph, line, local, display);
    }
    internal RichSelection LineEdge(Context context, int position, RichAffinity affinity, bool end)
    {
        int paragraph = Value.ParagraphAt(position);
        var display = Display(context, paragraph);
        var line = display.Lines[LineAt(display, position - Value.ParagraphStarts[paragraph], affinity)];
        return Position(paragraph, line, end ? line.SourceEnd : line.SourceStart, display);
    }
    internal RichSelection Collapse(Context context, RichSelection selection, int direction)
    {
        var start = new RichSelection(selection.Start, selection.Start)
        {
            Affinity = selection.Cursor == selection.Start ? selection.Affinity : RichAffinity.Downstream
        };
        var end = new RichSelection(selection.End, selection.End)
        {
            Affinity = selection.Cursor == selection.End ? selection.Affinity : RichAffinity.Upstream
        };
        int paragraph = Value.ParagraphAt(selection.Start);
        if (paragraph == Value.ParagraphAt(selection.End))
        {
            Measure(context, paragraph, paragraph);
            var a = Caret(context, start.Cursor, start.Affinity);
            var b = Caret(context, end.Cursor, end.Affinity);
            if (Math.Abs(a.Y - b.Y) < .01f && Math.Abs(a.X - b.X) > .01f)
                return (a.X < b.X) == (direction < 0) ? start : end;
        }
        // Across visual lines, follow the paragraph's flow while retaining each endpoint's wrap affinity.
        int logical = RightToLeft(paragraph) ? -direction : direction;
        return logical < 0 ? start : end;
    }
    internal RichSelection Horizontal(Context context, int position, RichAffinity affinity, int direction)
    {
        int paragraph = Value.ParagraphAt(position), start = Value.ParagraphStarts[paragraph];
        var display = Display(context, paragraph);
        int index = LineAt(display, position - start, affinity);
        var line = display.Lines[index];
        var here = Caret(context, position, affinity);
        if (!stops.TryGetValue((paragraph, index), out var candidates))
        {
            candidates = System.Globalization.StringInfo.ParseCombiningCharacters(Text(paragraph))
                .Append(Text(paragraph).Length).Where(b => b >= line.SourceStart && b <= line.SourceEnd)
                .Select(b => Position(paragraph, line, b, display)).ToArray();
            stops[(paragraph, index)] = candidates;
        }
        float nearest = float.PositiveInfinity;
        RichSelection? next = null;
        foreach (var candidate in candidates)
        {
            var rect = Caret(context, candidate.Cursor, candidate.Affinity);
            float distance = (rect.X - here.X) * direction;
            if (distance > .01f && distance < nearest)
            {
                nearest = distance;
                next = candidate;
            }
        }
        if (next is { } found)
            return found;
        int logical = RightToLeft(paragraph) ? -direction : direction;
        int other = index + logical;
        if (other >= 0 && other < display.Lines.Length)
        {
            var adjacent = display.Lines[other];
            return Position(paragraph, adjacent, logical < 0 ? adjacent.SourceEnd : adjacent.SourceStart, display);
        }
        int nextParagraph = paragraph + logical;
        if (nextParagraph >= 0 && nextParagraph < Value.Paragraphs.Count)
        {
            Measure(context, nextParagraph, nextParagraph);
            var target = Display(context, nextParagraph);
            var edge = logical < 0 ? target.Lines[^1] : target.Lines[0];
            return Position(nextParagraph, edge, logical < 0 ? edge.SourceEnd : edge.SourceStart, target);
        }
        return new(position, position)
        {
            Affinity = affinity
        };
    }
}
