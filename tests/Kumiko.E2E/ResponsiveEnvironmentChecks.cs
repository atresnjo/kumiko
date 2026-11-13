using Kumiko;
using Kumiko.UI;

internal static class ResponsiveEnvironmentChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var clicks = 0;
        void Scene(Context c)
        {
            using var root = c.Div(key: "root").SizeFull().Padding(Space.S4).Gap(Space.S3);
            c.Text($"{c.WidthBreakpoint} / {c.HorizontalSizeClass} / {c.VerticalSizeClass}", key: "environment");
            var wide = c.HorizontalSizeClass == SizeClass.Regular;
            using var body = c.Div(key: "body").FillWidth().Flex().Gap(Space.S3);
            if (wide)
                body.Row();
            using (c.Div(key: "list").Flex().FillWidth().Background(c.Theme.Surface))
                c.Text("Companies", key: "heading");
            using (c.Div(key: "detail").Flex().FillWidth().Background(c.Theme.Surface))
                if (c.Button("Open company", key: "open").Clicked)
                    clicks++;
        }
        using var tester = new Tester(Scene, new(480, 900, Scale: 2, DevMode: true));
        var action = tester.Get(Role.Button, "Open company").Id;
        foreach (var (width, band, horizontal) in new[]
        {
            (480, Breakpoint.Compact, SizeClass.Compact),
            (800, Breakpoint.Medium, SizeClass.Regular),
            (1280, Breakpoint.Wide, SizeClass.Regular),
            (1800, Breakpoint.Wide, SizeClass.Regular),
            (599, Breakpoint.Compact, SizeClass.Compact),
            (600, Breakpoint.Medium, SizeClass.Regular),
            (899, Breakpoint.Medium, SizeClass.Regular),
            (900, Breakpoint.Expanded, SizeClass.Regular),
            (1199, Breakpoint.Expanded, SizeClass.Regular),
            (1200, Breakpoint.Wide, SizeClass.Regular)
        })
        {
            var previous = tester.Context.FrameId;
            tester.Resize(width, 900);
            check(tester.Context.FrameId > previous && tester.HasText($"{band} / {horizontal} / Regular"),
                $"resize rebuilds responsive content at {width} logical points independently of 2x scale");
            var list = Box(tester, "list");
            var detail = Box(tester, "detail");
            check(horizontal == SizeClass.Compact ? list.Bottom <= detail.Y : list.Right <= detail.X,
                $"panes do not overlap after reflow at {width}");
            check(detail.Right <= width && detail.Bottom <= 900 && detail.Width > 0 && detail.Height > 0,
                $"detail fits the resized viewport at {width}");
            tester.Get(Role.Button, "Open company").Click();
            check(tester.Get(Role.Button, "Open company").Id == action, "reflow retains the actionable keyed identity");
            check(tester.Context.SafeAreaInsets == default, "desktop has no unreported safe-area insets");
            Save(tester, output, $"environment-{width}");
        }
        check(clicks == 10, "hit testing reaches the same action exactly once after each reflow");
        tester.Resize(800, 599);
        check(tester.HasText("Medium / Regular / Compact"), "height-only resize updates vertical responsive content");
        tester.Resize(800, 600);
        check(tester.HasText("Medium / Regular / Regular"), "vertical class changes at 600 logical points");
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Responsive verification", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        var frames = 0;
        var compact = false;
        using var window = app.NewWindow(new(Title: "Responsive hidden", Width: 800, Height: 900, Visible: false), c =>
        {
            frames++;
            compact = c.HorizontalSizeClass == SizeClass.Compact;
            Scene(c);
        });
        app.RunFor(TimeSpan.FromMilliseconds(80));
        window.Resize(480, 900);
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(frames >= 2 && compact && !window.IsVisible && errors.Count == 0,
            "native resize invalidates the responsive view without showing a window");
    }

    internal static Rect Box(Tester tester, string key) => tester.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;

    internal static void Save(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, $"responsive-{name}.png"));
        File.WriteAllText(Path.Combine(output, $"responsive-{name}.txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, $"responsive-{name}.warnings.json"));
    }
}
