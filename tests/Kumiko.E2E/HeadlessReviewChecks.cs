using System.Globalization;
using Kumiko.Components;
using Kumiko.UI;

/// <summary>Content-only parts retain readable chrome, active targets and caller configuration.</summary>
static class HeadlessReviewChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        Commands(output, check);
        Defaults(output, check);
        Culture(output, check);
        Foregrounds(output, check);
    }

    static Tester Open(Action<Context> render)
    {
        var t = new Tester(c =>
        {
            using var root = c.Div(key: "root").SizeFull().Padding(16);
            render(c);
        }, new(Width: 520, Height: 420, Scale: 1, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        return t;
    }

    static AccessibleNode Node(Tester t, Role role, string name) =>
        t.Context.Accessibility.Single(n => n.Role == role && n.Name == name);

    static bool Surface(Tester t, AccessibleNode node, Color color) =>
        t.Context.Instances.Any(i => i.Kind == 0 && i.Color == new System.Numerics.Vector4(color.R, color.G, color.B, color.A) &&
            MathF.Abs(i.Rect.X - node.Rect.X) < .1f && MathF.Abs(i.Rect.Y - node.Rect.Y) < .1f &&
            MathF.Abs(i.Rect.Z - node.Rect.Width) < .1f && MathF.Abs(i.Rect.W - node.Rect.Height) < .1f);

    static void Save(Tester t, string output, string name)
    {
        t.Screenshot(Path.Combine(output, "parts-review-" + name + ".png"));
        File.WriteAllText(Path.Combine(output, "parts-review-" + name + ".txt"), t.Dump());
        t.WriteWarnings(Path.Combine(output, "parts-review-" + name + ".warnings.json"));
    }

    static void Commands(string output, Action<bool, string> check)
    {
        foreach (var custom in new[] { false, true })
        {
            var states = new Dictionary<string, CommandItemState>();
            using var t = Open(c => c.Kit.CommandMenu(null, "", _ => { },
                [new("off", "Disabled") { Disabled = true }, new("a", "Alpha"), new("b", "Beta")], _ => { }, () => { },
                new CommandProps
                {
                    RenderItem = custom ? (ctx, item, state) =>
                    {
                        states[item.Id] = state;
                        using var content = ctx.Div(key: "content").FillWidth().Height(32).Center();
                        ctx.Text(item.Label, key: "label");
                    }
                    : null
                }, key: "command"));
            t.Get(Role.TextField, "Type to search…").Click();
            t.Settle();
            var alpha = Node(t, Role.MenuItem, "Alpha");
            var beta = Node(t, Role.MenuItem, "Beta");
            check(Surface(t, alpha, t.Context.Kit.Theme.Accent) && !Surface(t, beta, t.Context.Kit.Theme.Accent),
                (custom ? "custom" : "default") + " command paints the enabled Enter target while searching");
            Save(t, output, custom ? "command-custom" : "command-default");
            t.MoveTo(beta.Rect.X + 10, beta.Rect.Y + beta.Rect.Height / 2);
            t.Settle();
            check(Surface(t, beta, t.Context.Kit.Theme.Accent) && !Surface(t, alpha, t.Context.Kit.Theme.Accent),
                "command pointer movement transfers Kit-owned chrome to the hovered row");
            if (custom)
                check(states["b"] is { Highlighted: true, Focused: true, Hovered: true } && !states["off"].Hovered,
                    "command state separates enabled hover from its active target");
            t.Key(Key.Up);
            t.Settle();
            if (custom)
                check(states["a"] is { Highlighted: true, Focused: true, Hovered: false },
                    "command keyboard navigation highlights its current row under a resting pointer");
        }
    }

    static void Defaults(string output, Action<bool, string> check)
    {
        foreach (var gap in new[] { 0f, 5f })
        {
            using var t = Open(c => c.Kit.CommandMenu(new Rect(30, 240, 120, 24), "", _ => { },
                [new("a", "Alpha"), new("b", "Beta")], _ => { }, () => { },
                new CommandProps
                {
                    Placeholder = "Locate",
                    Empty = "Nothing here",
                    MaxHeight = 90,
                    Side = Side.Top,
                    Align = PopoverAlign.End,
                    ResultsGap = gap,
                    ResultsPadding = gap,
                    RenderItem = (ctx, item, _) =>
                    {
                        using var row = ctx.Div(key: "content").FillWidth().Height(24);
                        ctx.Text(item.Label, key: "label");
                    }
                }, key: "command"));
            var first = Node(t, Role.MenuItem, "Alpha").Rect;
            var second = Node(t, Role.MenuItem, "Beta").Rect;
            var results = Node(t, Role.List, "Results").Rect;
            check(MathF.Abs(second.Y - first.Bottom - gap) < .1f && MathF.Abs(first.X - results.X - gap) < .1f,
                $"command initializer without Width preserves explicit padding/gap {gap}");
            check(t.Context.Accessibility.Any(n => n.Role == Role.TextField && n.Name == "Locate") &&
                results.Bottom <= 240 && MathF.Abs(results.Width - 380) < .1f,
                "command initializer keeps its placeholder and top placement with default width");
            Save(t, output, "spacing-" + gap.ToString(CultureInfo.InvariantCulture));
        }
    }

    static void Culture(string output, Action<bool, string> check)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            string chosen = "";
            using var t = Open(c =>
            {
                c.Culture = CultureInfo.GetCultureInfo("tr-TR");
                c.Kit.CommandMenu(null, "ı", _ => { }, [new("city", "Istanbul")], id => chosen = id, () => { },
                    new CommandProps { Embedded = true }, key: "command");
            });
            check(t.HasText("Istanbul"), "command filtering uses Turkish context culture instead of English process culture");
            t.Get(Role.TextField, "Type to search…").Click();
            t.Key(Key.Enter);
            check(chosen == "city", "Enter chooses the result matched with context culture");
            Save(t, output, "culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    static void Foregrounds(string output, Action<bool, string> check)
    {
        foreach (var theme in new[] { KitTheme.Light, KitTheme.Dark })
        {
            Color tabColor = default, treeColor = default, commandColor = default;
            var commandActive = false;
            using var t = Open(c =>
            {
                c.Theme = theme.Theme;
                using var scope = c.Kit.Use(theme);
                c.Kit.Tabs("Primary tabs", [new("a", "Selected tab"), new("b", "Other tab")], "a", _ => { },
                    new TabsProps(TabsVariant.Pill)
                    {
                        RenderItem = (ctx, item, state) =>
                        {
                            if (state.Selected)
                                tabColor = state.Foreground;
                            ctx.Text(item.Label, new(Color: state.Foreground), key: "label");
                        }
                    }, key: "tabs");
                c.Kit.Tree("Selected tree", [new("a", "Selected tree row")], new HashSet<string>(), "a", (_, _) => { }, _ => { },
                    new TreeProps(Height: 40)
                    {
                        RenderItem = (ctx, item, state) =>
                        {
                            treeColor = state.Foreground;
                            ctx.Text(item.Label, new(Color: state.Foreground), key: "label");
                        }
                    }, key: "tree");
                c.Kit.CommandMenu(null, "", _ => { }, [new("a", "Command")], _ => { }, () => { },
                    new CommandProps
                    {
                        Embedded = true,
                        RenderItem = (ctx, item, state) =>
                        {
                            commandColor = state.Foreground;
                            commandActive = state.Highlighted;
                            ctx.Text(item.Label, new(Color: state.Foreground), key: "label");
                        }
                    }, key: "command");
            });
            check(KitTheme.Contrast(tabColor, theme.Primary.Background) >= 4.5f &&
                !t.Context.AuditAccessibility().Any(f => f.Code == "contrast"),
                "custom selected Primary tab uses a readable foreground on Kit's surface");
            check(treeColor == theme.AccentForeground && commandColor == (commandActive ? theme.AccentForeground : theme.PopoverForeground),
                "tree and command content receive their default text foreground");
            Save(t, output, theme.IsDark ? "foreground-dark" : "foreground-light");
        }
    }
}
