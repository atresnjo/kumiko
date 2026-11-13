using Kumiko.UI;

/// <summary>Eased scrolling: wheel notches glide, Reveal scrolls to an element once per request, input interrupts.</summary>
static class ScrollChecks
{
    public static void Run(Action<bool, string> check)
    {
        long request = 0;
        var target = "s3";
        ulong viewport = 0;
        void View(Context c)
        {
            using var page = c.Div(key: "page").SizeFull().Padding(10);
            using var list = c.Div(key: "list").FillWidth().Height(300).Scroll().Padding(new Edges(20, 0, 0, 0)).Gap(10);
            viewport = list.Id;
            foreach (var id in new[] { "s1", "s2", "s3", "s4", "s5", "s6" })
            {
                using var section = c.Div(key: id).FillWidth().Height(260).Background(c.Theme.Surface);
                if (request > 0 && id == target)
                    section.Reveal(request, 8);
                c.Text(id, key: "title");
            }
        }
        using var t = new Tester(View, new(400, 400));
        t.Frame();
        float Offset() => t.Context.Scrolls[viewport].Offset;

        t.MoveTo(200, 200);
        t.Wheel(120);
        var first = Offset();
        check(first > 0 && first < 120, $"a wheel notch starts moving without jumping ({first:0.#} of 120 px)");
        for (var i = 0; i < 20; i++)
            t.Frame(1 / 60f);
        check(MathF.Abs(Offset() - 120) < .01f, "the wheel glide settles exactly on the notch distance");
        t.Wheel(120);
        t.Wheel(120);
        for (var i = 0; i < 20; i++)
            t.Frame(1 / 60f);
        check(MathF.Abs(Offset() - 360) < .01f, "notches during a glide extend it instead of restarting from the current offset");
        t.Scroll(-40);
        check(MathF.Abs(Offset() - 320) < .01f, "trackpad deltas stay one to one");

        request = 1;
        t.Frame(1 / 60f);
        var expected = 2 * (260 + 10) - 8;
        var sections = new List<float>();
        for (var i = 0; i < 60; i++)
        {
            t.Frame(1 / 60f);
            sections.Add(Offset());
        }
        check(MathF.Abs(Offset() - expected) < .01f, $"Reveal lands the element at its margin below the viewport top ({Offset():0.#} vs {expected})");
        check(sections.Zip(sections.Skip(1)).All(p => p.Second >= p.First - .001f) && sections[0] > 320 && sections[0] < expected,
            "Reveal eases monotonically from where the user was");
        t.Scroll(30);
        for (var i = 0; i < 10; i++)
            t.Frame(1 / 60f);
        check(MathF.Abs(Offset() - (expected + 30)) < .01f, "the same request does not scroll again after the user moves");

        target = "s1";
        request = 2;
        t.Frame(1 / 60f);
        t.Frame(1 / 60f);
        t.Scroll(5);
        var interrupted = Offset();
        for (var i = 0; i < 30; i++)
            t.Frame(1 / 60f);
        check(MathF.Abs(Offset() - interrupted) < .01f && interrupted > 100, "trackpad input interrupts a reveal in flight");

        t.Context.ReduceMotion = true;
        request = 3;
        t.Frame(1 / 60f);
        t.Frame(1 / 60f);
        check(Offset() == 0, "ReduceMotion makes a reveal jump, clamped to the top of the range");
        Scrollbar(check);
    }

    /// <summary>
    /// A 300x200 scroller over 2,000 px of clickable rows: a 20 px thumb travels 180 px for 1,800 px of offset.
    /// Pressing the bar must scroll, never click the row beneath it.
    /// </summary>
    static void Scrollbar(Action<bool, string> check)
    {
        ulong viewport = 0;
        var clicks = 0;
        void View(Context c)
        {
            using var list = c.Div(key: "bar-list").Width(300).Height(200).Scroll();
            viewport = list.Id;
            for (var i = 0; i < 20; i++)
            {
                using var row = c.Div(key: i).FillWidth().Height(100);
                if (row.Control().Clicked)
                    clicks++;
            }
        }
        using var t = new Tester(View, new(400, 400));
        t.Frame();
        float Offset() => t.Context.Scrolls[viewport].Offset;

        t.Press(296, 10);
        t.MoveTo(296, 55);
        check(MathF.Abs(Offset() - 450) < .5f, $"dragging the thumb scrolls in proportion ({Offset():0.#} of 450 px)");
        t.MoveTo(296, 100);
        t.Release(296, 100);
        check(MathF.Abs(Offset() - 900) < .5f && clicks == 0, "the thumb follows the pointer until release and never clicks the row beneath");
        t.MoveTo(296, 400);
        check(MathF.Abs(Offset() - 900) < .5f, "the thumb stops following after release");

        t.Press(296, 100);
        t.MoveTo(296, 250);
        t.Release(296, 250);
        check(MathF.Abs(Offset() - 1800) < .5f, "dragging keeps the grab point and clamps at the end");

        t.Press(296, 40);
        t.Release(296, 40);
        check(MathF.Abs(Offset() - 300) < .5f && clicks == 0, $"pressing the track centers the thumb there ({Offset():0.#} of 300 px)");

        t.Press(150, 100);
        t.Release(150, 100);
        check(clicks == 1, "presses beside the bar still reach the content");
    }
}
