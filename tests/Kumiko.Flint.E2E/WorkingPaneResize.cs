using Kumiko.UI;

static class WorkingPaneResize
{
    public static void Run(Harness h)
    {
        var tester = h.Tester;
        Tester.Locator WidthHandle() => tester.Get(Role.Separator, "Resize local files");
        Tester.Locator HeightHandle() => tester.Get(Role.Separator, "Resize unstaged files");
        void Drag(Tester.Locator handle, float dx, float dy)
        {
            var rect = handle.Rect;
            var x = rect.X + rect.Width / 2;
            var y = rect.Y + rect.Height / 2;
            tester.Press(x, y);
            tester.MoveTo(x + dx, y + dy);
            tester.Release(x + dx, y + dy);
            h.Idle();
        }
        h.Type("Commit subject", "Draft survives resizing");
        var selected = h.Tab.File;
        var patch = h.Tab.Patch;
        var width = WidthHandle().Rect.X;
        var field = tester.Get(Role.TextField, "Commit subject").Rect.Width;
        Drag(WidthHandle(), 120, 0);
        h.Check(Math.Abs(WidthHandle().Rect.X - width - 120) < 1
            && Math.Abs(tester.Get(Role.TextField, "Commit subject").Rect.Width - field + 120) < 1,
            "Local file divider resizes the trees and composer together");
        var height = HeightHandle().Rect.Y;
        Drag(HeightHandle(), 0, 110);
        h.Check(Math.Abs(HeightHandle().Rect.Y - height - 110) < 1, "Unstaged and Staged sections resize vertically");
        h.Check(h.Tab.File == selected && h.Tab.Patch == patch && h.Tab.Subject == "Draft survives resizing",
            "Resizing preserves selection, diff and commit draft");
        h.Click("Stage File");
        h.Check(h.Tab.Snapshot.Staged.Any(f => f.Path == selected), "Staging works with resized file sections");
        h.Click("Staged: " + selected);
        h.Click("Unstage File");
        h.Click("Unstaged: " + selected);
        h.Click("All Commits");
        h.Click("Local Changes");
        h.Check(Math.Abs(WidthHandle().Rect.X - width - 120) < 1
            && Math.Abs(HeightHandle().Rect.Y - height - 110) < 1,
            "Local file split sizes survive page navigation");
        h.Shot("local-files-resized");
        WidthHandle().Click();
        h.Press(Key.Left);
        h.Check(Math.Abs(WidthHandle().Rect.X - width - 110) < 1, "Keyboard arrows resize the local file column");
        HeightHandle().Click();
        h.Press(Key.Up);
        h.Check(Math.Abs(HeightHandle().Rect.Y - height - 100) < 1, "Keyboard arrows resize the unstaged section");
        Drag(HeightHandle(), 0, -5000);
        h.Check(tester.Get(Role.Button, "Stage All").Rect.Bottom <= HeightHandle().Rect.Y,
            "Dragging up keeps the unstaged header accessible");
        Drag(HeightHandle(), 0, 5000);
        h.Check(tester.Get(Role.Button, "Unstage All").Rect.Bottom <= 900,
            "Dragging down keeps the staged header accessible");
        Drag(WidthHandle(), 5000, 0);
        tester.Resize(1000, 700);
        h.Idle();
        var subject = tester.Get(Role.TextField, "Commit subject").Rect;
        h.Check(subject.Width >= 200 && subject.Right <= 1000
            && tester.Get(Role.Button, "Commit & Push").Rect.Right <= 1000
            && tester.Get(Role.Button, "Unstage All").Rect.Bottom <= 700,
            "A smaller window keeps both sections and composer controls usable");
        h.Shot("local-files-compact");
        tester.Resize(1440, 900);
        h.Idle();
        Drag(WidthHandle(), -5000, 0);
        Drag(WidthHandle(), 160, 0);
        Drag(HeightHandle(), 0, height - HeightHandle().Rect.Y);
        h.Type("Commit subject", "");
        h.Click("Unstaged: " + selected);
    }
}
