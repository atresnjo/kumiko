using System.Numerics;
using Kumiko.Rendering;

namespace Kumiko.UI;

internal static partial class Paint
{
    public static void Visit(
        Context c,
        int index,
        Rect clip,
        float opacity,
        float clipRound,
        List<Instance> instances,
        List<Hit> hits,
        Dictionary<ulong, Rect> rects
    )
    {
        ref readonly var n = ref c.Tree[index];
        if (n.LayoutHidden)
            return;
        if (n.Style.Layout.Display == DisplayMode.Contents)
        {
            c.Tree[index].ClipRect = clip;
            for (var child = n.FirstChild; child >= 0; child = c.Tree[child].Next)
                Visit(c, child, clip, opacity * n.Style.Opacity, clipRound, instances, hits, rects);
            return;
        }
        var r = n.Rect;
        var first = instances.Count;
        var outer = clip;
        ref readonly var s = ref n.Style;
        opacity *= s.Opacity;
        rects[n.Id] = r;
        c.Tree[index].ClipRect = clip;
        var visible = r.Intersect(clip);
        var parent = n.Parent >= 0 ? c.Tree[n.Parent].Id : 0;
        if (visible.Width > 0 && visible.Height > 0)
        {
            if (opacity > 0 && (n.Interactive || s.Scroll))
                hits.Add(new(n.Id, parent, visible, n.Interactive, s.Scroll, !s.SkipTab));
            if (s.Shadow is { } shadow)
            {
                var i = Instance.Quad(r with
                {
                    Y = r.Y + shadow.Y
                }, shadow.Color.Alpha(opacity), clip, s.Radius);
                i.Kind = 2;
                i.BorderWidth = shadow.Blur;
                i.ClipRound = clipRound;
                instances.Add(i);
            }

            if (s.Glass is { } glass)
            {
                var pane = Glass.Instance(glass, r, clip, s.Radius, opacity, c.Theme.Dark, c.Scale);
                pane.ClipRound = clipRound;
                instances.Add(pane);
            }
            else if (s.Background is not null || s.Border is not null)
            {
                var i = Instance.Quad(r, (s.Background ?? Color.Transparent).Alpha(opacity), clip, s.Radius);
                i.BorderColor = (s.Border ?? Color.Transparent).Alpha(opacity).Vector;
                i.BorderWidth = s.Border is null ? 0 : s.BorderWidth;
                i.ClipRound = clipRound;
                instances.Add(i);
            }

            if (c.ControlRenderer is null && n.Kind is ContentKind.Text or ContentKind.Icon)
            {
                if (n.Kind == ContentKind.Icon)
                {
                    var sprite = c.TextSystem.Symbol(n.Text ?? "", n.TextStyle.Size);
                    var icon = Instance.Quad(r, (n.TextStyle.Color ?? c.Theme.Text).Alpha(opacity), clip);
                    icon.Kind = 1;
                    icon.UV = new(sprite.X, sprite.Y, sprite.X + sprite.Width, sprite.Y + sprite.Height);
                    icon.ClipRound = clipRound;
                    instances.Add(icon);
                }
                else
                {
                    if (n.TextStyle.Overflow != TextOverflow.Visible)
                        PaintOverflow(c, index, n, clip, opacity, clipRound, instances);
                    else
                    {
                        if (c.SelectedRange(index) is { } selection)
                        {
                            var text = n.Text ?? "";
                            float x = c.TextSystem.Measure(text[..selection.Start], n.TextStyle).Width;
                            float w = c.TextSystem.Measure(text[..selection.End], n.TextStyle).Width - x;
                            var start = n.RightToLeft ? r.Right - c.TextSystem.Measure(text, n.TextStyle).Width : r.X;
                            instances.Add(Instance.Quad(new(start + x, r.Y, w, r.Height), c.Theme.Accent.Alpha(.3f * opacity), clip));
                        }
                        PaintText(c, n, clip, opacity, clipRound, instances);
                    }
                }
            }

            if (n.Kind == ContentKind.Image && n.Image is { } image)
            {
                var sprite = c.TextSystem.Picture(image);
                var i = Instance.Quad(r, Color.White.Alpha(opacity), clip, s.Radius);
                i.Kind = 3;
                i.UV = new(sprite.X, sprite.Y, sprite.X + sprite.Width, sprite.Y + sprite.Height);
                i.ClipRound = clipRound;
                instances.Add(i);
            }
        }

        if (s.Clip || s.Scroll)
        {
            clip = clip.Intersect(r);
            clipRound = s.Radius > 0 ? 15 * 4096 + Math.Min(4095, s.Radius) : 0;
        }

        for (int child = n.FirstChild; child >= 0; child = c.Tree[child].Next)
            Visit(c, child, clip, opacity, clipRound, instances, hits, rects);
        if (s.Scroll && c.Scrolls.TryGetValue(n.Id, out var state) && state.Max > 0)
        {
            state.Track = r;
            state.RightToLeft = n.RightToLeft;
            var active = c.ScrollbarActive(n.Id);
            var thumb = Context.ScrollThumb(r, state, n.RightToLeft, active ? 7 : 3);
            instances.Add(Instance.Quad(thumb, c.Theme.Muted.Alpha((active ? .6f : .4f) * opacity), clip, active ? 3.5f : 2));
        }
        if (s.Scroll && c.CrossScrolls.TryGetValue(n.Id, out var cross) && cross.Max > 0)
        {
            cross.Track = r;
            cross.RightToLeft = n.RightToLeft;
            var active = c.ScrollbarActive(n.Id);
            var thumb = Context.ScrollThumb(r, cross, n.RightToLeft, active ? 7 : 3);
            instances.Add(Instance.Quad(thumb, c.Theme.Muted.Alpha((active ? .6f : .4f) * opacity), clip, active ? 3.5f : 2));
        }
        if (n.EnterFrom is not null || n.ExitTo is not null || n.Rotation != 0 || n.Scale is > 0 and not 1 || n.Draggable is not null)
            c.Painted(index, first, instances.Count, outer);
    }

    static void PaintText(Context c, Node n, Rect clip, float opacity, float clipRound, List<Instance> instances)
    {
        float y = n.Rect.Y;
        var style = n.TextStyle;
        // Right-to-left text aligns each line to the end of its box; glyph order comes from the shaper.
        float Start(float width) => n.RightToLeft ? n.Rect.Right - width : n.Rect.X;
        if (n.Spans is { Length: > 0 } && PaintTinted(c, n, clip, opacity, clipRound, instances))
            return;
        if (!style.Wrap && !(n.Text ?? "").Contains('\n') && n.Spans is null)
        {
            var single = c.TextSystem.Shape(n.Text ?? "", style);
            DrawRun(c, single, style, Start(single.Width), y, clip, opacity, clipRound, instances);
            return;
        }

        int offset = 0;
        foreach (var line in c.TextSystem.Lines(n.Text ?? "", style, style.Wrap ? n.Rect.Width : float.PositiveInfinity))
        {
            if (n.Spans is not { Length: > 0 })
            {
                // A wrapped line keeps its break space; aligned to the end it would leave a gap at the edge.
                var run = n.RightToLeft && line.Text.Length > 0 && char.IsWhiteSpace(line.Text[^1])
                    ? c.TextSystem.Shape(line.Text.TrimEnd(), style)
                    : line.Run;
                DrawRun(c, run, style, Start(run.Width), y, clip, opacity, clipRound, instances);
            }
            else
            {
                float x = Start(line.Run.Width);
                foreach (var segment in TextSystem.Segments(line.Text, style, n.Spans, offset))
                {
                    var run = c.TextSystem.Shape(segment.Text, segment.Style);
                    if (segment.Span?.Background is { } background)
                        instances.Add(Instance.Quad(new(x, y, run.Width, run.Height), background.Alpha(opacity), clip));
                    DrawRun(c, run, segment.Style, x, y, clip, opacity, clipRound, instances);
                    if (segment.Span is { Underline: true } || segment.Span is { Strike: true })
                    {
                        float yy = segment.Span.Value.Strike ? y + run.Ascent * .6f : y + run.Ascent + 1;
                        instances.Add(Instance.Quad(new(x, yy, run.Width, 1), (segment.Style.Color ?? c.Theme.Text).Alpha(opacity), clip));
                    }

                    x += run.Width;
                }
            }

            y += line.Run.Height;
            offset += line.Text.Length;
            if (offset < n.Text?.Length && n.Text[offset] == '\n')
                offset++;
        }
    }

    static void DrawRun(
        Context c,
        ShapedRun run,
        TextStyle style,
        float x,
        float y,
        Rect clip,
        float opacity,
        float clipRound,
        List<Instance> instances,
        TextSpan[]? tints = null
    )
    {
        if (y > clip.Bottom || y + run.Height < clip.Y)
            return;
        var color = (style.Color ?? c.Theme.Text).Alpha(opacity);
        int tint = 0;
        // Glyph bitmaps land on whole device pixels so sampling never blurs their stems. Where the rasterizer
        // supports it, the horizontal remainder is drawn into the bitmap in quarter pixels to keep spacing exact.
        var scale = c.Scale;
        var baseline = MathF.Round((y + run.Ascent) * scale);
        // A long clipped line paints only the glyphs near its clip; two line heights cover any bearing or wide glyph.
        var reach = 2 * run.Height;
        for (int g = 0; g < run.Glyphs.Length; g++)
        {
            var glyph = run.Glyphs[g];
            if (x + glyph.X > clip.Right + reach || x + glyph.X < clip.X - reach)
                continue;
            var origin = (x + glyph.X) * scale;
            var whole = c.TextSystem.Subpixel ? MathF.Floor(origin) : MathF.Round(origin);
            var offset = MathF.Round((origin - whole) * TextSystem.SubpixelSteps) / TextSystem.SubpixelSteps;
            if (offset >= 1)
            {
                whole++;
                offset = 0;
            }
            var sprite = c.TextSystem.Glyph(glyph, style, offset);
            if (sprite.Width == 0 || sprite.Height == 0)
                continue;
            var rect = new Rect(
                (whole + sprite.BearingX) / scale,
                (baseline + MathF.Round(glyph.Y * scale) + sprite.BearingY) / scale,
                sprite.Width / scale,
                sprite.Height / scale
            );
            // Tinted runs map glyph g to character g (see PaintTinted); spans are ordered and disjoint.
            var tinted = color;
            if (tints is not null)
            {
                while (tint < tints.Length && tints[tint].End <= g)
                    tint++;
                if (tint < tints.Length && tints[tint].Start <= g && tints[tint].Color is { } span)
                    tinted = span.Alpha(opacity);
            }
            var i = Instance.Quad(rect, tinted, clip);
            i.Kind = 1;
            i.UV = new(sprite.X, sprite.Y, sprite.X + sprite.Width, sprite.Y + sprite.Height);
            i.ClipRound = clipRound;
            instances.Add(i);
        }
    }
}
