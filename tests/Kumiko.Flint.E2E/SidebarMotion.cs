using System.Globalization;
using Kumiko.UI;

static class SidebarMotion
{
    public static void Run(Harness h, string output)
    {
        var tester = h.Tester;
        var trace = new List<string> { "phase,main_x,subject_width" };
        float MainX() => tester.Context.Nodes.ToArray().Single(n => n.Key == "main").Rect.X;
        float FieldWidth() => tester.Get(Role.TextField, "Commit subject").Rect.Width;
        void Sample(string phase) => trace.Add(string.Create(CultureInfo.InvariantCulture, $"{phase},{MainX():0.###},{FieldWidth():0.###}"));
        h.Type("Commit subject", "Draft survives sidebar motion");
        var file = h.Tab.File;
        var patch = h.Tab.Patch;
        var open = MainX();
        var field = FieldWidth();
        tester.Context.ReduceMotion = false;
        Sample("expanded");
        tester.Get(Role.Button, "Toggle sidebar").Click();
        tester.Frame();
        var closing = MainX();
        h.Check(!h.Model.Sidebar && closing > 0 && closing < open && tester.Context.Animating,
            "Toolbar sidebar toggle eases closed instead of jumping");
        h.Check(Math.Abs(FieldWidth() - field - open + closing) < 1,
            "Composer width follows the animated main pane on each frame");
        Sample("closing-first");
        h.Shot("sidebar-closing");
        tester.Frame();
        h.Check(MainX() > 0 && MainX() < closing, "Successive frames move the sidebar further closed");
        Sample("closing-next");
        var beforeReverse = MainX();
        tester.Get(Role.Button, "Toggle sidebar").Invoke();
        h.Check(h.Model.Sidebar && MainX() > beforeReverse && MainX() < open,
            "Reversing the sidebar toggle continues from its current position");
        Sample("reversed");
        h.Shot("sidebar-reversing");
        tester.Advance(1);
        tester.Settle();
        h.Check(Math.Abs(MainX() - open) < .5f && h.Has(Role.Separator, "Resize sidebar") && !tester.Context.Animating,
            "Sidebar animation settles at the saved width and stops requesting animation frames");
        Sample("expanded-again");
        tester.Get(Role.Button, "Toggle sidebar").Click();
        tester.Advance(1);
        tester.Settle();
        h.Check(MainX() == 0 && !h.Has(Role.Separator, "Resize sidebar") && !h.Has(Role.Button, "Local Changes"),
            "Collapsed sidebar leaves no visible navigation or resize handle");
        Sample("collapsed");
        h.Shot("sidebar-hidden");
        tester.Context.ReduceMotion = true;
        tester.Get(Role.Button, "Toggle sidebar").Click();
        tester.Frame();
        h.Check(Math.Abs(MainX() - open) < .5f && !tester.Context.Animating,
            "Reduced motion expands the sidebar immediately");
        h.Check(h.Tab.File == file && h.Tab.Patch == patch && h.Tab.Subject == "Draft survives sidebar motion",
            "Sidebar animation preserves the selected file, patch and draft");
        Sample("reduced-motion-expanded");
        File.WriteAllLines(Path.Combine(output, "sidebar-animation.csv"), trace);
        h.Type("Commit subject", "");
    }
}
