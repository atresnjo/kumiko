using System.Collections.Immutable;
using Kumiko.Icons;
using Kumiko.UI;

namespace TraceWaterfall;

public sealed partial class TraceScreen
{
    void Waterfall()
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        var labelWidth = Math.Clamp((c.Size.Width - Palette.Inset * 2) * .34f, 280, 430);
        var plotWidth = c.Size.Width - Palette.Inset * 2 - labelWidth;
        var rows = TraceQueries.Rows(model);
        using var waterfall = c.Div(key: "waterfall").FillWidth().Flex().Flow(FlowDirection.LeftToRight);
        var range = waterfall.Animate(TraceQueries.Range(model.Now), Motion.Quick);
        using (c.Div(key: "axis").Row().FillWidth().Height(40).Border(p.Grid))
        {
            using (c.Div(key: "heading").Width(labelWidth).FillHeight().Px(24).Justify(Justify.Center))
                Text("SPAN", Palette.Caption, p.Muted, key: "text");
            using var axis = c.Div(key: "ticks").Flex().FillHeight();
            Grid(plotWidth, 40, model.Now, range, ticks: true);
        }
        Rect? anchor = null;
        ulong origin = 0;
        using (c.Div(key: "rows").FillWidth().Flex().Scroll().Semantics(Role.Tree, "Agent trace"))
        {
            foreach (var span in rows)
            {
                using var row = c.Div(key: span.Id).Row().FillWidth().Height(Palette.Row)
                    .Semantics(Role.TreeItem, span.Name, Describe(span))
                    .Accessibility(new(Expanded: model.Spans.Any(s => s.Parent == span.Id) ? !model.Collapsed.Contains(span.Id) : null)
                    {
                        Level = TraceQueries.Depth(model, span) + 1
                    });
                if (model.RestoreFocus && model.FocusId == span.Id)
                {
                    c.FocusNextControl();
                    ui.Dispatch(new TraceMsg.FocusRestored());
                }
                var hit = row.Control();
                ui.OnPress(hit, new TraceMsg.Open(span.Id));
                if (model.OpenId == span.Id)
                {
                    row.Background(p.Hover);
                    anchor = hit.Rect;
                    origin = row.Id;
                }
                Feedback(row, hit);
                if (hit.Focused)
                    Navigate(span, rows);
                SpanLabel(span, labelWidth);
                SpanBar(span, plotWidth, range);
            }
            // The grid remains continuous below a short, live trace.
            using (c.Div(key: "tail").Row().FillWidth().Grow().MinHeight(16))
            {
                using (c.Div(key: "labels").Width(labelWidth).FillHeight())
                {
                }
                using var tail = c.Div(key: "grid").Flex().FillHeight();
                Grid(plotWidth, 16, model.Now, range);
            }
        }
        if (model.OpenId is { } id && model.Spans.FirstOrDefault(s => s.Id == id) is { } selected)
            Inspector(selected, anchor, origin);
    }

    void SpanLabel(TraceSpan span, float width)
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        var depth = TraceQueries.Depth(model, span);
        var children = model.Spans.Count(s => s.Parent == span.Id && s.Start <= model.Now);
        using var label = c.Div(key: "label").Row().Width(width).FillHeight().ItemsCenter().Gap(9).Px(12).Clip();
        for (var level = 0; level < depth; level++)
        {
            using var indent = c.Div(key: level).Width(13).FillHeight();
            using var line = c.Div(key: "line").Absolute().Offset(6, 0).Width(1).FillHeight().Background(p.Grid);
        }
        using (var chevron = c.Div(key: "chevron").Width(24).Height(28).Center().Rounded(4))
        {
            if (children > 0)
            {
                var collapsed = model.Collapsed.Contains(span.Id);
                var hit = ui.Button(chevron, (collapsed ? "Expand " : "Collapse ") + span.Name, new TraceMsg.ToggleBranch(span.Id), true);
                Feedback(chevron, hit);
                c.Icon(collapsed ? Hugeicons.ArrowRight01 : Hugeicons.ArrowDown01, 13, p.Muted, key: "icon");
            }
        }
        using (c.Div(key: "mark").Width(25).Height(25).Center().Rounded(6).Background(p.Surface).Border(p.Border))
            c.Icon(span.Icon, 16, span.Kind == SpanKind.Tool ? p.Tint(span.Kind) : span.Detail.StartsWith("GPT", StringComparison.Ordinal) ? p.Text : p.Slow, key: "icon");
        Text(span.Name, Palette.Body, p.Text, span.Kind == SpanKind.Agent ? Weight.Semibold : Weight.Regular, "name");
        Text(span.Detail, Palette.Body, p.Muted, key: "detail");
        if (children > 0 && model.Collapsed.Contains(span.Id))
            Text("+" + model.Spans.Count(s => s.Start <= model.Now && TraceQueries.DescendsFrom(model, s, span.Id)), Palette.Caption, p.Muted, key: "count");
    }

    void SpanBar(TraceSpan span, float width, float range)
    {
        var c = C;
        var model = Model;
        using var plot = c.Div(key: "plot").Flex().FillHeight().Clip();
        Grid(width, Palette.Row, model.Now, range);
        var start = span.Start / range * width;
        var duration = TraceQueries.Duration(model, span);
        var length = MathF.Max(2, duration / range * width);
        var failed = TraceQueries.Failed(model, span);
        using (c.Div(key: "bar").Absolute().Offset(start, 9, animate: Motion.Quick).Width(length, animate: Motion.Quick).Height(18)
            .Rounded(4).Background(failed ? p.DangerFill : p.Fill(span.Kind)).Border(failed ? p.Danger : p.Tint(span.Kind).Alpha(.32f)))
        {
            if (model.Collapsed.Contains(span.Id))
            {
                foreach (var child in model.Spans.Where(s => s.Start <= model.Now && s.Kind != SpanKind.Agent && TraceQueries.DescendsFrom(model, s, span.Id)))
                {
                    using var mark = c.Div(key: child.Id).Absolute().Offset((child.Start - span.Start) / range * width, 5)
                        .Width(MathF.Max(2, TraceQueries.Duration(model, child) / range * width)).Height(7).Rounded(2)
                        .Background(TraceQueries.Failed(model, child) ? p.Danger : p.Tint(child.Kind));
                }
            }
        }
        var total = TraceQueries.Totals(model, span);
        var caption = TraceQueries.Time(duration) + (total.Cost > 0 ? " · " + TraceQueries.Cost(total.Cost) : "");
        using var info = c.Div(key: "info").Absolute().Offset(start + length + 9, 0, animate: Motion.Quick)
            .Row().Height(Palette.Row).ItemsCenter().Gap(8);
        Text(caption, Palette.Body, failed ? p.Danger : p.Muted, key: "duration");
        if (TraceQueries.Slowest(model) == span.Id)
        {
            using var badge = c.Div(key: "slowest").Px(5).Py(2).Rounded(4).Background(p.SlowFill);
            Text("SLOWEST", 10, p.Slow, Weight.Medium, "label");
        }
        if (span.End > model.Now)
            c.Icon(Hugeicons.Clock01, 12, p.Blue, key: "running");
        if (failed)
            c.Icon(Hugeicons.Alert02, 14, p.Danger, key: "error");
    }

    void Grid(float width, float height, int now, float range, bool ticks = false)
    {
        var c = C;
        for (var tick = 0; tick < 5; tick++)
        {
            using (c.Div(key: $"line-{tick}").Absolute().Offset(width * tick / 5, 0).Width(1).FillHeight().Background(tick == 0 ? p.Border : p.Grid))
            {
            }
            if (ticks)
            {
                using var label = c.Div(key: $"tick-{tick}").Absolute().Offset(width * tick / 5 + 9, 11).Width(80).Height(18);
                Text(tick == 0 ? "0s" : TraceQueries.Time((int)(range * tick / 5)), Palette.Caption, p.Muted, key: "text");
            }
        }
        using var cursor = c.Div(key: "now").Absolute().Offset(now / range * width, 0, animate: Motion.Quick)
            .Width(1).FillHeight().Background(p.Blue.Alpha(.6f));
    }

    string Describe(TraceSpan span) =>
        $"{span.Kind}, {(TraceQueries.Failed(Model, span) ? "Failed" : span.End > Model.Now ? "Running" : "Done")}, {TraceQueries.Time(TraceQueries.Duration(Model, span))}";

    void Navigate(TraceSpan span, ImmutableArray<TraceSpan> rows)
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        if (model.OpenId is not null)
            return;
        var index = rows.IndexOf(span);
        string? target = null;
        if (c.Shortcut(Key.Down, Modifiers.None))
            target = rows[Math.Min(index + 1, rows.Length - 1)].Id;
        else if (c.Shortcut(Key.Up, Modifiers.None))
            target = rows[Math.Max(0, index - 1)].Id;
        else if (c.Shortcut(Key.Home, Modifiers.None))
            target = rows[0].Id;
        else if (c.Shortcut(Key.End, Modifiers.None))
            target = rows[^1].Id;
        else if (c.Shortcut(Key.Left, Modifiers.None))
        {
            if (!model.Collapsed.Contains(span.Id) && rows.Any(s => s.Parent == span.Id))
                ui.Dispatch(new TraceMsg.ToggleBranch(span.Id));
            else if (span.Parent is { } parent)
                target = parent;
        }
        else if (c.Shortcut(Key.Right, Modifiers.None))
        {
            if (model.Collapsed.Contains(span.Id))
                ui.Dispatch(new TraceMsg.ToggleBranch(span.Id));
            else
                target = rows.FirstOrDefault(s => s.Parent == span.Id)?.Id;
        }
        if (target is { } id && c.Accessibility.FirstOrDefault(n => n.Role == Role.TreeItem && n.Key == id) is { } node)
            c.FocusControl(node.Id);
    }
}
