using Kumiko.UI;

internal static class YogaFeatureChecks
{
    internal static void Run(string output, Action<bool, string> check)
    {
        using var program = YogaFeaturesApp.CreateProgram();
        using var t = new Tester(program, new(Width: 390, Height: 720, DevMode: true));
        Save(t, output, "phone");
        CheckGeometry(t, check);
        var hidden = t.Get(Role.Button, "Hidden action").Id;
        var last = Box(t, "contents-last").X;
        Save(t, output, "phone");
        t.Get(Role.Button, "Toggle visibility").Click();
        check(!t.Context.Accessibility.Any(n => n.Id == hidden) && !t.HasText("Hidden action") && Box(t, "contents-last").X < last,
            "display-none removes descendants from paint, semantic actions and flex spacing");
        check(!t.Context.Warnings.Any(w => w.Path.Contains("hidden-scope", StringComparison.Ordinal)),
            "hidden descendants produce no geometry or contrast diagnostics");
        var exposed = t.Render();
        var outside = ((int)Box(t, "overflow").Y + 3) * exposed.Width * 4 + 8 * 4;
        check(exposed.Bytes[outside + 2] < 100, "visible overflow restores painting outside the box after a cached style change");
        Save(t, output, "hidden");
        t.Get(Role.Button, "Toggle visibility").Click();
        check(t.Get(Role.Button, "Hidden action").Id == hidden, "restored display scopes preserve keyed control identity");
        t.Resize(720, 720);
        CheckGeometry(t, check);
        Save(t, output, "wide");
        using var rtlProgram = YogaFeaturesApp.CreateProgram(new YogaFeaturesModel(Rtl: true));
        using var rtl = new Tester(rtlProgram, new(Width: 390, Height: 720, DevMode: true));
        check(Box(rtl, "auto-last").X == 12 && Box(rtl, "auto-first").Right == 378,
            "logical automatic margins mirror with right-to-left layout");
        Save(rtl, output, "rtl");
        Positioning(output, check);
        ResetAndFlex(output, check);
    }

    static void CheckGeometry(Tester t, Action<bool, string> check)
    {
        var row = Box(t, "auto-row");
        check(Box(t, "auto-first").X == row.X && Box(t, "auto-last").Right == row.Right,
            "automatic start margin consumes the row's remaining free space");
        var percent = Box(t, "percent-box");
        var padding = (t.Context.Size.Width - 24) * .05f + 2;
        check(Near(Box(t, "percent-child").X, percent.X + padding) &&
            Near(Box(t, "percent-child").Width, percent.Width - 2 * padding),
            "percentage padding and layout borders inset the rendered content");
        check(Near(Box(t, "percent-margin").X, Box(t, "percent-child").X + Box(t, "percent-child").Width * .05f),
            "percentage margins use the containing content width");
        var bounds = Box(t, "bounds");
        check(Near(Box(t, "minimum").Width, bounds.Width * .25f) &&
            Near(Box(t, "maximum").Width, bounds.Width * .4f) &&
            Near(Box(t, "maximum").X - Box(t, "minimum").Right, bounds.Width * .04f),
            "percentage min/max bounds and gaps recompute after resize");
        check(Box(t, "content-box").Width == 100 && Box(t, "content-box").Height == 60 &&
            Box(t, "border-box").Width == 80 && Box(t, "border-box").Height == 40,
            "content-box includes authored padding and layout borders while border-box keeps the authored size");
        var insets = Box(t, "insets");
        check(Near(Box(t, "pinned").Right, insets.Right - 8) && Near(Box(t, "pinned").Y, insets.Y + (insets.Height - 16) * .1f) &&
            Near(Box(t, "stretched").Width, insets.Width - 16) && Box(t, "stretched").Bottom == insets.Bottom - 8,
            "absolute logical/percentage insets and opposing insets position and stretch boxes");
        check(Near(Box(t, "static-marker").X, insets.X + 5) && Near(Box(t, "static-marker").Y, insets.Y + 5),
            "absolute descendants of static scopes export in the positioned ancestor's coordinate system");
        check(Box(t, "contents-first").X == Box(t, "display-row").X &&
            !t.Context.Instances.Any(i => i.Color.X > .99 && i.Color.Y == 0 && i.Color.Z == 0),
            "boxless display scopes ignore their own padding and background");
        var pixels = t.Render();
        var outside = ((int)Box(t, "overflow").Y + 3) * pixels.Width * 4 + 8 * 4;
        check(pixels.Bytes[outside] == 255 && pixels.Bytes[outside + 1] == 255 && pixels.Bytes[outside + 2] == 255,
            "hidden overflow clips painted descendants at the viewport edge");
        var reference = t.Context.TextSystem.Shape("Reference", new(Size: 24)).Ascent;
        var selected = t.Context.TextSystem.Shape("Selected baseline", new(Size: 14)).Ascent;
        check(Near(Box(t, "reference").Y + reference, Box(t, "selected-baseline").Y + selected),
            "baseline-reference children select a nested container's exported font baseline");
    }

    static void Positioning(string output, Action<bool, string> check)
    {
        using var t = new Tester(c =>
        {
            using var root = c.Div().SizeFull();
            using var containing = c.Div(key: "containing").Position(PositionMode.Relative).Width(200).Height(180).Padding(20);
            using (c.Div(key: "static").Position(PositionMode.Static).Width(100).Height(80).Inset(30))
            using (c.Div(key: "absolute").Position(PositionMode.Absolute).Width(20).Height(20).Inset(new LengthEdges(Top: 5, Left: 5)))
            {
            }
            using (c.Div(key: "explicit-block").Position(PositionMode.Static).ContainingBlock().Width(100).Height(60))
            using (c.Div(key: "block-child").Position(PositionMode.Absolute).Width(20).Height(20).Inset(new LengthEdges(Top: 5, Left: 5)))
            {
            }
        }, new(Width: 260, Height: 300));
        check(Box(t, "static").X == 20 && Box(t, "static").Y == 20 && Box(t, "absolute").X == 5 && Box(t, "absolute").Y == 5,
            "static boxes ignore their insets and absolute descendants use the nearest containing block");
        check(Box(t, "block-child").X == Box(t, "explicit-block").X + 5 &&
            Box(t, "block-child").Y == Box(t, "explicit-block").Y + 5,
            "an explicit containing-block flag bounds descendants of a static box");
        Save(t, output, "positioning");
    }

    static void ResetAndFlex(string output, Action<bool, string> check)
    {
        var changed = false;
        using var t = new Tester(c =>
        {
            using var root = c.Div().SizeFull();
            using (c.Div(key: "flex").Row().Width(300).Height(40))
            {
                using (c.Div(key: "flex-one").FlexValue(changed ? null : 1).Height(20))
                {
                }
                using (c.Div(key: "flex-two").FlexValue(2).Height(20))
                {
                }
            }
            using (c.Div(key: "negative-flex").Row().Width(300).Height(30))
            {
                using (c.Div(key: "negative-one").Width(200).Height(20).FlexValue(-1))
                {
                }
                using (c.Div(key: "negative-two").Width(200).Height(20).FlexValue(-2))
                {
                }
            }
            using (c.Div(key: "contents-change").Row().Width(200).Height(30))
            {
                using (c.Div(key: "changing-box").Display(changed ? DisplayMode.Flex : DisplayMode.Contents).Width(60).Height(20))
                using (c.Div(key: "flattened-child").Width(30).Height(20))
                {
                }
                using (c.Div(key: "following-box").Width(40).Height(20))
                {
                }
            }
            using (c.Div(key: "typed-ratio-row").Row().Width(160).Height(50))
            using (c.Div(key: "typed-ratio").FillHeight().AspectRatio(2).Margin(LengthEdges.All(5)))
            {
            }
            using (c.Div(key: "capped-row").Row().Width(160).Height(50).ItemsCenter())
            using (c.Div(key: "capped-fill").Width(40).FillHeight().MaxHeight(20).Margin(LayoutEdge.Start, Length.Auto))
            {
            }
            using var box = c.Div(key: "reset").Width(200).Height(100).Padding(Length.Percent(10)).Padding(5)
                .Margin(Length.Auto).Margin(3).Gap(Length.Percent(10)).Gap(4).MaxWidth(Length.Percent(25)).MaxWidth(180);
        }, new(Width: 400, Height: 400));
        check(Box(t, "flex-one").Width == 100 && Box(t, "flex-two").Width == 200,
            "Yoga flex shorthand distributes zero-basis free space by signed flex value");
        check(Near(Box(t, "negative-one").Width, 166.667f) && Near(Box(t, "negative-two").Width, 133.333f),
            "negative Yoga flex shorthand supplies basis-weighted shrink factors");
        check(Box(t, "following-box").X == 30, "display-contents descendants participate directly in their layout parent");
        check(Box(t, "typed-ratio").Width == 80 && Box(t, "typed-ratio").Height == 40,
            "typed edge margins preserve filled aspect-ratio sizing");
        check(Box(t, "capped-fill").X == 120 && Box(t, "capped-fill").Y == Box(t, "capped-row").Y + 15,
            "main-axis auto margins preserve bounded cross-axis Fill alignment");
        changed = true;
        t.Frame();
        check(Box(t, "flex-one").Width == 0 && Box(t, "flex-two").Width == 300 && Box(t, "reset").Width == 180,
            "resetting shorthand and overriding typed styles with pixel APIs clears cached properties");
        check(Box(t, "following-box").X == 60, "changing contents back to a box rebuilds native-independent layout parenting");
        Save(t, output, "reset");
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < .05f;
    static Rect Box(Tester t, string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
    static void Save(Tester t, string output, string name)
    {
        var stem = Path.Combine(output, "yoga-features-" + name);
        t.Screenshot(stem + ".png");
        File.WriteAllText(stem + ".txt", t.Dump());
        t.WriteWarnings(stem + ".warnings.json");
    }
}
