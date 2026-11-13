using Kumiko.UI;

/// <summary>
/// Variable-height virtual lists: only the viewport plus overscan is built, rows sit at their offsets, reveals run
/// once per request, sticky headers follow their section and the horizontal offset is shared by every row.
/// </summary>
internal static class VirtualListChecks
{
    const int Rows = 10_000;
    const float Width = 400;
    const float Height = 400;

    internal static void Run(Action<bool, string> check)
    {
        // Every 100th row is a 40 px section header; the rest are 20 px lines.
        var offsets = new float[Rows + 1];
        for (int i = 0; i < Rows; i++)
            offsets[i + 1] = offsets[i] + (i % 100 == 0 ? 40 : 20);
        VirtualReveal? reveal = null;
        VirtualListView view = default;
        float content = 0;
        var built = new List<int>();
        var scrolls = new List<float>();
        using var t = new Tester(c =>
        {
            built.Clear();
            scrolls.Clear();
            view = c.VirtualList("list", offsets, static i => (ulong)i + 1, (ctx, row) =>
            {
                built.Add(row.Index);
                scrolls.Add(row.ScrollX);
                ctx.Text(row.Index % 100 == 0 ? $"Section {row.Index / 100}" : $"Row {row.Index}", key: "text");
            }, new()
            {
                Overscan = 3,
                StickyHeader = static i => i / 100 * 100,
                ContentWidth = content,
                Reveal = reveal
            });
        }, new(Width, Height));
        t.Settle();
        check(view.First == 0 && built.Min() == 0 && built.Count <= 21 + 3 + 1, $"a virtual list builds only visible rows and overscan ({built.Count} built)");
        check(t.FindText("Row 1") is { } first && Math.Abs(first.Y - 40) < 1, "rows start at their own offsets");

        t.MoveTo(100, 100);
        t.Scroll(5000);
        t.Settle();
        int expected = Array.FindIndex(offsets, o => o > 5000) - 1;
        check(view.First == expected && Math.Abs(view.Offset - 5000) < .5f, $"the first visible row follows the scroll offset (row {view.First}, expected {expected})");
        check(built.Count <= 21 + 6 + 1 && built.Contains(expected) && !built.Contains(0), "scrolling replaces rows instead of accumulating them");
        var section = expected / 100;
        check(t.FindText($"Section {section}") is { } pinned && pinned.Y < 20, $"the section header is pinned while its rows scroll (section {section})");

        reveal = new(9000, 1);
        t.Settle();
        check(view.First <= 9000 && view.Last > 9000, "a nearest reveal scrolls the row into view");
        var revealed = view.Offset;
        t.Scroll(-300);
        t.Settle();
        check(Math.Abs(view.Offset - (revealed - 300)) < .5f, "a handled reveal request does not pull the list back");

        reveal = new(500, 2, VirtualRevealMode.Top, 20);
        t.Advance(.1f);
        check(Math.Abs(view.Offset - (offsets[500] - 20)) > 1, "a top reveal glides instead of jumping");
        for (int i = 0; i < 60; i++)
            t.Advance(1 / 60f);
        check(Math.Abs(view.Offset - (offsets[500] - 20)) < .5f, $"a top reveal settles with the row at its margin ({view.Offset})");

        content = 2000;
        t.Settle();
        t.Context.Input.ScrollX = 150;
        t.Frame();
        t.Settle();
        check(Math.Abs(view.ScrollX - 150) < .5f && scrolls.All(x => Math.Abs(x - 150) < .5f), "horizontal scrolling reaches every row");
        var before = view.Offset;
        t.Context.Input.ScrollX = 2;
        t.Scroll(60);
        t.Settle();
        check(Math.Abs(view.ScrollX - 150) < .5f && view.Offset > before, "a mostly vertical swipe moves only vertically");
        t.Context.Input.ScrollX = 5000;
        t.Frame();
        t.Settle();
        check(Math.Abs(view.ScrollX - (2000 - Width)) < .5f, "the horizontal offset stops at the content width");
    }
}
