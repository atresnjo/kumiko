using Kumiko;
using Kumiko.UI;

internal static partial class YogaUpstreamChecks
{
    static void Views(string output, Action<bool, string> check)
    {
        var changed = false;
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(10).Gap(10);
            foreach (var kind in new[] { "auto", "zero", "clip", "scroll" })
            {
                using var row = c.Div(key: kind + "-row").Row().Width(160).Height(40);
                using (var item = c.Div(key: kind).Shrink().Background(Color.Hex(0xdce8fa)))
                {
                    if (kind == "zero")
                        item.MinWidth(0);
                    if (kind == "clip")
                        item.Clip();
                    if (kind == "scroll")
                        item.Overflow(OverflowMode.Scroll);
                    c.Text(changed ? "LongerUnbreakable" : "Unbreakable", new(Size: 16, Wrap: true), key: "word");
                }
                using var spacer = c.Div(key: "spacer").Width(130).Height(20).Background(Color.Hex(0xf0dfcf));
            }
            using (c.Div(key: "fractional").Row().Width(350).Height(30))
            {
                var widths = new[] { 100.1f, 200.3f, 300.7f };
                var minima = new[] { 90f, 190f, 290f };
                for (var i = 0; i < 3; i++)
                {
                    using var item = c.Div(key: i).Width(widths[i]).MinWidth(minima[i]).Shrink().Height(20);
                }
            }
            using var box = c.Div(key: "boundary").Absolute().Width(100).Height(80)
                .Padding(Length.Percent(5)).Offset(230, 10);
            c.Text(changed ? "New boundary text" : "Boundary text", new(Wrap: true), key: "boundary-text");
        }, new(Width: 600, Height: 320));
        Rect Box(string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
        check(Box("auto").Width > 30 && Box("zero").Width == 30 && Box("clip").Width == 30,
            "Kumiko enables content minimums by default and honors explicit zero and clipping");
        var old = Box("auto").Width;
        Save("initial");
        changed = true;
        t.Frame();
        check(Box("auto").Width > old, "changed words invalidate the adapter's intrinsic minimum cache");
        Save("changed");
        t.Resize(700, 320);
        check(Box("boundary-text").X == Box("boundary").X + 34,
            "Kumiko fixed absolute boundary refreshes percentage padding after resize");
        Save("resized");
        void Save(string name)
        {
            var stem = Path.Combine(output, "yoga-upstream-" + name);
            t.Screenshot(stem + ".png");
            File.WriteAllText(stem + ".txt", t.Dump());
            t.WriteWarnings(stem + ".warnings.json");
        }
    }
}
