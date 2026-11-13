using CRMLite;
using Kumiko.UI;

/// <summary>The company list tabs: one underline that glides between tabs and no focus box after a click.</summary>
static class TabFlows
{
    public static void Run(CrmSession s, string output, Action<bool, string> check)
    {
        s.Tester.Context.ReduceMotion = false;
        var all = s.Node(Role.Option, "All companies").Rect;
        var hot = s.Node(Role.Option, "Hot").Rect;
        check(Underline(s) is { } start && Near(start.X, all.X), "the underline starts under All companies");
        s.Tester.Click(hot.X + hot.Width / 2, hot.Y + hot.Height / 2);
        s.Tester.Frame();
        check(s.Model.List == CompanyList.Hot && Underline(s) is { } moving && moving.X > all.X + 1 && moving.X < hot.X - 1,
            "the underline glides toward the chosen tab");
        s.Tester.Screenshot(Path.Combine(output, "tabs-moving.png"));
        s.Tester.Advance(.6f);
        s.Tester.Settle();
        hot = s.Node(Role.Option, "Hot").Rect;
        check(Underline(s) is { } settled && Near(settled.X, hot.X) && Near(settled.Width, hot.Width), "the underline settles under Hot");
        var focusBox = s.Tester.Context.Instances.Any(i => i.BorderWidth > 0 && i.BorderColor.W > 0 && i.Rect.Y >= hot.Y - 1 &&
            i.Rect.Y + i.Rect.W <= hot.Bottom + 1 && i.Rect.X >= hot.X - 1 && i.Rect.X + i.Rect.Z <= hot.Right + 1);
        check(!focusBox, "a clicked tab shows no focus box");

        var from = Selection(s);
        var deals = s.Node(Role.Button, "Deals").Rect;
        s.Tester.Click(deals.X + 20, deals.Y + deals.Height / 2);
        s.Tester.Frame();
        check(Selection(s) is { } gliding && from is { } before && MathF.Abs(gliding.Y - deals.Y) > 1 && MathF.Abs(gliding.Y - before.Y) > 1 && (gliding.Y - before.Y) * (deals.Y - gliding.Y) > 0,
            "the sidebar selection glides to the clicked item");
        s.Tester.Advance(.6f);
        s.Tester.Settle();
        check(Selection(s) is { } landed && Near(landed.Y, deals.Y) && Near(landed.Height, deals.Height), "the sidebar selection lands on Deals");
        s.Tester.Context.ReduceMotion = true;
    }

    static Rect? Underline(CrmSession s)
    {
        foreach (ref readonly var node in s.Tester.Context.Nodes)
            if (node.Key == "rule" && node.Style.Background is { A: > 0 } && node.Rect.Height == 2)
                return node.Rect;
        return null;
    }

    static Rect? Selection(CrmSession s)
    {
        foreach (ref readonly var node in s.Tester.Context.Nodes)
            if (node.Key == "selection")
                return node.Rect;
        return null;
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < .5f;
}
