using Kumiko.UI;

static class CurrentBranch
{
    public static void Run(Harness h, Repositories fixture)
    {
        const string branch = "zzz-focus/team/current";
        h.OpenRepository(fixture.Repo);
        Repositories.Git(fixture.Repo, "branch", "-m", branch);
        for (var index = 0; index < 60; index++)
            Repositories.Git(fixture.Repo, "branch", $"focus-{index:D2}");
        h.Press(Key.R, Modifiers.Primary);
        Tester.Locator List() => h.Tester.Get(Role.Tree, "References").Get(Role.List, "");
        List().Scroll(5000);
        h.Click("Folder: team");
        h.Click("Folder: zzz-focus");
        List().Scroll(-5000);
        h.Click("Branches");
        h.Type("Filter", "no-matching-branch");
        var page = h.Tab.Page;
        var sha = h.Tab.Sha;
        var file = h.Tab.File;
        var head = Repositories.Git(fixture.Repo, "rev-parse", "HEAD");
        var status = Repositories.Git(fixture.Repo, "status", "--porcelain");
        h.Click("Focus current branch");
        h.Check(h.Has(Role.TreeItem, "Branch: " + branch) && h.Tab.Filter.Length == 0,
            "Focus clears a hiding filter, expands nested folders and scrolls to the checked-out branch");
        h.Check(h.Tab.SideSelected == "branch:" + branch && h.Tab.Page == page && h.Tab.Sha == sha && h.Tab.File == file,
            "Focus highlights the branch without changing the page, commit or file selection");
        h.Type("Filter", "current");
        h.Click("Collapse all");
        CheckCollapsed();
        h.Check(h.Tab.Filter.Length == 0 && h.Tab.Page == page && h.Tab.Sha == sha && h.Tab.File == file
            && h.Tab.SideSelected == "branch:" + branch,
            "Collapse all clears filtering and preserves page, commit, file and branch selection");
        h.Shot("sidebar-collapsed-all");
        h.Press(Key.Enter);
        CheckCollapsed();
        h.Click("Branches");
        List().Scroll(5000);
        h.Check(h.Has(Role.Button, "Folder: zzz-focus") && !h.Has(Role.Button, "Folder: team"),
            "Reopening Branches leaves previously offscreen folders collapsed");
        h.Click("Folder: zzz-focus");
        h.Check(h.Has(Role.Button, "Folder: team") && !h.Has(Role.TreeItem, "Branch: " + branch),
            "Reopening a parent leaves its nested folder collapsed");
        h.Click("Focus current branch");
        h.Check(h.Has(Role.TreeItem, "Branch: " + branch), "Current-branch focus reveals its branch after collapse all");
        h.Shot("current-branch-focused");
        List().Scroll(-5000);
        h.Idle();
        h.Check(!h.Has(Role.TreeItem, "Branch: " + branch), "The sidebar remains freely scrollable after focusing");
        h.Press(Key.Enter);
        h.Check(h.Has(Role.TreeItem, "Branch: " + branch), "Keyboard activation can focus the same branch again");
        h.Check(Repositories.Git(fixture.Repo, "branch", "--show-current").Trim() == branch
            && Repositories.Git(fixture.Repo, "rev-parse", "HEAD") == head
            && Repositories.Git(fixture.Repo, "status", "--porcelain") == status,
            "Focusing preserves the checkout and working changes");
        h.Tester.Resize(1000, 700);
        h.Idle();
        h.Click("Resize sidebar", Role.Separator);
        h.Press(Key.Home);
        var button = h.Tester.Get(Role.Button, "Focus current branch").Rect;
        var filter = h.Tester.Get(Role.TextField, "Filter").Rect;
        var collapse = h.Tester.Get(Role.Button, "Collapse all").Rect;
        h.Check(Math.Abs(h.Tester.Get(Role.Separator, "Resize sidebar").Rect.X - 220) < 1
            && filter.Width > 0 && filter.Right <= button.X && button.Right <= collapse.X
            && collapse.Right <= h.Tester.Get(Role.Separator, "Resize sidebar").Rect.X,
            "The filter, focus and collapse buttons fit the minimum sidebar width");
        h.Shot("current-branch-compact");
        h.Tester.Resize(1440, 900);
        h.Idle();
        for (var index = 0; index < 7; index++)
            h.Press(Key.Right);
        h.OpenRepository(fixture.History);
        h.Click("Focus current branch");
        h.Check(h.Has(Role.TreeItem, "Branch: main") && h.Tab.SideSelected == "branch:main",
            "Focus uses the active repository's checked-out branch");
        h.Click("Close repository: " + fixture.History);
        h.OpenRepository(fixture.Empty);
        h.Check(!h.Tester.Get(Role.Button, "Focus current branch").Enabled,
            "An unborn repository disables focus because no branch row exists");
        h.Click("Close repository: " + fixture.Empty);
        h.OpenRepository(fixture.Repo);
        Repositories.Git(fixture.Repo, "switch", "--detach");
        h.Press(Key.R, Modifiers.Primary);

        void CheckCollapsed()
        {
            var tree = h.Tester.Context.Accessibility.Single(n => n.Role == Role.Tree && n.Name == "References");
            h.Check(!h.Tester.Context.Accessibility.Any(n => n.Role == Role.TreeItem && !n.Offscreen
                && n.Rect.Y >= tree.Rect.Y && n.Rect.Bottom <= tree.Rect.Bottom)
                && h.Has(Role.Button, "Branches") && h.Has(Role.Button, "Remotes")
                && h.Has(Role.Button, "Tags") && h.Has(Role.Button, "Stashes")
                && !h.Tester.Context.Accessibility.Any(n => n.Role == Role.Button && n.Name.StartsWith("Folder: ", StringComparison.Ordinal) && !n.Offscreen),
                "Collapse all closes every sidebar section and folder, including on repeated keyboard activation");
        }
        h.Check(!h.Tester.Get(Role.Button, "Focus current branch").Enabled,
            "Detached HEAD disables current-branch focus");
        h.Shot("current-branch-detached");
        Repositories.Git(fixture.Repo, "switch", branch);
        Repositories.Git(fixture.Repo, "branch", "-m", "main");
        for (var index = 0; index < 60; index++)
            Repositories.Git(fixture.Repo, "branch", "-D", $"focus-{index:D2}");
        h.Press(Key.R, Modifiers.Primary);
        h.Click("Focus current branch");
        h.Click("Folder: feature");
        h.Click("Remotes");
        h.Click("Stashes");
    }
}
