using Kumiko;
using Kumiko.Components;
using Kumiko.UI;

/// <summary>High-contrast and scaled themes, system preference application and the accessibility audit.</summary>
static class AccessibilityChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        Contrast(output, check);
        TextScale(output, check);
        Preferences(check);
        Audit(output, check);
    }

    static float Ratio(Color a, Color b)
    {
        static float Luminance(Color c)
        {
            static float Channel(float v) => v <= .03928f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
            return .2126f * Channel(c.R) + .7152f * Channel(c.G) + .0722f * Channel(c.B);
        }
        var (light, dark) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (light + .05f) / (dark + .05f);
    }

    static void View(Context c)
    {
        var kit = c.Kit;
        using var page = c.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S3);
        c.Text("Account settings", c.Theme.Typography.Title, key: "title");
        c.Text("Changes are saved when you press Save.", c.Theme.Typography.Body with
        {
            Color = c.Theme.Muted
        }, key: "hint");
        c.TextField("Ada", _ =>
        {
        }, new(Width: 260, Name: "Name"), key: "name");
        using (c.Div(key: "actions").Row().Gap(Space.S2))
        {
            kit.Button("Save", new(Tone.Primary), key: "save");
            kit.Button("Cancel", new(Appearance: Appearance.Outline), key: "cancel");
            kit.Button("Archive", new(Disabled: true), key: "archive");
        }
        kit.Switch("Notifications", true, _ =>
        {
        }, key: "notify");
        kit.Checkbox("Weekly summary", false, _ =>
        {
        }, key: "weekly");
    }

    static void Contrast(string output, Action<bool, string> check)
    {
        foreach (var (name, theme) in new[] { ("light", Theme.HighContrast), ("dark", Theme.HighContrastDark) })
        {
            check(Ratio(theme.Text, theme.Background) >= 7 && Ratio(theme.Muted, theme.Background) >= 7,
                $"high-contrast {name} text and secondary text reach 7:1");
            check(Ratio(theme.AccentText, theme.Accent) >= 7 && Ratio(theme.SelectionText, theme.Selection) >= 7,
                $"high-contrast {name} accent and selection text reach 7:1");
            check(Ratio(theme.Border, theme.Background) >= 4.5f && Ratio(theme.FocusRing, theme.Background) >= 4.5f &&
                Ratio(theme.Accent, theme.Background) >= 3 && theme.Controls.FocusWidth >= 3,
                $"high-contrast {name} borders, focus rings and accents stand out from the background");
            using var t = new Tester(View, new(Width: 420, Height: 320, Theme: theme, DevMode: true));
            t.Context.ReduceMotion = true;
            t.Get(Role.TextField, "Name").Click();
            t.Settle();
            t.Screenshot(Path.Combine(output, $"contrast-{name}.png"));
            File.WriteAllText(Path.Combine(output, $"contrast-{name}.txt"), t.Dump());
            check(!t.Context.AuditAccessibility().Any(f => f.Code == "contrast"), $"high-contrast {name} views pass the contrast audit");
        }
        check(KitTheme.HighContrast.Primary.Background == Theme.HighContrast.Accent && KitTheme.HighContrastDark.IsDark,
            "kit high-contrast themes follow the core themes");
    }

    static void TextScale(string output, Action<bool, string> check)
    {
        var scaled = Theme.ShadcnLight.WithTextScale(1.5f);
        check(scaled.Typography.Body.Size == Theme.ShadcnLight.Typography.Body.Size * 1.5f &&
            scaled.Controls.Height == MathF.Round(Theme.ShadcnLight.Controls.Height * 1.5f), "text scaling enlarges type and controls together");
        check(KitTheme.Of(scaled).Metrics.HeightSmall > KitTheme.Light.Metrics.HeightSmall, "kit controls grow with scaled text");
        using var t = new Tester(View, new(Width: 560, Height: 420, Theme: scaled, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        t.Screenshot(Path.Combine(output, "text-scale.png"));
        var clipped = t.Context.Warnings.Where(w => w.Code is "text-overflow" or "overflow").ToArray();
        check(clipped.Length == 0, "controls keep their labels at 150% text: " + string.Join("; ", clipped.Select(w => w.Path)));
    }

    static void Preferences(Action<bool, string> check)
    {
        var light = Kumiko.Desktop.Appearance.ThemeFor(false, false, 1, null);
        var contrast = Kumiko.Desktop.Appearance.ThemeFor(true, true, 1, null);
        var system = Theme.HighContrast with
        {
            Accent = Color.Hex(0x1aebff)
        };
        check(light == Theme.Light && contrast == Theme.HighContrastDark, "system contrast and darkness choose the default theme");
        check(Kumiko.Desktop.Appearance.ThemeFor(false, true, 1, system) == system, "Windows system colors replace the built-in high-contrast palette");
        check(Kumiko.Desktop.Appearance.ThemeFor(false, false, 1.25f, null).Typography.Body.Size == Theme.Light.Typography.Body.Size * 1.25f,
            "system text size scales the default theme");

        using var app = new App(new("Preferences", Hidden: true));
        using var window = app.NewWindow(new(Title: "Preferences", Width: 200, Height: 100, Visible: false), _ =>
        {
        });
        using var themed = app.NewWindow(new(Title: "Themed", Width: 200, Height: 100, Visible: false, Theme: Theme.ShadcnDark), _ =>
        {
        });
        window.ApplyPreferences(true, Theme.HighContrast);
        themed.ApplyPreferences(true, Theme.HighContrast);
        check(window.Context!.ReduceMotion && window.Context.Theme == Theme.HighContrast, "a window applies reduced motion and the system theme");
        check(themed.Context!.ReduceMotion && themed.Context.Theme == Theme.ShadcnDark, "an explicit window theme survives system preference changes");
    }

    static void Audit(string output, Action<bool, string> check)
    {
        void Problems(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(16).Gap(8);
            using (var icon = c.Div(key: "unnamed").Width(32).Height(32).Background(c.Theme.Surface))
            {
                icon.Control();
                c.Icon("trash", 16, key: "glyph");
            }
            using (c.Div(key: "crowded").Row().Gap(2))
            {
                using (var tiny = c.Div(key: "tiny").Width(16).Height(16).Semantics(Role.Button, "Tiny"))
                    tiny.Control();
                using (var next = c.Div(key: "next").Width(16).Height(16).Semantics(Role.Button, "Next"))
                    next.Control();
            }
            using (var spaced = c.Div(key: "spaced").Width(16).Height(16).Semantics(Role.Button, "Spaced"))
                spaced.Control();
            using (var hidden = c.Div(key: "hidden").Width(80).Height(32).TabStop(false).Semantics(Role.Button, "Skipped"))
                hidden.Control();
            using (c.Div(key: "pair").Row().Gap(8))
            {
                c.Button("Delete", key: "one");
                c.Button("Delete", key: "two");
            }
            c.Text("Faint text", new(Color: Color.Hex(0xd0d0d0)), key: "faint");
            c.Icon("star", 16, key: "decoration");
            c.Button("Disabled", new(Disabled: true), key: "disabled");
        }
        using var t = new Tester(Problems, new(Width: 360, Height: 320));
        var findings = t.Context.AuditAccessibility();
        string[] Codes(string key) => findings.Where(f => f.Path.EndsWith($"[\"{key}\"]", StringComparison.Ordinal)).Select(f => f.Code).Order().ToArray();
        check(Codes("unnamed").SequenceEqual(["unnamed-control"]), "the audit reports an unnamed icon-only control");
        check(Codes("tiny").SequenceEqual(["target-size"]) && Codes("next").SequenceEqual(["target-size"]),
            "the audit reports crowded pointer targets below 24×24");
        check(Codes("spaced").Length == 0, "the audit applies the WCAG 2.5.8 spacing exception");
        check(Codes("hidden").SequenceEqual(["keyboard"]), "the audit reports a control keyboard users cannot reach");
        check(Codes("two").SequenceEqual(["duplicate-name"]) && Codes("one").Length == 0, "the audit reports indistinguishable sibling controls");
        check(Codes("faint").SequenceEqual(["contrast"]), "the audit reports low-contrast text");
        check(Codes("decoration").Length == 0 && Codes("disabled").Length == 0, "decorative and disabled content is not reported");
        File.WriteAllText(Path.Combine(output, "accessibility-audit.json"), string.Join('\n', findings.Select(f => $"{f.Code} {f.Path}")) + "\n");
    }
}
