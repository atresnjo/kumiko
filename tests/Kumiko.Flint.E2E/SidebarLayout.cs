using Kumiko.UI;

static class SidebarLayout
{
    public static void Run(Harness h, Repositories fixture)
    {
        var tester = h.Tester;
        Tester.Locator Handle() => tester.Get(Role.Separator, "Resize sidebar");
        Rect List(string key) => tester.Context.Accessibility.First(n => n.Role == Role.List && n.Key == key).Rect;
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
        var sidebar = Handle().Rect.X;
        h.Check(Math.Abs(tester.Get(Role.Button, "All Commits").Rect.Right - sidebar + 6) < 1,
            "Selected sidebar navigation fills the available row");
        h.Check(Math.Abs(List("refs").Right - sidebar) < 1, "Sidebar scrollbar viewport reaches the pane edge");
        h.Check(Math.Abs(List("commit-files").Right - tester.Get(Role.Separator, "Resize commit files").Rect.X) < 1,
            "Commit file scrollbar viewport reaches its divider");
        Drag(120);
        h.Check(Math.Abs(Handle().Rect.X - sidebar - 120) < 1
            && Math.Abs(tester.Get(Role.Button, "All Commits").Rect.Right - sidebar - 114) < 1,
            "Dragging the sidebar resizes navigation with it");
        Handle().Click();
        h.Press(Key.Left);
        h.Check(Math.Abs(Handle().Rect.X - sidebar - 110) < 1, "Keyboard arrows resize the sidebar");
        var longName = "worktree-with-a-very-long-name-and-Unicode-👩‍💻-ending";
        var longBranch = "feature/a-very-long-branch-name-for-the-sidebar";
        var longPath = Path.Combine(fixture.Root, longName);
        Repositories.Git(fixture.Repo, "worktree", "add", "--no-checkout", "-b", longBranch, longPath);
        for (var index = 0; index < 30; index++)
            Repositories.Git(fixture.Repo, "worktree", "add", "--no-checkout", "--detach", Path.Combine(fixture.Root, $"z-sidebar-{index:D2}"));
        h.Press(Key.R, Modifiers.Primary);
        var row = tester.Get(Role.TreeItem, "Worktree: " + longName);
        var texts = tester.Context.Nodes.ToArray().Where(n => n.Kind == ContentKind.Text && n.Rect.Y >= row.Rect.Y && n.Rect.Bottom <= row.Rect.Bottom).ToArray();
        h.Check(texts.Any(n => n.Text == longName && tester.Context.TextSystem.Measure(longName, n.TextStyle).Width > n.Rect.Width),
            "Long worktree names retain their complete source and accessible name inside a constrained text box");
        h.Check(texts.All(n => n.Rect.Right <= Handle().Rect.X), "Worktree name and branch stay inside the sidebar");
        h.Shot("sidebar-resized");
        var restoredWidth = Handle().Rect.X;
        Drag(-5000);
        CheckLabels(true, "sidebar-ellipsis-narrow");
        Drag(5000);
        CheckLabels(false, "sidebar-ellipsis-wide");
        Drag(restoredWidth - Handle().Rect.X);
        CheckLabels(true, "sidebar-ellipsis-restored");
        tester.Get(Role.Tree, "References").Get(Role.List, "").Scroll(5000);
        h.Check(h.Has(Role.TreeItem, "Worktree: z-sidebar-29"), "Sidebar scrollbar reaches the final worktree");
        h.Shot("sidebar-scrolled");
        Drag(5000);
        tester.Resize(1000, 700);
        h.Idle();
        h.Check(Handle().Rect.X <= 300 && tester.Get(Role.Button, "Find").Rect.Right <= 1000,
            "Window resizing constrains the sidebar and keeps the diff visible");
        h.Shot("sidebar-compact");
        tester.Resize(1440, 900);
        h.Idle();
        Drag(-5000);
        Drag(70);
        foreach (var path in Directory.EnumerateDirectories(fixture.Root).Where(p => p == longPath || Path.GetFileName(p).StartsWith("z-sidebar-", StringComparison.Ordinal)))
            Repositories.Git(fixture.Repo, "worktree", "remove", "--force", path);
        h.Press(Key.R, Modifiers.Primary);

        void CheckLabels(bool truncated, string snapshot)
        {
            var worktree = tester.Get(Role.TreeItem, "Worktree: " + longName);
            var label = worktree.Get(Role.Text, longName).Rect;
            var badge = worktree.Get(Role.Text, longBranch).Rect;
            var nodes = tester.Context.Nodes.ToArray();
            var nameNode = nodes.Single(n => n.Text == longName);
            var branchNode = nodes.Single(n => n.Key == "badge" && n.Text == longBranch);
            var nameFits = tester.Context.TextSystem.Measure(longName, nameNode.TextStyle).Width <= label.Width;
            var branchFits = tester.Context.TextSystem.Measure(longBranch, branchNode.TextStyle).Width <= badge.Width;
            h.Check(nameFits != truncated && branchFits != truncated,
                snapshot + (truncated ? ": both complete labels exceed their allocated boxes" : ": both complete labels fit after widening"));
            h.Check(label.Width > 0 && badge.Width > 0 && label.Right + 5.9f <= badge.X && badge.Right <= Handle().Rect.X,
                snapshot + ": worktree name and branch remain separate and inside the sidebar");
            h.Check(!tester.Context.Warnings.Any(w => w.Code is "overflow" or "text-overflow"),
                snapshot + ": intentional truncation produces no overflow warnings");
            h.Shot(snapshot);
        }
    }
}
