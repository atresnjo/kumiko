using Kumiko;
using Kumiko.UI;

internal static class FlexboxChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var selected = -1;
        using var cards = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(10);
            using var grid = c.Div(key: "cards").Row().Wrap().FillWidth().Gap(99).RowGap(10).ColumnGap(10);
            for (var i = 0; i < 4; i++)
            {
                using var card = c.Div(key: i).Width(100).AspectRatio(2).Margin(5)
                    .Background(Color.Hex(0x2368aa)).Semantics(Role.Button, $"Card {i}");
                if (card.Control().Clicked)
                    selected = i;
            }
            using var badge = c.Div(key: "badge").Absolute().Width(1).Height(1);
        }, new(Width: 260, Height: 300, DevMode: true));
        var first = cards.Get(Role.Button, "Card 0");
        var identity = first.Id;
        check(Box(cards, "cards").Height == 130, "wrapped cards determine the container's intrinsic height");
        check(Box(cards, "0") == new Rect(15, 15, 100, 50) && Box(cards, "2").Y == 85,
            "card wrapping includes padding, margins and gaps and excludes absolute overlays");
        cards.Get(Role.Button, "Card 3").Click();
        check(selected == 3, "wrapped card input reaches its semantic target");
        Save(cards, output, "phone");
        cards.Resize(520, 300);
        check(Box(cards, "3").Y == 15 && Box(cards, "cards").Height == 60,
            "resizing unwraps cards and recomputes intrinsic height");
        check(first.Id == identity, "reflow preserves keyed control identity");
        Save(cards, output, "wide");
        cards.Resize(130, 400);
        check(Box(cards, "3").Y == 225, "narrow cards reflow to four independent lines");
        Save(cards, output, "narrow");

        using var sizing = new Tester(c =>
        {
            using var page = c.Div().SizeFull().Gap(10);
            using (c.Div(key: "grow").Row().Width(300).Height(40))
            {
                using (c.Div(key: "capped").Basis(50).Grow().MaxWidth(70).Height(40))
                {
                }
                using (c.Div(key: "remaining").Basis(50).Grow().Height(40))
                {
                }
            }
            using (c.Div(key: "shrink").Row().Width(300).Height(40))
            {
                using (c.Div(key: "minimum").Width(250).Basis(200).Shrink().MinWidth(180).Height(40))
                {
                }
                using (c.Div(key: "shrunk").Width(200).Shrink().Height(40))
                {
                }
            }
            using (c.Div().Row().Width(300).Height(40))
            {
                using (c.Div(key: "weighted-a").Basis(100).Shrink().Height(40))
                {
                }
                using (c.Div(key: "weighted-b").Basis(300).Shrink().Height(40))
                {
                }
            }
            using (c.Div().Row().Width(320).Padding(10))
            {
                using (c.Div(key: "ratio").Basis(Length.Percent(50)).Grow().AspectRatio(2))
                {
                }
            }
            using (c.Div().Row().Width(300).Height(60).Items(Align.Stretch))
            {
                using (c.Div(key: "self").Width(40).Height(20).Margin(5).AlignSelf(Align.End))
                {
                }
                using (c.Div(key: "stretch").Width(40).Margin(5))
                {
                }
                using (c.Div(key: "fixed").Width(40).Height(20))
                {
                }
            }
        }, new(Width: 400, Height: 400));
        check(Box(sizing, "capped").Width == 70 && Box(sizing, "remaining").Width == 230,
            "growth redistributes free space after a child reaches its maximum");
        check(Box(sizing, "minimum").Width == 180 && Box(sizing, "shrunk").Width == 120,
            "shrink redistributes deficit after a child reaches its minimum and basis overrides width");
        check(Box(sizing, "weighted-a").Width == 75 && Box(sizing, "weighted-b").Width == 225,
            "shrink is weighted by the flex basis");
        check(Box(sizing, "ratio").Width == 300 && Box(sizing, "ratio").Height == 150,
            "aspect ratio follows the final grown main size");
        check(Box(sizing, "stretch").Height == 50 && Box(sizing, "fixed").Height == 20 &&
            Box(sizing, "self").Bottom == Box(sizing, "stretch").Bottom,
            "self alignment and margins coexist with stretch and fixed cross dimensions");
        Save(sizing, output, "sizing");
        Alignment(output, check);
        FlexboxEdges.Run(output, check);
    }

    static void Alignment(string output, Action<bool, string> check)
    {
        using var t = new Tester(c =>
        {
            using var page = c.Div().SizeFull().Gap(10);
            using (c.Div(key: "lines").Row().Wrap(FlexWrap.WrapReverse).Width(150).Height(120)
                .Gap(10).AlignContent(ContentAlign.SpaceBetween))
            {
                using (c.Div(key: "line-a").Width(100).Height(20))
                {
                }
                using (c.Div(key: "line-b").Width(100).Height(20))
                {
                }
            }
            using (c.Div(key: "column").Column().Wrap().Width(150).Height(100).Gap(10))
            {
                using (c.Div(key: "column-a").Width(30).Height(60))
                {
                }
                using (c.Div(key: "column-b").Width(30).Height(60))
                {
                }
            }
            using (c.Div(key: "reverse").RowReverse().Width(200).Height(40).Justify(Justify.SpaceEvenly))
            {
                using (c.Div(key: "reverse-a").Width(40).Height(20))
                {
                }
                using (c.Div(key: "reverse-b").Width(40).Height(20))
                {
                }
            }
            using (c.Div(key: "baseline").Row().FillWidth().Items(Align.Baseline).Gap(10))
            {
                c.Text("Large", new(Size: 28), key: "large");
                using (c.Div(key: "nested").Padding(5))
                    c.Text("Small", new(Size: 12), key: "small");
            }
        }, new(Width: 400, Height: 450));
        check(Box(t, "line-a").Y == 100 && Box(t, "line-b").Y == 0,
            "reverse wrapping and line distribution use the cross axis");
        check(Box(t, "column-b").X > Box(t, "column-a").Right &&
            Box(t, "column-a").Y == Box(t, "column-b").Y, "columns wrap into distinct columns");
        check(Box(t, "reverse-a").X == 120 && Box(t, "reverse-b").X == 40,
            "reversed rows distribute space evenly without changing identity");
        var large = t.Context.TextSystem.Shape("Large", new(Size: 28)).Ascent;
        var small = t.Context.TextSystem.Shape("Small", new(Size: 12)).Ascent;
        check(Math.Abs(Box(t, "large").Y + large - Box(t, "small").Y - small) < .01,
            "different fonts align their first text baselines through padded containers");
        Save(t, output, "alignment");
    }

    static Rect Box(Tester t, string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
    static void Save(Tester t, string output, string name)
    {
        var path = Path.Combine(output, "flexbox-" + name);
        t.Screenshot(path + ".png");
        File.WriteAllText(path + ".txt", t.Dump());
        t.WriteWarnings(path + ".warnings.json");
    }
}
