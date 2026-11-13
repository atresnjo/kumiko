using Facebook.Yoga;
using static Facebook.Yoga.YGNodeAPI;
using YogaNode = Facebook.Yoga.Node;

namespace Kumiko.UI;

internal static partial class Layout
{
    static YGSize MeasureText(YogaNode yoga, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var entry = (Workspace.Entry)YGNodeGetContext(yoga)!;
        var c = entry.Context;
        var queries = entry.Queries ??= new();
        var size = queries.Measure(c, c.Tree[entry.Index], width, widthMode);
        queries.Record(width, widthMode, height, heightMode, size);
        return size;
    }

    static YGSize MeasureMinContentText(YogaNode yoga, float width, MeasureMode widthMode, float height, MeasureMode heightMode)
    {
        var entry = (Workspace.Entry)YGNodeGetContext(yoga)!;
        var queries = entry.Queries ??= new();
        var size = queries.MeasureMinContent(entry.Context, entry.Context.Tree[entry.Index]);
        queries.RecordMinContent(size);
        return size;
    }

    static YGSize MeasureMinContentText(Context c, in Node node)
    {
        if (node.Kind == ContentKind.Icon)
            return new()
            {
                Width = node.TextStyle.Size,
                Height = node.TextStyle.Size
            };
        var natural = MeasureText(c, node, float.PositiveInfinity, MeasureMode.Undefined);
        if (node.TextStyle.Overflow != TextOverflow.Visible)
            return new()
            {
                Width = 0,
                Height = natural.Height
            };
        if (!node.TextStyle.Wrap)
            return natural;
        // Match our whitespace wrapping opportunities; emergency grapheme breaks
        // do not reduce a word's intrinsic contribution.
        float minimum = 0;
        var text = node.Text ?? "";
        int start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i != text.Length && !char.IsWhiteSpace(text[i]))
                continue;
            if (i > start)
            {
                var word = node;
                word.Text = text[start..i];
                minimum = Math.Max(minimum, MeasureText(c, word, float.PositiveInfinity, MeasureMode.Undefined).Width);
            }
            start = i + 1;
        }
        return new()
        {
            Width = minimum,
            Height = natural.Height
        };
    }

    static float TextBaseline(YogaNode yoga, float width, float height)
    {
        var entry = (Workspace.Entry)YGNodeGetContext(yoga)!;
        var c = entry.Context;
        var baseline = TextBaseline(c, c.Tree[entry.Index], width);
        (entry.Queries ??= new()).Record(width, height, baseline);
        return baseline;
    }

    static YGSize MeasureText(Context c, in Node node, float width, MeasureMode widthMode)
    {
        var constrained = node.TextStyle.Wrap || node.TextStyle.Overflow != TextOverflow.Visible;
        var available = constrained && widthMode != MeasureMode.Undefined ? Math.Max(0, width) : float.PositiveInfinity;
        var measured = c.ControlRenderer is { } native
            ? native.MeasureText(node.Text ?? "", node.TextStyle, available, node.NativeContent)
            : node.TextStyle.Overflow != TextOverflow.Visible
                ? node.TextStyle.Wrap
                    ? c.TextSystem.Display(node.Text ?? "", node.TextStyle, available, false, node.Spans).Size
                    : c.TextSystem.MeasureDisplay(node.Text ?? "", node.TextStyle, node.Spans)
                : c.TextSystem.Measure(node.Text ?? "", node.TextStyle, available);
        return new()
        {
            Width = node.TextStyle.Overflow == TextOverflow.Visible ? measured.Width :
                widthMode == MeasureMode.Exactly ? available : Math.Min(available, measured.Width),
            Height = measured.Height
        };
    }

    static float TextBaseline(Context c, in Node node, float width) => c.ControlRenderer is { } native
        ? native.TextBaseline(node.Text ?? "", node.TextStyle, node.NativeContent)
        : node.TextStyle.Overflow != TextOverflow.Visible
            ? c.TextSystem.Display(node.Text ?? "", node.TextStyle, node.TextStyle.Wrap ? width : float.PositiveInfinity,
                false, node.Spans).Lines.FirstOrDefault()?.Ascent ?? 0
            : c.TextSystem.Lines(node.Text ?? "", node.TextStyle, width).FirstOrDefault().Run?.Ascent ?? 0;

    static bool SameSpans(TextSpan[]? a, TextSpan[]? b) => a.AsSpan().SequenceEqual(b.AsSpan());

    /// <summary>
    /// A private copy of a node's spans (callers may reuse their arrays). Rows of a scrolling list hand each tree
    /// index different spans every frame, so the copy reuses its buffer instead of allocating a new array.
    /// </summary>
    internal struct SpanSnapshot
    {
        TextSpan[]? buffer;
        int count;

        internal readonly bool Matches(TextSpan[]? spans) => spans.AsSpan().SequenceEqual(buffer.AsSpan(0, count));

        internal void Set(TextSpan[]? spans)
        {
            count = spans?.Length ?? 0;
            if (count == 0)
                return;
            if (buffer is null || buffer.Length < count)
                buffer = new TextSpan[Math.Max(count, 2 * (buffer?.Length ?? 4))];
            spans.AsSpan().CopyTo(buffer);
        }
    }

    // The measure and baseline questions Yoga asked since the node was last dirtied. Yoga's caches
    // hold answers only to these, so a new string that answers each identically leaves them valid.
    internal sealed class TextQueries
    {
        const int Capacity = 8;
        readonly (float Width, MeasureMode WidthMode, float Height, MeasureMode HeightMode, YGSize Size)[] measures =
            new (float, MeasureMode, float, MeasureMode, YGSize)[Capacity];
        readonly (float Width, float Height, float Baseline)[] baselines = new (float, float, float)[Capacity];
        int measureCount, baselineCount;
        bool overflow;
        YGSize? minContent;
        (string? Text, TextStyle Style, YGSize Size) minimum;
        SpanSnapshot minimumSpans;
        // The latest measurement: Yoga usually repeats the question a changed string just failed.
        (string? Text, TextStyle Style, float Width, MeasureMode Mode, YGSize Size) last;
        SpanSnapshot lastSpans;

        internal YGSize Measure(Context c, in Node node, float width, MeasureMode widthMode)
        {
            if (c.ControlRenderer is null && last.Text is not null && ReferenceEquals(last.Text, node.Text) &&
                last.Style == node.TextStyle && lastSpans.Matches(node.Spans) && Same(last.Width, width) && last.Mode == widthMode)
                return last.Size;
            var size = MeasureText(c, node, width, widthMode);
            last = (node.Text, node.TextStyle, width, widthMode, size);
            lastSpans.Set(node.Spans);
            return size;
        }

        internal YGSize MeasureMinContent(Context c, in Node node)
        {
            if (c.ControlRenderer is null && minimum.Text is not null && minimum.Text == node.Text && minimum.Style == node.TextStyle &&
                minimumSpans.Matches(node.Spans))
                return minimum.Size;
            var size = MeasureMinContentText(c, node);
            minimum = (node.Text, node.TextStyle, size);
            minimumSpans.Set(node.Spans);
            return size;
        }

        internal void RecordMinContent(YGSize size) => minContent = size;

        internal void Record(float width, MeasureMode widthMode, float height, MeasureMode heightMode, YGSize size)
        {
            for (var i = 0; i < measureCount; i++)
            {
                ref readonly var m = ref measures[i];
                if (Same(m.Width, width) && m.WidthMode == widthMode && Same(m.Height, height) && m.HeightMode == heightMode)
                {
                    overflow |= !Same(m.Size.Width, size.Width) || !Same(m.Size.Height, size.Height);
                    return;
                }
            }
            if (measureCount == Capacity)
                overflow = true;
            else
                measures[measureCount++] = (width, widthMode, height, heightMode, size);
        }

        internal void Record(float width, float height, float baseline)
        {
            for (var i = 0; i < baselineCount; i++)
            {
                ref readonly var b = ref baselines[i];
                if (Same(b.Width, width) && Same(b.Height, height))
                {
                    overflow |= !Same(b.Baseline, baseline);
                    return;
                }
            }
            if (baselineCount == Capacity)
                overflow = true;
            else
                baselines[baselineCount++] = (width, height, baseline);
        }

        // Forget the latest measurement too when fonts or scale change under the same string.
        internal void Clear(bool metricsChanged = false)
        {
            minContent = null;
            measureCount = 0;
            baselineCount = 0;
            overflow = false;
            if (metricsChanged)
            {
                last = default;
                minimum = default;
            }
        }

        // True when the node's current text gives every recorded answer unchanged.
        internal bool Answers(Context c, in Node node)
        {
            if (overflow)
                return false;
            if (minContent is { } previous)
            {
                var current = MeasureMinContent(c, node);
                if (!Same(previous.Width, current.Width) || !Same(previous.Height, current.Height))
                    return false;
            }
            for (var i = 0; i < measureCount; i++)
            {
                ref readonly var m = ref measures[i];
                var size = Measure(c, node, m.Width, m.WidthMode);
                if (!Same(size.Width, m.Size.Width) || !Same(size.Height, m.Size.Height))
                    return false;
            }
            for (var i = 0; i < baselineCount; i++)
                if (!Same(TextBaseline(c, node, baselines[i].Width), baselines[i].Baseline))
                    return false;
            return true;
        }

        static bool Same(float a, float b) => a == b || float.IsNaN(a) && float.IsNaN(b);
    }
}
