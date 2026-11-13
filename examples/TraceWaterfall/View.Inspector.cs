using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.UI;

namespace TraceWaterfall;

public sealed partial class TraceScreen
{
    void Inspector(TraceSpan span, Rect? anchor, ulong origin)
    {
        var ui = Ui;
        var model = Model;
        var c = ui.Context;
        void Close()
        {
            ui.Dispatch(new TraceMsg.Close());
            c.FocusControl(origin);
        }
        using var popup = c.Kit.Popover(anchor, Close,
            new(Side.Top, PopoverAlign.End, Width: 408, Padding: 18, Name: span.Name + " details"), "inspector");
        using var content = c.Div(key: "content").FillWidth().Gap(16);
        using (c.Div(key: "header").Row().FillWidth().ItemsCenter().Gap(10))
        {
            c.Icon(span.Icon, 22, p.Tint(span.Kind), key: "icon");
            using (c.Div(key: "title").Flex().Gap(4))
            {
                Text(span.Name, 16, p.Text, Weight.Semibold, "name");
                Text(span.Kind + " · " + span.Detail, 13, p.Muted, key: "detail");
            }
            using var close = c.Div(key: "close").Width(28).Height(28).Center().Rounded(4);
            var hit = close.Semantics(Role.Button, "Close details").Control();
            if (hit.Clicked)
                Close();
            Feedback(close, hit);
            c.Icon(Hugeicons.Cancel01, 16, p.Muted, key: "icon");
        }
        using (c.Div(key: "timing").Row().FillWidth().Padding(12).Rounded(8).Background(p.Page).Gap(24))
        {
            Detail("DURATION", TraceQueries.Time(TraceQueries.Duration(model, span)), "duration");
            Detail("STARTED", "+" + TraceQueries.Time(span.Start), "start");
            Detail("STATUS", TraceQueries.Failed(model, span) ? "Failed" : model.Now < span.End ? "Running" : "Done", "status");
        }
        if (TraceQueries.Failed(model, span))
        {
            using var error = c.Div(key: "error").Row().FillWidth().Padding(12).Gap(10).Rounded(8).Background(p.DangerFill);
            c.Icon(Hugeicons.Alert02, 17, p.Danger, key: "icon");
            using var copy = c.Div(key: "copy").Flex();
            c.Text(span.Error!, new(Size: 14, Color: p.Danger, Wrap: true), key: "text");
        }
        var totals = TraceQueries.Totals(model, span);
        if (totals.Cost > 0 || totals.TokensIn + totals.TokensOut > 0)
        {
            using var usage = c.Div(key: "usage").Row().FillWidth().Gap(24);
            Detail("COST", TraceQueries.Cost(totals.Cost), "cost");
            Detail("TOKENS IN", TraceQueries.Tokens(totals.TokensIn), "input");
            Detail("TOKENS OUT", TraceQueries.Tokens(totals.TokensOut), "output");
        }
        Preview("INPUT", span.Input, "input");
        if (span.End <= model.Now && !string.IsNullOrEmpty(span.Output))
            Preview("OUTPUT", span.Output, "output");
    }

    void Detail(string label, string value, string key)
    {
        var c = C;
        using var detail = c.Div(key: key).Flex().Gap(6);
        Text(label, 10, p.Muted, key: "label");
        Text(value, 14, p.Text, Weight.Medium, "value");
    }

    void Preview(string label, string value, string key)
    {
        var c = C;
        if (string.IsNullOrEmpty(value))
            return;
        using var preview = c.Div(key: key).FillWidth().Gap(7);
        Text(label, Palette.Caption, p.Muted, key: "label");
        c.Text(value, new(Size: 13, Color: p.Muted, Wrap: true), key: "value");
    }
}
