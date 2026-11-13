using System.Globalization;
using Kumiko.UI;

internal static class EdgeMatrixView
{
    internal static readonly LayoutEdge[] Sides = [LayoutEdge.Left, LayoutEdge.Right, LayoutEdge.Start, LayoutEdge.End];
    internal static readonly string[] Properties = ["Padding", "Margin", "Border", "Inset"];

    internal static void Render(Context c, bool rtl)
    {
        c.Culture = CultureInfo.GetCultureInfo(rtl ? "he-IL" : "en-US");
        using var root = c.Div(key: "matrix").SizeFull().Padding(12).Gap(12).Background(Color.White);
        c.Text($"Physical edges / logical edges — {(rtl ? "RTL" : "LTR")} window", new(Size: 22));
        using var panels = c.Div(key: "panels").Row().FillWidth().Flex().Gap(16);
        for (var panel = 0; panel < 2; panel++)
        {
            using var island = c.Div(key: $"panel-{panel}").Flex().FillHeight().Gap(6);
            if (panel == 1)
                island.Flow(rtl ? FlowDirection.LeftToRight : FlowDirection.RightToLeft);
            c.Text(panel == 0 ? "Inherited direction" : "Opposite-direction island", new(Size: 16), key: "heading");
            foreach (var property in Properties)
                foreach (var typed in new[] { false, true })
                {
                    c.Text($"{property} / {(typed ? "LengthEdges" : "LayoutEdge")}", new(Size: 12), key: $"{property}-{typed}-label");
                    using var row = c.Div(key: $"{property}-{typed}").Row().FillWidth().Gap(8);
                    foreach (var side in Sides)
                        Cell(c, $"{panel}-{property}-{typed}-{side}", property, side, typed);
                }
            c.Text("Edges shorthand and symmetric helpers", new(Size: 12));
            using (c.Div(key: "legacy").Row().FillWidth().Gap(8))
                foreach (var property in new[] { "Padding", "Margin", "Border", "Symmetric" })
                    Cell(c, $"{panel}-legacy-{property}", property, LayoutEdge.All, false);
            c.Text("Percentage insets / auto margins", new(Size: 12));
            using (c.Div(key: "advanced").Row().FillWidth().Gap(8))
                foreach (var side in Sides)
                    Cell(c, $"{panel}-percent-{side}", "Percent", side, true);
            using (c.Div(key: "automatic").Row().FillWidth().Gap(8))
                foreach (var side in Sides)
                    Cell(c, $"{panel}-auto-{side}", "Auto", side, true);
            c.Text("Edges on an opposite-direction node", new(Size: 12));
            var boundary = (panel == 0 ? rtl : !rtl) ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
            foreach (var property in Properties)
            {
                c.Text(property, new(Size: 10), key: "boundary-label-" + property);
                using var row = c.Div(key: "boundary-" + property).Row().FillWidth().Gap(8);
                foreach (var side in Sides)
                    Cell(c, $"{panel}-boundary-{property}-{side}", property, side, true, boundary);
            }
            c.Text("Relative position", new(Size: 12));
            using (c.Div(key: "relative").Row().FillWidth().Gap(8))
                foreach (var side in Sides)
                    Cell(c, $"{panel}-relative-{side}", "Relative", side, true);
            c.Text("Fixed LTR node while parent direction changes", new(Size: 12));
            foreach (var property in new[] { "Margin", "Inset" })
            {
                using var row = c.Div(key: "fixed-" + property).Row().FillWidth().Gap(8);
                foreach (var side in Sides)
                    Cell(c, $"{panel}-fixed-{property}-{side}", property, side, true, FlowDirection.LeftToRight);
            }
            c.Text("Typed physical override after Edges shorthand", new(Size: 12));
            using (c.Div(key: "fallback").Row().FillWidth().Gap(8))
                foreach (var property in new[] { "Padding", "Margin", "Border" })
                    Cell(c, $"{panel}-fallback-{property}", property, LayoutEdge.Left, true, fallback: true);
        }
    }

    static void Cell(Context c, string key, string property, LayoutEdge side, bool typed,
        FlowDirection flow = FlowDirection.Inherit, bool fallback = false)
    {
        using var cell = c.Div(key: key).Width(112).Gap(3);
        c.Text(side == LayoutEdge.All ? property : side.ToString(), new(Size: 10));
        using var stage = c.Div(key: key + "-stage").Width(104).Height(36).Background(Color.Hex(0xe1e9f3));
        var inset = property is "Inset" or "Percent" or "Relative";
        var edges = Edges(side, property == "Percent" ? Length.Percent(25) : 20);
        if (property is "Padding" or "Border")
            stage.Flow(flow);
        if (property == "Padding")
        {
            if (side == LayoutEdge.All)
                stage.Padding(new Edges(0, 8, 0, 20));
            else if (typed)
                stage.Padding(edges);
            else
                stage.Padding(side, 20);
        }
        if (property == "Border")
        {
            if (side == LayoutEdge.All)
                stage.LayoutBorder(new Edges(0, 8, 0, 20));
            else if (typed)
                stage.LayoutBorder(edges);
            else
                stage.LayoutBorder(side, 20);
        }
        if (property == "Symmetric")
            stage.Px(8);
        if (fallback && property == "Padding")
            stage.Padding(new Edges(0, 8, 0, 20)).Padding(LayoutEdge.Left, 30);
        if (fallback && property == "Border")
            stage.LayoutBorder(new Edges(0, 8, 0, 20)).LayoutBorder(LayoutEdge.Left, 30);
        using var target = c.Div(key: key + "-target").Height(20).Background(Color.Hex(0x2368aa));
        if (property is not ("Padding" or "Border"))
            target.Flow(flow);
        if (inset)
        {
            if (property == "Relative")
            {
                stage.ItemsCenter();
                target.Position(PositionMode.Relative);
            }
            else
                target.Absolute();
            target.Width(20).Inset(typed ? edges : default);
            if (!typed)
                target.Inset(side, 20);
        }
        else if (property == "Auto")
            target.Width(20).Margin(Edges(side, Length.Auto));
        else
        {
            target.FillWidth();
            if (property == "Margin")
            {
                if (side == LayoutEdge.All)
                    target.Margin(new Edges(0, 8, 0, 20));
                else if (typed)
                    target.Margin(Edges(side, 20));
                else
                    target.Margin(side, 20);
            }
            if (property == "Symmetric")
                target.Mx(4);
            if (fallback && property == "Margin")
                target.Margin(new Edges(0, 8, 0, 20)).Margin(LayoutEdge.Left, 30);
        }
    }

    internal static LengthEdges Edges(LayoutEdge side, Length value) => side switch
    {
        LayoutEdge.Left => new(Left: value),
        LayoutEdge.Right => new(Right: value),
        LayoutEdge.Start => new(Start: value),
        _ => new(End: value)
    };
}
