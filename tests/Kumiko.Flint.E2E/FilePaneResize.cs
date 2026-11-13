using Kumiko.UI;

static class FilePaneResize
{
    public static void Run(Harness h)
    {
        var tester = h.Tester;
        Tester.Locator Handle() => tester.Get(Role.Separator, "Resize commit files");
        float DiffStart() => tester.Get(Role.Text, "feature line").Rect.X;
        void Drag(float distance)
        {
            var rect = Handle().Rect;
            var x = rect.X + rect.Width / 2;
            var y = rect.Y + rect.Height / 2;
            tester.Press(x, y);
            tester.MoveTo(x + distance, y);
            tester.Release(x + distance, y);
            h.Idle();
        }
        var initial = Handle().Rect.X;
        var diff = DiffStart();
        var patch = h.Tab.Patch;
        Drag(180);
        h.Check(Math.Abs(Handle().Rect.X - initial - 180) < 1 && Math.Abs(DiffStart() - diff - 180) < 1,
            "Dragging the commit-file divider widens the list and moves the diff together");
        h.Check(h.Tab.Patch == patch, "Resizing leaves the displayed patch intact");
        h.Click("File: feature.txt");
        h.Check(Math.Abs(Handle().Rect.X - initial - 180) < 1 && h.Tab.File == "feature.txt",
            "Selecting a commit file preserves the resized list");
        h.Click("Commit");
        h.Click("Changes");
        h.Check(Math.Abs(Handle().Rect.X - initial - 180) < 1, "Commit and Changes tabs retain the file-pane width");
        h.Shot("commit-files-resized");
        Handle().Click();
        h.Press(Key.Right);
        h.Check(Math.Abs(Handle().Rect.X - initial - 190) < 1, "Keyboard arrows resize the focused file divider");
        Drag(-5000);
        var minimum = Handle().Rect.X;
        Drag(-100);
        h.Check(Math.Abs(Handle().Rect.X - minimum) < 1 && tester.Get(Role.Button, "File: feature.txt").Rect.Width >= 160,
            "Dragging left keeps a usable file list");
        Drag(5000);
        h.Check(tester.Get(Role.Button, "Find").Rect.Right <= 1440, "Dragging right keeps the diff inside the window");
        tester.Resize(1000, 700);
        h.Idle();
        h.Check(Handle().Rect.X <= 760 && tester.Get(Role.Button, "Find").Rect.Right <= 1000,
            "Narrowing the window constrains a previously widened file pane");
        h.Shot("commit-files-compact");
        tester.Resize(1440, 900);
        h.Idle();
        h.Click("All files");
        Drag(-5000);
        Drag(120);
    }
}
