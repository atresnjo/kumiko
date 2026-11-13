using Kumiko.Components;
using Kumiko.UI;

/// <summary>Shared layout motion, keyboard-only focus rings and keyboard context menus.</summary>
static class MotionChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        Underline(output, check);
        ReducedMotion(check);
        ContextMenuRequest(check);
    }

    static readonly Choice[] Choices = [new("all", "All companies"), new("mine", "Mine"), new("archived", "Archived accounts")];

    static Tester Tabs(Func<string> selected, Action<string> changed, bool fill = false)
    {
        var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S4);
            new Kit(c).Tabs("Lists", Choices, selected(), changed, new(TabsVariant.Underline, Fill: fill), key: "tabs");
        }, new(Width: 640, Height: 200));
        t.Settle();
        return t;
    }

    static Rect Indicator(Tester t)
    {
        foreach (ref readonly var node in t.Context.Nodes)
            if (node.Key == "indicator")
                return node.Rect;
        throw new InvalidOperationException("The active tab has no indicator.");
    }

    static Rect Tab(Tester t, string name) => t.Context.Accessibility.Single(n => n.Role == Role.Option && n.Name == name).Rect;

    static bool Near(float a, float b) => MathF.Abs(a - b) < .5f;

    static float Center(Rect r) => r.X + r.Width / 2;

    /// <summary>Whether the underline is centered under the tab, within its width, along its bottom edge.</summary>
    static bool Under(Rect line, Rect tab) => Near(Center(line), Center(tab)) && line.Width > 0 && line.Width <= tab.Width &&
        line.Y >= tab.Bottom - 4 && line.Y <= tab.Bottom + 4;

    /// <summary>Whether a focus-ring border is painted around the tab.</summary>
    static bool Ringed(Tester t, string name)
    {
        var tab = Tab(t, name);
        return t.Context.Instances.Any(i => i.BorderWidth > 0 && i.BorderColor.W > 0 && Near(i.Rect.X, tab.X) && Near(i.Rect.Z, tab.Width));
    }

    static void Underline(string output, Action<bool, string> check)
    {
        var selected = "all";
        using var t = Tabs(() => selected, id => selected = id);
        var start = Indicator(t);
        var first = Tab(t, "All companies");
        check(Under(start, first), "the underline starts under the selected tab");
        var target = Tab(t, "Archived accounts");
        t.Click(target.X + target.Width / 2, target.Y + target.Height / 2);
        t.Frame();
        var moving = Indicator(t);
        check(selected == "archived" && moving.X > start.X + 1 && Center(moving) < Center(target) - 1,
            "the underline slides from the previous tab instead of jumping");
        check(!Ringed(t, "Archived accounts"), "a pointer click draws no focus ring");
        t.Screenshot(Path.Combine(output, "tabs-underline-moving.png"));
        t.Advance(.6f);
        t.Settle();
        var settled = Indicator(t);
        check(Under(settled, target), "the underline settles under the new tab");
        check(moving.Width > start.Width + .5f && moving.Width < settled.Width - .5f, "the underline width blends between the two tabs");
        t.Screenshot(Path.Combine(output, "tabs-underline.png"));

        t.Key(Key.Left);
        t.Settle();
        check(selected == "mine" && Ringed(t, "Mine"), "keyboard focus shows the focus ring");

        using var filled = Tabs(() => selected, id => selected = id, fill: true);
        filled.Resize(900, 200);
        filled.Frame();
        check(Under(Indicator(filled), Tab(filled, "Mine")), "resizing the window moves the underline without lag");
    }

    static void ReducedMotion(Action<bool, string> check)
    {
        var selected = "all";
        using var t = Tabs(() => selected, id => selected = id);
        t.Context.ReduceMotion = true;
        var target = Tab(t, "Mine");
        t.Click(target.X + target.Width / 2, target.Y + target.Height / 2);
        check(Under(Indicator(t), target), "Reduce Motion moves the underline at once");
    }

    static void ContextMenuRequest(Action<bool, string> check)
    {
        Point? opened = null;
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S4);
            using var row = c.Div(key: "row").Width(200).Height(30).Semantics(Role.Row, "Row");
            if (c.MenuRequested(row.Control()) is { } at)
                opened = at;
        }, new(Width: 400, Height: 200));
        t.Settle();
        var rect = t.Context.Accessibility.Single(n => n.Role == Role.Row).Rect;
        t.Click(rect.X + 10, rect.Y + 10);
        check(opened is null, "a primary click requests no menu");
        t.RightClick(rect.X + 40, rect.Y + 12);
        check(opened is { X: var x, Y: var y } && Near(x, rect.X + 40) && Near(y, rect.Y + 12), "a secondary press requests a menu at the pointer");
        opened = null;
        t.Key(Key.F10, Modifiers.Shift);
        check(opened is { } below && Near(below.Y, rect.Bottom), "Shift-F10 requests a menu below the focused control");
    }
}
