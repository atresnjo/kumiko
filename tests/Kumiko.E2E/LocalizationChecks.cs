using System.Collections.Immutable;
using System.Globalization;
using Kumiko;
using Kumiko.Components;
using Kumiko.UI;

/// <summary>Right-to-left mirroring, direction-aware input and built-in translations.</summary>
static class LocalizationChecks
{
    static readonly CultureInfo Hebrew = CultureInfo.GetCultureInfo("he-IL");

    public static void Run(string output, Action<bool, string> check)
    {
        Layout(output, check);
        Controls(output, check);
        Fields(check);
        Strings(check);
        CommandLine(output, check);
    }

    static Tester Open(Action<Context> view, CultureInfo culture, float width = 400, float height = 320)
    {
        var tester = new Tester(c =>
        {
            c.Culture = culture;
            view(c);
        }, new(Width: width, Height: height, DevMode: true));
        tester.Context.ReduceMotion = true;
        tester.Settle();
        return tester;
    }

    static Rect Box(Kumiko.Rendering.Instance instance) => new(instance.Rect.X, instance.Rect.Y, instance.Rect.Z, instance.Rect.W);

    static Node Find(Tester t, string key) => t.Context.Nodes.ToArray().Single(n => n.Key == key);

    static void Layout(string output, Action<bool, string> check)
    {
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(new Edges(0, 0, 0, 40)).Gap(8);
            using (c.Div(key: "row").Row().FillWidth().Height(30).Gap(10))
            {
                using (c.Div(key: "first").Width(50).FillHeight().Background(Color.Hex(0xe5484d)))
                {
                }
                using (c.Div(key: "second").Width(50).FillHeight().Background(Color.Hex(0x3e63dd)))
                {
                }
            }
            using (c.Div(key: "absolute").Absolute().Offset(10, 120).Width(30).Height(30).Background(Color.Hex(0x30a46c)))
            {
            }
            using (c.Div(key: "chart").Row().Flow(FlowDirection.LeftToRight).Width(200).Height(30).Gap(10))
            {
                using (c.Div(key: "c1").Width(50).FillHeight().Background(Color.Hex(0xf76b15)))
                {
                }
                using (c.Div(key: "c2").Width(50).FillHeight().Background(Color.Hex(0x8e4ec6)))
                {
                }
            }
            using (c.Div(key: "para").Width(200))
                c.Text("שלום עולם, זו פסקה ארוכה שעוברת לשורה הבאה בחלון צר.", new(Wrap: true), key: "text");
            using (c.Div(key: "scroll").FillWidth().Height(40).Scroll())
            {
                for (var i = 0; i < 6; i++)
                    c.Text($"שורה {i}", key: i);
            }
        }
        using var t = Open(View, Hebrew);
        var page = Find(t, "page").Rect;
        check(Find(t, "first").Rect.Right == page.Right - 40 && Find(t, "second").Rect.Right == page.Right - 100,
            "RTL rows start at the right inside start padding");
        check(Find(t, "absolute").Rect.Right == page.Right - 50, "absolute offsets measure from the right in RTL");
        var chart = Find(t, "chart").Rect;
        check(chart.Right == page.Right - 40 && Find(t, "c1").Rect.X == chart.X && Find(t, "c2").Rect.X == chart.X + 60,
            "a left-to-right subtree keeps its internal order inside an RTL window");
        var para = Find(t, "para").Rect;
        var glyphs = t.Context.Instances.Where(i => i.Kind == 1).Select(Box).Where(r => r.Y >= para.Y && r.Bottom <= para.Bottom + 4).ToArray();
        var lineHeight = t.Context.TextSystem.Measure("א", new()).Height;
        int Line(Rect g) => (int)((g.Y + g.Height / 2 - para.Y) / lineHeight);
        var lines = glyphs.GroupBy(Line).Select(g => (Start: g.Min(x => x.X), End: g.Max(x => x.Right))).ToArray();
        check(lines.Length > 1 && lines.All(l => Math.Abs(l.End - para.Right) < 3) && lines.Any(l => l.Start > para.X + 20),
            "wrapped RTL lines align to the right edge");
        var scroll = Find(t, "scroll").Rect;
        var thumb = t.Context.Instances.Where(i => i.Kind == 0).Select(Box).Where(r => r.Width == 3 && r.Y >= scroll.Y && r.Bottom <= scroll.Bottom + 1);
        check(thumb.Any(r => r.X < scroll.X + 8), "vertical scrollbars sit on the left in RTL");
        t.Screenshot(Path.Combine(output, "rtl-layout.png"));
        File.WriteAllText(Path.Combine(output, "rtl-layout.txt"), t.Dump());
    }

    static void Controls(string output, Action<bool, string> check)
    {
        DateOnly? date = new DateOnly(2026, 10, 21);
        var open = false;
        float pane = 200;
        float volume = 50;
        ImmutableArray<TreeNode> roots = [new("docs", "מסמכים", [new("a", "א")]), new("b", "ב")];
        var expanded = ImmutableHashSet<string>.Empty;
        string? selected = "docs";
        using var t = Open(c =>
        {
            var kit = c.Kit;
            using var page = c.Div(key: "page").SizeFull().Padding(16).Gap(12);
            kit.DatePicker("תאריך", date, open, d => date = d, o => open = o, new(new(Today: new(2026, 10, 20))), key: "date");
            kit.Slider("עוצמה", volume, v => volume = v, new(0, 100, 1, 200), key: "volume");
            using (c.Div(key: "split").Row().FillWidth().Height(60))
            {
                using (c.Div(key: "side").Width(pane).FillHeight())
                {
                }
                kit.ResizeHandle("רוחב", pane, w => pane = w, new(Min: 100, Max: 300), key: "handle");
            }
            kit.Tree("עץ", roots, expanded, selected, (id, on) => expanded = on ? expanded.Add(id) : expanded.Remove(id),
                id => selected = id, new(Height: 80), key: "tree");
        }, Hebrew, 520, 520);
        var sideways = t.Context.Nodes.ToArray().Where(n => n.Kind == ContentKind.Icon && n.Text is "chevron.left" or "chevron.right").ToArray();
        check(sideways.Length > 0 && sideways.All(n => n.Text == "chevron.left"), "collapsed tree chevrons point toward the reading direction");
        t.Get(Role.TreeItem, "מסמכים").Click();
        t.Key(Key.Left);
        t.Settle();
        check(expanded.Contains("docs"), "Left expands a tree branch in RTL");

        var slider = t.Get(Role.Slider, "עוצמה").Rect;
        t.Click(slider.Right - 6, slider.Y + slider.Height / 2);
        t.Settle();
        check(volume < 5, "the RTL slider minimum is at the right");
        t.Key(Key.Left);
        t.Settle();
        check(volume >= 1 && volume < 6, "Left increases an RTL slider");

        var handle = t.Get(Role.Separator, "רוחב").Rect;
        var y = handle.Y + handle.Height / 2;
        t.Press(handle.X + 3, y);
        t.MoveTo(handle.X - 37, y);
        t.Release(handle.X - 37, y);
        t.Settle();
        check(Math.Abs(pane - 240) < 1, "dragging an RTL handle toward the end grows the pane before it");

        t.Get(Role.Button, "תאריך").Click();
        t.Settle();
        var trigger = t.Get(Role.Button, "תאריך").Rect;
        var popup = t.Context.Accessibility.First(n => n.Role == Role.Grid && !n.Offscreen).Rect;
        check(open && popup.Right <= trigger.Right && popup.Right > trigger.Right - 40 && popup.Y >= trigger.Bottom,
            "an RTL popover opens below its trigger aligned to the trigger's start edge");
        var focused = t.Context.Accessibility.First(n => n.Focused).Name;
        t.Key(Key.Left);
        t.Settle();
        var next = t.Context.Accessibility.First(n => n.Focused).Name;
        check(focused == new DateOnly(2026, 10, 21).ToString("D", Hebrew) && next == new DateOnly(2026, 10, 22).ToString("D", Hebrew),
            "Left moves an RTL calendar to the next day");
        t.Screenshot(Path.Combine(output, "rtl-controls.png"));
        File.WriteAllText(Path.Combine(output, "rtl-controls.txt"), t.Dump());
    }

    static void Fields(Action<bool, string> check)
    {
        var text = "";
        using var t = Open(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(16);
            c.TextField(text, v =>
            {
                text = v;
            }, new(Width: 240, Name: "שם"), key: "name");
        }, Hebrew);
        var field = t.Get(Role.TextField, "שם").Rect;
        t.Click(field.X + field.Width / 2, field.Y + field.Height / 2);
        t.TypeText("abc");
        t.Settle();
        var content = t.Context.Nodes.ToArray().Last(n => n.Kind == ContentKind.Text && n.Text == "abc").Rect;
        check(Math.Abs(content.Right - (field.Right - 12)) < 2, "RTL text fields align their text to the right");
        var caret = t.Context.Nodes.ToArray().Where(n => n.Rect.Width == 1 && field.Contains(new(n.Rect.X, n.Rect.Y + 1))).ToArray();
        check(caret.Length == 1 && Math.Abs(caret[0].Rect.X - content.Right) < 2, "the RTL caret follows the end of the text");
        t.Click(field.X + 14, field.Y + field.Height / 2);
        t.TypeText("x");
        t.Settle();
        check(text == "xabc", "clicking before right-aligned text places the caret at its start");
        t.Click(field.Right - 6, field.Y + field.Height / 2);
        t.TypeText("y");
        t.Settle();
        check(text == "xabcy", "clicking after right-aligned text places the caret at its end");
    }

    static void Strings(Action<bool, string> check)
    {
        using var context = new Context();
        context.Culture = CultureInfo.GetCultureInfo("de-DE");
        check(context.Strings.PreviousMonth == "Vorheriger Monat" && context.Strings.Decrease.Contains("{0}", StringComparison.Ordinal),
            "a German window uses built-in German control text");
        context.Culture = CultureInfo.GetCultureInfo("de-AT");
        check(context.Strings.PreviousMonth == "Vorheriger Monat", "regional cultures fall back to their language");
        context.Culture = CultureInfo.GetCultureInfo("sw-KE");
        check(context.Strings == UiStrings.English, "an untranslated culture falls back to English");
        context.Strings = UiStrings.English with
        {
            Clear = "Effacer"
        };
        context.Culture = CultureInfo.GetCultureInfo("de-DE");
        check(context.Strings.Clear == "Effacer", "explicit strings are not overridden by the culture");
        foreach (var language in UiStrings.Languages)
        {
            var strings = UiStrings.For(CultureInfo.GetCultureInfo(language));
            var formatted = string.Format(CultureInfo.InvariantCulture, strings.NumberRange, 1, 2) + string.Format(CultureInfo.InvariantCulture,
                strings.Group, "a", "b", 3) + string.Format(CultureInfo.InvariantCulture, strings.Record, 7);
            check(formatted.Contains('1') && formatted.Contains('3') && formatted.Contains('7') && strings.PreviousMonth.Length > 0,
                $"{language} control text formats every placeholder");
        }
    }

    static void CommandLine(string output, Action<bool, string> check)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        var defaults = (CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            void View(Context c)
            {
                using var page = c.Div(key: "page").SizeFull().Padding(16);
                using (c.Div(key: "box").Width(60).Height(20))
                {
                }
                c.Text(new DateOnly(2026, 10, 21).ToString("d", c.Culture), key: "date");
            }
            var tree = Path.Combine(output, "rtl-cli.txt");
            Dev.Run("Locale", 300, 120, View, ["--kumiko-culture=he-IL", "--kumiko-scale=1", "--kumiko-tree=" + tree]);
            var dump = File.ReadAllText(tree);
            check(dump.Contains("key=\"box\" rect=(224,16,60,20)", StringComparison.Ordinal) && dump.Contains("21.10.2026", StringComparison.Ordinal),
                "--kumiko-culture sets formatting and direction offscreen");
        }
        finally
        {
            CultureInfo.CurrentCulture = before.Item1;
            CultureInfo.CurrentUICulture = before.Item2;
            CultureInfo.DefaultThreadCurrentCulture = defaults.Item1;
            CultureInfo.DefaultThreadCurrentUICulture = defaults.Item2;
        }
    }
}
