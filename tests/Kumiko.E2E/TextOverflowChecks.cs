using Kumiko.Rendering;
using Kumiko.UI;

internal static class TextOverflowChecks
{
    static readonly TextStyle Style = new(Size: 18, Mono: true, Color: Color.Black);
    const string Long = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    internal static void Run(string output, Action<bool, string> check)
    {
        using var metrics = new Context();
        var width = metrics.TextSystem.Measure("ABC…", Style).Width + .25f;
        using var actual = Label(Long, width, TextOverflow.Ellipsis);
        using var expected = Label("ABC…", width, TextOverflow.Visible);
        check(Equal(actual, expected), "ellipsis paints the fitting prefix and marker at the allocated width");
        check(actual.Context.Accessibility.Any(n => n.Name == Long), "ellipsized labels retain their complete accessible text");
        Save(actual, output, "ellipsis");
        var box = actual.FindText(Long)!.Value;
        actual.Press(box.X, box.Y + box.Height / 2);
        actual.MoveTo(box.X + metrics.TextSystem.Measure("AB", Style).Width, box.Y + box.Height / 2);
        actual.Release(box.X + metrics.TextSystem.Measure("AB", Style).Width, box.Y + box.Height / 2);
        actual.Key(Key.C, Modifiers.Primary);
        check(actual.Clipboard == "AB", "partial selection copies only retained source characters");
        actual.Press(box.X, box.Y + box.Height / 2);
        actual.MoveTo(box.Right, box.Y + box.Height / 2);
        actual.Release(box.Right, box.Y + box.Height / 2);
        actual.Key(Key.C, Modifiers.Primary);
        check(actual.Clipboard == Long, "selection through an ellipsis copies the complete source to the private clipboard");
        Save(actual, output, "selection");

        foreach (var cluster in new[] { "👩‍💻", "e\u0301", "🇪🇸" })
        {
            var fit = metrics.TextSystem.Measure(cluster + "…", Style).Width + .25f;
            using var unicode = Label(cluster + Long, fit, TextOverflow.Ellipsis);
            using var reference = Label(cluster + "…", fit, TextOverflow.Visible);
            Save(unicode, output, "unicode-" + (int)cluster[0]);
            Save(reference, output, "unicode-reference-" + (int)cluster[0]);
            check(Equal(unicode, reference), "truncation preserves the complete grapheme " + cluster);
        }
        using (var tiny = Label(Long, 1, TextOverflow.Ellipsis))
        using (var blank = Label("", 1, TextOverflow.Visible))
        {
            check(Equal(tiny, blank), "a box narrower than the ellipsis paints no partial marker");
            check(Find(tiny, "text").Rect.Height == Find(blank, "text").Rect.Height,
                "suppressed text retains its line height");
        }
        using (var clipped = Label(Long, width, TextOverflow.Clip))
        using (var reference = Label(Long, width, TextOverflow.Visible, parentClip: true))
            check(Equal(clipped, reference), "clip paints the original glyphs inside the text box without a marker");
        using (var exact = Label("ABC", metrics.TextSystem.Measure("ABC", Style).Width, TextOverflow.Ellipsis))
        using (var reference = Label("ABC", metrics.TextSystem.Measure("ABC", Style).Width, TextOverflow.Visible))
            check(Equal(exact, reference), "exactly fitting text is not shortened");
        using (var lines = Label(Long + "\n" + Long, width, TextOverflow.Ellipsis))
        using (var reference = Label("ABC…\nABC…", width, TextOverflow.Visible))
            check(Equal(lines, reference), "explicit line breaks retain independent ellipsized lines");

        using (var zero = Label(Long, 0, TextOverflow.Ellipsis))
        using (var blank = Label("", 0, TextOverflow.Visible))
            check(Equal(zero, blank) && Find(zero, "text").Rect.Height > 0,
                "zero-width text paints nothing without losing line height");
        using (var unbounded = Label(Long, 340, TextOverflow.Ellipsis))
        using (var reference = Label(Long, 340, TextOverflow.Visible))
            check(Equal(unbounded, reference), "text with sufficient space retains its original rendering");
        using (var overflow = new Tester(c =>
        {
            using var parent = c.Div(key: "short").Width(width).Height(5);
            c.Text(Long, Style with
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        }, new(Width: 360, Height: 100, DevMode: true)))
            check(overflow.Context.Warnings.Any(w => w.Code == "text-overflow"),
                "horizontal ellipsis does not hide a real vertical overflow warning");

        Resize(output, check);
        Rich(output, check);
        RightToLeft(output, check);
        MixedDirection(output, check);
        WrappedClip(output, check);
        try
        {
            using var invalid = new Tester(c => c.Text(Long, Style with { Wrap = true, Overflow = TextOverflow.Ellipsis }));
            check(false, "wrapped ellipsis must reject the unsupported combination");
        }
        catch (ArgumentException)
        {
            check(true, "wrapped ellipsis reports the unsupported combination");
        }
    }

    static Tester Label(string text, float width, TextOverflow overflow, bool parentClip = false) => new(c =>
    {
        using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8).Selectable();
        using var box = c.Div(key: "box").Width(width);
        if (parentClip)
            box.Clip();
        c.Text(text, Style with
        {
            Overflow = overflow
        }, key: "text");
    }, new(Width: 360, Height: 100, DevMode: true));

    static void Resize(string output, Action<bool, string> check)
    {
        var value = Long;
        var style = Style with
        {
            Overflow = TextOverflow.Ellipsis
        };
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8);
            using var row = c.Div(key: "row").Row().FillWidth().ItemsCenter().Gap(8);
            c.Text(value, style, key: "text");
            using var badge = c.Div(key: "badge").Width(40).Height(24).Background(Color.Hex(0x335588));
        }
        using var live = new Tester(View, new(Width: 150, Height: 80, DevMode: true));
        foreach (var size in new[] { 150, 500, 90, 150 })
        {
            live.Resize(size, 80);
            var text = Find(live, "text").Rect;
            var badge = Find(live, "badge").Rect;
            check(text.Right + 7.9f <= badge.X && badge.Right <= size - 7.9f,
                "text shrinks without moving its fixed sibling outside the row at " + size);
            using var fresh = new Tester(View, new(Width: size, Height: 80));
            check(Equal(live, fresh), "resized text matches a fresh rendering at " + size);
            check(!live.Context.Warnings.Any(w => w.Code is "overflow" or "text-overflow"),
                "intentional truncation has no overflow warnings at " + size);
            Save(live, output, "resize-" + size);
        }
        var before = live.Render().Bytes.ToArray();
        value = "ZYXWVUTSRQPONMLKJIHGFEDCBA";
        live.Frame();
        using (var fresh = new Tester(View, new(Width: 150, Height: 80)))
            check(Equal(live, fresh) && !before.SequenceEqual(live.Render().Bytes),
                "same-sized replacement text repaints despite cached Yoga geometry");
        style = style with
        {
            Size = 24,
            Weight = Weight.Bold
        };
        live.Frame();
        using (var fresh = new Tester(View, new(Width: 150, Height: 80)))
            check(Equal(live, fresh), "font changes invalidate fitting and shaping");
        Save(live, output, "changed");
    }

    static void Rich(string output, Action<bool, string> check)
    {
        using var metrics = new Context();
        var emphasized = Style with
        {
            Weight = Weight.Bold,
            Color = Color.Hex(0x993300)
        };
        var width = metrics.TextSystem.Measure("AB", Style).Width + metrics.TextSystem.Measure("CD…", emphasized).Width + .25f;
        Tester Open(string value, TextOverflow overflow) => new(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8);
            using var box = c.Div(key: "box").Width(width);
            c.RichText(value, [new(2, value.Length, Weight: Weight.Bold, Color: emphasized.Color, Underline: true)],
                Style with
                {
                    Overflow = overflow
                }, key: "text");
        }, new(Width: 360, Height: 100));
        using var actual = Open(Long, TextOverflow.Ellipsis);
        using var expected = Open("ABCD…", TextOverflow.Visible);
        check(Equal(actual, expected), "rich ellipsis preserves span widths, weight, color and decoration");
        Save(actual, output, "rich");
        TextSpan[] spans = [new(0, Long.Length, Weight: Weight.Regular)];
        void Changing(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8);
            using var box = c.Div(key: "box").Width(300);
            c.RichText(Long, spans, new TextStyle(Size: 18) with
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        }
        using var changing = new Tester(Changing, new(Width: 360, Height: 100));
        spans[0] = new(0, Long.Length, Weight: Weight.Bold);
        changing.Frame();
        using var fresh = new Tester(Changing, new(Width: 360, Height: 100));
        check(Equal(changing, fresh) && Find(changing, "text").Rect == Find(fresh, "text").Rect,
            "changing a rich span invalidates both its measured width and its displayed text");
    }

    static void RightToLeft(string output, Action<bool, string> check)
    {
        const string source = "אבגדהוזחטיכלמנסעפצקרשת";
        using var metrics = new Context();
        var width = metrics.TextSystem.Measure("אבג…", Style).Width + .25f;
        Tester Open(string value, TextOverflow overflow) => new(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8)
                .Flow(FlowDirection.RightToLeft).Selectable();
            using var box = c.Div(key: "box").Width(width);
            c.Text(value, Style with
            {
                Overflow = overflow
            }, key: "text");
        }, new(Width: 360, Height: 100));
        using var actual = Open(source, TextOverflow.Ellipsis);
        using var expected = Open("אבג…", TextOverflow.Visible);
        check(Equal(actual, expected), "RTL ellipsis paints at the visual trailing edge");
        var box = actual.FindText(source)!.Value;
        actual.Press(box.Right - .1f, box.Y + box.Height / 2);
        actual.MoveTo(box.X, box.Y + box.Height / 2);
        actual.Release(box.X, box.Y + box.Height / 2);
        actual.Key(Key.C, Modifiers.Primary);
        check(actual.Clipboard == source, "RTL selection maps the marker back to the original source");
        Save(actual, output, "rtl");
    }

    static void MixedDirection(string output, Action<bool, string> check)
    {
        using var metrics = new Context();
        var width = metrics.TextSystem.Measure("…ABC", Style).Width + .25f;
        Tester Open(string value, TextOverflow overflow) => new(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8).Flow(FlowDirection.RightToLeft);
            using var box = c.Div(key: "box").Width(width);
            c.Text(value, Style with
            {
                Overflow = overflow
            }, key: "text");
        }, new(Width: 360, Height: 100));
        using var actual = Open("ABCאבגדהוזחטיכלמנסעפצקרשת", TextOverflow.Ellipsis);
        using var expected = Open("…ABC", TextOverflow.Visible);
        check(Equal(actual, expected), "an RTL label with a Latin prefix keeps its marker at the left visual edge");
        Save(actual, output, "mixed-direction");
    }

    static void WrappedClip(string output, Action<bool, string> check)
    {
        foreach (var rtl in new[] { false, true })
        {
            var text = rtl ? "שלום עולם, זו פסקה ארוכה שעוברת לשורה הבאה בחלון צר." :
                "Wrapped clipping must preserve line breaks and alignment inside its allocated box.";
            Tester Open(TextOverflow overflow) => new(c =>
            {
                using var page = c.Div(key: "page").SizeFull().Background(Color.White).Padding(8)
                    .Flow(rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
                using var box = c.Div(key: "box").Width(180).Items(Align.Stretch).Clip();
                c.Text(text, Style with
                {
                    Wrap = true,
                    Overflow = overflow
                }, key: "text");
            }, new(Width: 360, Height: 240));
            using var actual = Open(TextOverflow.Clip);
            using var expected = Open(TextOverflow.Visible);
            check(Equal(actual, expected), "wrapped clipping retains the existing line layout in " + (rtl ? "RTL" : "LTR"));
            Save(actual, output, "wrapped-" + (rtl ? "rtl" : "ltr"));
        }
    }

    static bool Equal(Tester a, Tester b)
    {
        var actual = a.Render();
        var expected = b.Render();
        if (actual.Bytes.SequenceEqual(expected.Bytes))
            return true;
        var differences = Enumerable.Range(0, actual.Bytes.Length).Where(i => actual.Bytes[i] != expected.Bytes[i]).ToArray();
        Console.WriteLine($"Pixel mismatch: {differences.Length} channels; first at ({differences[0] / 4 % actual.Width}, {differences[0] / 4 / actual.Width}); max {differences.Max(i => Math.Abs(actual.Bytes[i] - expected.Bytes[i]))}");
        return false;
    }
    static Node Find(Tester tester, string key) => tester.Context.Nodes.ToArray().Single(n => n.Key == key);
    static void Save(Tester tester, string output, string name)
    {
        var path = Path.Combine(output, "text-overflow-" + name);
        tester.Screenshot(path + ".png");
        File.WriteAllText(path + ".txt", tester.Dump());
        tester.WriteWarnings(path + ".warnings.json");
    }
}
