using Kumiko;
using Kumiko.UI;

internal static class FlexboxEdges
{
    internal static void Run(string output, Action<bool, string> check)
    {
        using var t = new Tester(c =>
        {
            using var page = c.Div().SizeFull().Gap(10);
            using (c.Div(key: "rtl").Row().Wrap().Flow(FlowDirection.RightToLeft).Width(220).Gap(10))
            {
                using (c.Div(key: "rtl-a").Width(80).Height(20).Margin(new Edges(0, 15, 0, 5)))
                {
                }
                using (c.Div(key: "rtl-b").Width(80).Height(20).Margin(new Edges(0, 15, 0, 5)))
                {
                }
            }
            using (c.Div(key: "percent-parent").Width(220).Height(100).Padding(10))
            {
                using (c.Div(key: "percent-margin").Width(Length.Percent(50)).Height(20).Margin(5))
                {
                }
                using (c.Div(key: "absolute-margin").Absolute().Width(Length.Percent(50)).Height(20).Margin(5))
                {
                }
            }
            using (c.Div(key: "ratio-parent").Width(220).Padding(10))
            {
                using (c.Div(key: "fill-ratio").FillWidth().AspectRatio(2))
                {
                }
            }
            using (c.Div().Column().Height(100).Width(220))
            {
                using (c.Div(key: "column-fixed").Height(80).Shrink(0).Width(20))
                {
                }
                using (c.Div(key: "column-shrink").Height(80).Shrink().Width(20))
                {
                }
            }
            using (c.Div().Row().Width(220).Gap(10).Justify(Justify.SpaceAround))
            {
                using (c.Div(key: "around-a").Width(50).Height(20))
                {
                }
                using (c.Div(key: "around-b").Width(50).Height(20))
                {
                }
            }
        }, new(Width: 400, Height: 500));
        check(Box(t, "rtl-a").X == 135 && Box(t, "rtl-b").X == 25,
            "RTL mirrors asymmetric outer spacing without reordering domain keys");
        check(Box(t, "percent-margin").Width == 100 && Box(t, "absolute-margin").Width == 100,
            "percentage dimensions use parent content size before subtracting the child's margins");
        check(Box(t, "fill-ratio").Width == 200 && Box(t, "fill-ratio").Height == 100 &&
            Box(t, "ratio-parent").Height == 120, "filled ratio cards contribute their height to automatic columns");
        check(Box(t, "column-fixed").Height == 80 && Box(t, "column-shrink").Height == 20,
            "column shrinking respects an explicit zero weight");
        check(Math.Abs(Box(t, "around-a").X - 27.5f) < .01 && Math.Abs(Box(t, "around-b").X - 142.5f) < .01,
            "space-around supplements the authored gap with half edge spacing");
        Save(t, output, "edges");

        using var scroll = new Tester(c =>
        {
            using var grid = c.Div(key: "scroll-grid").Row().Wrap().SizeFull().Gap(10).Scroll();
            for (var i = 0; i < 6; i++)
            {
                using var item = c.Div(key: i).Width(90).Height(60).Margin(5)
                    .Background(Color.Hex(0x2368aa)).Semantics(Role.Button, $"Scroll card {i}");
                item.Control();
            }
        }, new(Width: 220, Height: 100));
        scroll.MoveTo(100, 50);
        scroll.Scroll(1000);
        scroll.Settle();
        check(scroll.Dump().Contains("scroll=y:130/130", StringComparison.Ordinal),
            "wrapped vertical scroll range includes every line and its trailing margins");
        check(scroll.Get(Role.Button, "Scroll card 5").Rect.Bottom <= 100,
            "last wrapped card can be scrolled fully into view");
        Save(scroll, output, "scroll");

        using var text = new Tester(c =>
        {
            using var root = c.Div().SizeFull();
            using var row = c.Div(key: "text-row").Row().Wrap().FillWidth().Gap(10);
            for (var i = 0; i < 3; i++)
            {
                using var card = c.Div(key: i).Basis(140).Grow().Padding(5);
                c.Text("Longer card content must wrap without overlapping the following row.", new(Wrap: true), key: $"text-{i}");
            }
        }, new(Width: 300, Height: 500));
        check(Box(text, "2").Y >= Box(text, "0").Bottom + 10 &&
            Box(text, "text-row").Bottom >= Box(text, "2").Bottom,
            "wrapped lines measure reflowed text before positioning subsequent lines");
        Save(text, output, "text");
        using var intrinsic = new Tester(c =>
        {
            using var page = c.Div().SizeFull();
            using (c.Div(key: "intrinsic-row").Row())
            {
                using var tile = c.Div(key: "intrinsic-tile").Basis(100).Grow().AspectRatio(2)
                    .Background(Color.Hex(0x2368aa));
            }
            using (c.Div(key: "intrinsic-column").MinHeight(100))
            {
                using var tile = c.Div(key: "column-ratio").Basis(50).Grow().AspectRatio(2)
                    .Background(Color.Hex(0x2368aa));
            }
        }, new(Width: 400, Height: 300));
        check(Box(intrinsic, "intrinsic-row").Width == 100 && Box(intrinsic, "intrinsic-row").Height == 50,
            "intrinsic row height uses its own final width when flexible children have an aspect ratio");
        check(Box(intrinsic, "intrinsic-column").Width == 200 && Box(intrinsic, "column-ratio").Width == 200,
            "minimum-height columns measure ratio widths after flex growth");
        Save(intrinsic, output, "intrinsic");
        using var shrinkText = new Tester(c =>
        {
            using var row = c.Div().Row().SizeFull();
            using (c.Div().Width(40).Height(30))
            {
            }
            using var content = c.Div(key: "shrink-content").Width(200).Shrink().Padding(10);
            c.Text("Content in a shrunken column must reflow to its allocated width and keep all of its lines.",
                new(Wrap: true), key: "shrunk-text");
            c.Text("Following", key: "following");
        }, new(Width: 200, Height: 300));
        check(Box(shrinkText, "shrunk-text").Right <= Box(shrinkText, "shrink-content").Right - 10 &&
            Box(shrinkText, "following").Y >= Box(shrinkText, "shrunk-text").Bottom,
            "shrinking a fixed-width container remeasures nested wrapping text and following flow content");
        Save(shrinkText, output, "shrunk-text");
        using var tall = new Tester(c =>
        {
            using var row = c.Div().Row().SizeFull();
            using var image = c.Div(key: "height-ratio").FillHeight().AspectRatio(2)
                .Background(Color.Hex(0x2368aa));
        }, new(Width: 160, Height: 50));
        check(Box(tall, "height-ratio").Width == 100 && Box(tall, "height-ratio").Height == 50,
            "aspect ratio derives an automatic width from a filled height");
        Save(tall, output, "height-ratio");
        Invalid(check);
    }

    static void Invalid(Action<bool, string> check)
    {
        foreach (var scenario in new[] { 0, 1, 2, 3, 4 })
        {
            var rejected = false;
            try
            {
                using var t = new Tester(c =>
                {
                    using var item = c.Div();
                    switch (scenario)
                    {
                        case 0:
                            item.Shrink(-1);
                            break;
                        case 1:
                            item.AspectRatio(0);
                            break;
                        case 2:
                            item.Margin(float.NaN);
                            break;
                        case 3:
                            item.Basis(new(LengthKind.Pixels, float.PositiveInfinity));
                            break;
                        case 4:
                            item.Grow(float.NaN);
                            break;
                    }
                });
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }
            check(rejected, $"invalid flex sizing scenario {scenario} is rejected before layout");
        }
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
