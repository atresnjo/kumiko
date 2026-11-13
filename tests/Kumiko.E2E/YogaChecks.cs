using Kumiko;
using Kumiko.UI;

internal static class YogaChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var expanded = false;
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(10).Gap(10);
            if (c.Button("Change layout", key: "change").Clicked)
                expanded = !expanded;
            using (var content = c.Div(key: "content").FillWidth().Gap(8))
            {
                if (expanded)
                    content.RowReverse().Wrap();
                using (var item = c.Div(key: "item").Padding(4))
                {
                    if (expanded)
                        item.Width(100).AlignSelf(Align.End).AspectRatio(2).Margin(3);
                    c.Text(expanded ? "Short" : "A longer label that wraps across several lines when the available width changes.",
                        new(Size: expanded ? 22 : 14, Wrap: true), key: "label");
                }
                if (expanded)
                    using (c.Div(key: "extra").Width(80).Height(30).Background(Color.Hex(0x2368aa)))
                    {
                    }
                c.Text("Following content", key: "following");
            }
            c.Button("Last action", key: "last");
        }, new(Width: 260, Height: 400, DevMode: true));
        var initial = (Box(t, "content"), Box(t, "item"), Box(t, "following"));
        var id = t.Get(Role.Button, "Last action").Id;
        Save(t, output, "initial");
        t.Get(Role.Button, "Change layout").Click();
        check(t.HasText("Short") && !t.ContainsText("A longer label"),
            "rebuilt Yoga trees present changed text and discard removed payloads");
        check(t.Get(Role.Button, "Last action").Id == id,
            "Yoga reflow keeps semantic controls keyed to their domain identity");
        Save(t, output, "changed");
        t.Get(Role.Button, "Change layout").Click();
        check((Box(t, "content"), Box(t, "item"), Box(t, "following")) == initial, "returning to the initial view clears stale Yoga styles and child links");
        var oldHeight = Box(t, "item").Height;
        t.Resize(150, 500);
        check(Box(t, "item").Height > oldHeight && Box(t, "following").Y >= Box(t, "item").Bottom + 8,
            "cached Yoga text remeasures at a narrower width before positioning subsequent content");
        Save(t, output, "narrow");
        t.Resize(260, 400);
        check((Box(t, "content"), Box(t, "item"), Box(t, "following")) == initial, "repeated Yoga resize reproduces the initial geometry");
        Save(t, output, "restored");
        using var ratio = new Tester(c =>
        {
            using var row = c.Div().Row().SizeFull();
            using var card = c.Div(key: "ratio").FillHeight().AspectRatio(2).Margin(5)
                .Background(Color.Hex(0x2368aa));
        }, new(Width: 160, Height: 50));
        check(Box(ratio, "ratio") == new Rect(5, 5, 80, 40),
            $"filled aspect-ratio cards reserve their outer margins before deriving the other axis ({Box(ratio, "ratio")})");
        Save(ratio, output, "ratio-margin");
    }

    static Rect Box(Tester t, string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
    static void Save(Tester t, string output, string name)
    {
        var path = Path.Combine(output, "yoga-" + name);
        t.Screenshot(path + ".png");
        File.WriteAllText(path + ".txt", t.Dump());
        t.WriteWarnings(path + ".warnings.json");
    }
}
