using Kumiko.Rendering;
using Kumiko.UI;

static class StyleReuseChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        foreach (var theme in new[] { Theme.Light, Theme.DarkTheme })
            Reuse(output, theme, check);
        Resets(output, check);
    }

    static void Reuse(string output, Theme theme, Action<bool, string> check)
    {
        var selected = 1;
        var compact = false;
        void View(Context c)
        {
            var t = c.Theme;
            using var root = c.Div(key: "root").SizeFull().Padding(24).Gap(20);
            c.Checkbox("Compact", ref compact);
            using (c.Div(key: "plans").Row().Gap(20))
            {
                foreach (var name in new[] { "Starter", "Team", "Enterprise" })
                {
                    var index = name == "Starter" ? 0 : name == "Team" ? 1 : 2;
                    using var card = c.Div(key: name).CardSurface(t).Selected(t, selected == index)
                        .Density(t, compact).Width(200).Height(120)
                        .Semantics(Role.Button, name, isChecked: selected == index);
                    if (card.Control().Clicked)
                        selected = index;
                    c.Text(name, GalleryStyles.PlanTitle(t), key: "title");
                }
            }
            using (c.Div(key: "override").CardSurface(t).Width(320).Px(t.Space.S2))
                c.Text("Local override", key: "override-label");
            var scoped = t with
            {
                Surface = Color.Hex(0x28604a)
            };
            using (c.Div(key: "scoped").CardSurface(scoped).Width(200).Height(70))
                c.Text("Scoped theme", new(Color: Color.White));
        }

        using var tester = new Tester(View, new(Width: 740, Height: 440, Theme: theme, DevMode: true));
        var initialId = tester.Get(Role.Button, "Enterprise").Id;
        var starter = Box(tester, "Starter");
        var team = Box(tester, "Team");
        var enterprise = Box(tester, "Enterprise");
        var pixels = tester.Render();
        foreach (var name in new[] { "Starter", "Team", "Enterprise" })
        {
            var card = Box(tester, name);
            var title = tester.Context.Nodes.ToArray().Single(n => n.Text == name).Rect;
            check(Near(title.X - card.X, theme.Space.S4) && Near(title.Y - card.Y, theme.Space.S4),
                "shared styles preserve equal padding across independent cards");
            check(Matches(pixels, card.Right - 24, card.Bottom - 24, theme.Surface),
                "shared styles paint each card with the supplied theme surface");
        }
        check(Matches(pixels, starter.X, starter.Y + 60, theme.Border) &&
            Matches(pixels, team.X, team.Y + 60, theme.Accent), "selection variants compose over the shared card style");
        var local = Box(tester, "override");
        var label = tester.Context.Nodes.ToArray().Single(n => n.Key == "override-label").Rect;
        check(Near(local.Width, 320) && Near(label.X - local.X, theme.Space.S2) && Near(label.Y - local.Y, theme.Space.S4),
            "per-use width and horizontal padding overrides preserve vertical padding");
        var scoped = Box(tester, "scoped");
        check(Matches(pixels, scoped.Right - 24, scoped.Bottom - 24, Color.Hex(0x28604a)),
            "the same shared style accepts a different theme within one tree");
        Artifact(tester, output, theme.Dark ? "style-reuse-dark" : "style-reuse-light");
        tester.Get(Role.Button, "Enterprise").Click();
        tester.Get(Role.Checkbox, "Compact").Click();
        tester.Settle();
        pixels = tester.Render();
        enterprise = Box(tester, "Enterprise");
        team = Box(tester, "Team");
        var enterpriseTitle = tester.Context.Nodes.ToArray().Single(n => n.Text == "Enterprise").Rect;
        check(selected == 2 && tester.Get(Role.Button, "Enterprise").Id == initialId &&
            Matches(pixels, enterprise.X, enterprise.Y + 60, theme.Accent) &&
            Matches(pixels, team.X, team.Y + 60, theme.Border),
            "input changes the styled selection without leaking the old variant or changing identity");
        check(compact && Near(enterpriseTitle.X - enterprise.X, theme.Space.S2),
            "conditional density styles update through the input pipeline");
        var changedSurface = Color.Hex(theme.Dark ? 0x493a60u : 0xeee6fau);
        tester.Context.Theme = theme with
        {
            Surface = changedSurface
        };
        tester.Frame();
        pixels = tester.Render();
        check(Matches(pixels, enterprise.Right - 24, enterprise.Bottom - 24, changedSurface),
            "reused styles read changed themes on the next frame");
        tester.Get(Role.Checkbox, "Compact").Click();
        tester.Settle();
        enterpriseTitle = tester.Context.Nodes.ToArray().Single(n => n.Text == "Enterprise").Rect;
        check(!compact && Near(enterpriseTitle.X - enterprise.X, theme.Space.S4),
            "removing a conditional density variant restores the base spacing");
        Artifact(tester, output, theme.Dark ? "style-reuse-dark-changed" : "style-reuse-light-changed");
    }

    static void Resets(string output, Action<bool, string> check)
    {
        using var tester = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(24).Gap(24);
            using (c.Div(key: "painted").CardSurface(c.Theme).Width(160).Height(60).Rounded(0))
            {
            }
            using (c.Div(key: "cleared").CardSurface(c.Theme).Width(160).Height(60).Rounded(0)
                .Glass(new(Tint: Color.Hex(0xff0000))).Glass(null).Flat().Background(null))
            {
            }
            using (c.Div(key: "restored").Background(Color.White).Background(null)
                .Background(Color.Hex(0x28604a)).Width(160).Height(60))
            {
            }
            using (c.Div(key: "ratio").Width(160).AspectRatio(2).AspectRatio(null))
            using (c.Div(key: "content").Width(80).Height(30))
            {
            }
        }, new(Width: 400, Height: 380, DevMode: true));
        var pixels = tester.Render();
        var painted = Box(tester, "painted");
        var cleared = Box(tester, "cleared");
        check(!Matches(pixels, painted.X, painted.Y + 30, tester.Context.Theme.Background) &&
            !Matches(pixels, painted.X + 80, painted.Bottom + 3, tester.Context.Theme.Background),
            "the shared base paints a visible border and shadow before resets");
        check(Matches(pixels, cleared.X, cleared.Y + 30, tester.Context.Theme.Background) &&
            Matches(pixels, cleared.X + 80, cleared.Y + 30, tester.Context.Theme.Background) &&
            Matches(pixels, cleared.X + 80, cleared.Bottom + 3, tester.Context.Theme.Background),
            "null overrides remove shared fill, border, shadow and glass paint");
        var restored = Box(tester, "restored");
        check(Matches(pixels, restored.X + 80, restored.Y + 30, Color.Hex(0x28604a)),
            "a later fill overrides an explicit background reset");
        check(Near(Box(tester, "ratio").Height, 30), "resetting an aspect ratio restores content-driven height");
        Artifact(tester, output, "style-reuse-resets");
    }

    static Rect Box(Tester tester, string key) => tester.Context.Nodes.ToArray().Single(n => n.Key == key).Rect;
    static bool Near(float actual, float expected) => Math.Abs(actual - expected) < .01f;

    static bool Matches(Pixels pixels, float x, float y, Color color)
    {
        var index = ((int)y * pixels.Width + (int)x) * 4;
        return Math.Abs(pixels.Bytes[index + 2] - color.R * 255) <= 2 &&
            Math.Abs(pixels.Bytes[index + 1] - color.G * 255) <= 2 &&
            Math.Abs(pixels.Bytes[index] - color.B * 255) <= 2;
    }

    static void Artifact(Tester tester, string output, string name)
    {
        tester.Screenshot(Path.Combine(output, name + ".png"));
        File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        tester.WriteWarnings(Path.Combine(output, name + ".warnings.json"));
    }
}
