using Kumiko.Components;
using Kumiko.Rendering;
using Kumiko.UI;

/// <summary>Enter and exit transitions, and the drawer and dialog that use them.</summary>
static class TransitionChecks
{
    static readonly Color Marker = Color.Hex(0xff0000);

    public static void Run(string output, Action<bool, string> check)
    {
        EnterAndExit(check);
        ReducedMotion(check);
        Drawer(output, check);
        DialogPop(check);
    }

    static Instance? Find(Tester t, Color color)
    {
        foreach (var i in t.Context.Instances)
            if (MathF.Abs(i.Color.X - color.R) < .01f && MathF.Abs(i.Color.Y - color.G) < .01f && MathF.Abs(i.Color.Z - color.B) < .01f &&
                i.Rect.Z > 20)
                return i;
        return null;
    }

    static Tester Panel(Func<bool> show)
    {
        var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(40);
            if (!show())
                return;
            using var panel = c.Div(key: "panel").Width(200).Height(120).Background(Marker).Enter(Transition.Rise).Exit(Transition.Fade);
            c.Text("Panel", key: "label");
        }, new(Width: 400, Height: 300));
        t.Settle();
        return t;
    }

    static void EnterAndExit(Action<bool, string> check)
    {
        var show = false;
        using var t = Panel(() => show);
        show = true;
        t.Frame(0);
        check(Find(t, Marker) is { Color.W: < .01f }, "an entering node starts from its transition state");
        t.Frame(.05f);
        check(Find(t, Marker) is { } early && early.Color.W is > .05f and < .99f && early.Rect.Y > 40.5f,
            "an entering node fades in and rises toward its place");
        t.Advance(.5f);
        check(Find(t, Marker) is { } shown && shown.Color.W > .99f && MathF.Abs(shown.Rect.Y - 40) < .01f, "an entered node rests in its layout");
        check(t.Context.Accessibility.Any(n => n.Name == "Panel"), "transitions leave semantics alone");

        show = false;
        t.Frame(.02f);
        check(Find(t, Marker) is { } leaving && leaving.Color.W is > .05f and < 1 && !t.Context.Accessibility.Any(n => n.Name == "Panel"),
            "a removed node fades out from its last frame and leaves the semantic tree at once");
        t.Advance(.5f);
        t.Frame();
        check(Find(t, Marker) is null, "a removed node disappears once its exit completes");
    }

    static void ReducedMotion(Action<bool, string> check)
    {
        var show = false;
        using var t = Panel(() => show);
        t.Context.ReduceMotion = true;
        show = true;
        t.Frame(0);
        check(Find(t, Marker) is { } shown && shown.Color.W > .99f, "Reduce Motion shows an entering node at once");
        show = false;
        t.Frame(0);
        check(Find(t, Marker) is null, "Reduce Motion removes a node at once");
    }

    static void Drawer(string output, Action<bool, string> check)
    {
        var open = false;
        var closed = 0;
        var panel = Color.Hex(0x00ff00);
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S3);
            if (c.Button("Open details", key: "open").Clicked)
                open = true;
            if (!open)
                return;
            using var drawer = new Kit(c).Drawer("Partner billing", () =>
            {
                open = false;
                closed++;
            }, new(Description: "Shopify, shopify.com", Size: 360), key: "drawer");
            using (c.Div(key: "marker").Width(40).Height(40).Background(panel))
            {
            }
            c.Text("$380,000", new(Size: 24), key: "value");
            for (var i = 0; i < 30; i++)
                c.Text($"Activity {i}", key: i);
            if (c.Button("Email Alex", key: "email").Clicked)
                closed += 100;
        }, new(Width: 900, Height: 600, DevMode: true));
        t.Settle();
        t.ClickText("Open details");
        t.Frame(.03f);
        var dialog = t.Context.Accessibility.Single(n => n.Role == Role.Dialog && n.Name == "Partner billing");
        check(dialog.Rect.Right > 890 && dialog.Rect.Width is > 359 and < 361, "the drawer docks to the right edge at its width");
        check(Find(t, panel) is { } sliding && sliding.Rect.X > dialog.Rect.X + 30, "the drawer slides in from the right");
        t.Advance(.6f);
        t.Settle();
        check(Find(t, panel) is { } docked && docked.Rect.X < dialog.Rect.X + 60, "the drawer settles at its edge");
        check(t.Context.Accessibility.Any(n => n.Focused && n.Rect.X >= dialog.Rect.X), "focus moves into the drawer");
        for (var i = 0; i < 6; i++)
            t.Key(Key.Tab);
        check(t.Context.Accessibility.Any(n => n.Focused && n.Rect.X >= dialog.Rect.X), "Tab stays inside the drawer");
        check(t.HasText("Shopify, shopify.com") && t.Context.Accessibility.Any(n => n.Role == Role.Button && n.Name == "Close"),
            "the drawer shows its description and a close button");
        check(t.Context.Accessibility.Any(n => n.Role == Role.ScrollArea && n.Rect.X >= dialog.Rect.X), "the drawer body scrolls");
        check(t.Context.Warnings.Count == 0, "the drawer renders without layout, overlap or contrast warnings: " +
            string.Join("; ", t.Context.Warnings.Select(w => w.Message)));
        t.Screenshot(Path.Combine(output, "drawer.png"));
        File.WriteAllText(Path.Combine(output, "drawer.txt"), t.Dump());
        var restingX = Find(t, panel)!.Value.Rect.X;

        t.Key(Key.Escape);
        check(closed == 1 && !open, "Escape closes the drawer");
        t.Frame(.04f);
        check(Find(t, panel) is { } leaving && leaving.Rect.X > restingX + 5, "a closed drawer slides back out to the right");
        t.Advance(.6f);
        t.Frame();
        check(Find(t, panel) is null, "the drawer is gone once it has slid out");
    }

    static void DialogPop(Action<bool, string> check)
    {
        var open = false;
        var panel = Color.Hex(0x0000ff);
        using var t = new Tester(c =>
        {
            using var page = c.Div(key: "page").SizeFull();
            if (!open)
                return;
            using var dialog = new Kit(c).Dialog("Archive", () => open = false, key: "dialog");
            using (c.Div(key: "marker").Width(200).Height(40).Background(panel))
            {
            }
        }, new(Width: 700, Height: 500));
        t.Settle();
        open = true;
        t.Frame(0);
        t.Frame(.03f);
        var early = Find(t, panel);
        t.Advance(.5f);
        t.Settle();
        var settled = Find(t, panel);
        check(early is { } e && settled is { } s && e.Rect.Z < s.Rect.Z - .5f && e.Color.W < s.Color.W, "a dialog pops in, growing and fading in");
    }
}
