using System.Collections.Immutable;
using Flint;
using Flint.Services;
using Kumiko.Runtime;
using Kumiko.UI;

/// <summary>Behaviors of the original Flint that the first port lacked, exercised through real input and real Git.</summary>
static class Parity
{
    public static void Run(Harness h, Repositories fixture, RecordingLauncher launcher)
    {
        string Git(params string[] args) => Repositories.Git(fixture.Push, args).Trim();
        h.OpenRepository(fixture.Push);
        h.Click("Local Changes");
        h.Click("Unstaged: picks/a.txt");
        h.ClickWith("Unstaged: picks/c.txt", Harness.Primary);
        h.Check(h.Tab.Selected.SequenceEqual(["picks/a.txt", "picks/c.txt"]), "A real Command/Control click adds a file to the selection");
        h.ClickWith("Unstaged: picks/d.txt", Modifiers.Shift);
        h.Check(h.Tab.Selected.SequenceEqual(["picks/c.txt", "picks/d.txt"]), "Shift+click selects a range from the last clicked anchor");
        h.ClickWith("Unstaged: picks/b.txt", Modifiers.Shift);
        h.Check(h.Tab.Selected.SequenceEqual(["picks/b.txt", "picks/c.txt"]), "A second Shift+click keeps the fixed anchor");
        h.Click("Unstaged: picks/a.txt");
        h.Press(Key.Down, Modifiers.Shift);
        h.Check(h.Tab.Selected.SequenceEqual(["picks/a.txt", "picks/b.txt"]), "Shift+Down extends the selection from the anchor");
        h.Press(Key.Home);
        h.Check(h.Tab.Cursor == "picks/", "Home moves to the first row of the section");
        h.Press(Key.Left);
        h.Check(h.Tab.Folders.Contains("unstaged:picks") && !h.Has(Role.Button, "Unstaged: picks/a.txt"), "Left collapses the focused folder");
        h.Press(Key.Right);
        h.Check(h.Has(Role.Button, "Unstaged: picks/a.txt"), "Right expands it again");
        h.Press(Key.End);
        h.Check(h.Tab.Cursor == "picks/d.txt", "End moves to the last row");
        h.Click("Unstaged: picks/a.txt");
        h.ClickWith("Unstaged: picks/b.txt", Modifiers.Shift);
        h.Tester.TypeText("s");
        h.Idle();
        h.Check(h.Tab.Snapshot.Stashes.Length == 1 && h.Tab.Snapshot.Stashes[0].Message == "Flint: 2 files"
            && !h.Tab.Snapshot.Unstaged.Any(f => f.Path is "picks/a.txt" or "picks/b.txt"), "Plain S stashes only the selected files");
        h.Click("Unstaged: picks/c.txt");
        h.Press(Key.Space);
        h.Check(h.Tab.Snapshot.Staged.Any(f => f.Path == "picks/c.txt"), "Space stages the focused selection");
        h.Type("Commit subject", "Push through upstream");
        h.Click("Commit & Push");
        h.Check(Repositories.Git(fixture.Upstream, "rev-parse", "main").Trim() == Git("rev-parse", "HEAD"), "Commit & Push pushes to the configured upstream remote");
        h.Check(!Repositories.Git(fixture.Origin, "for-each-ref", "--format=%(refname)").Contains("refs/heads/main", StringComparison.Ordinal),
            "A tracked branch is not pushed to origin");
        h.Click("New Branch");
        h.Type("Branch name", "feature/first-push");
        h.Press(Key.Enter);
        h.Click("Push");
        h.Check(Repositories.Git(fixture.Origin, "rev-parse", "feature/first-push").Trim() == Git("rev-parse", "HEAD")
            && Git("rev-parse", "--abbrev-ref", "feature/first-push@{u}") == "origin/feature/first-push", "A first push sets origin as upstream");
        Git("config", "core.hooksPath", fixture.Hooks);
        File.WriteAllText(Path.Combine(fixture.Push, "hooked.txt"), "hooked\n");
        h.Press(Key.R, Modifiers.Primary);
        h.Click("Stage All");
        h.Type("Commit subject", "Hooked commit");
        var head = Git("rev-parse", "HEAD");
        h.Click("Commit & Push");
        h.Check(h.Model.Pending?.Title == "Commit blocked by a git hook" && h.Model.Pending.Details.Contains("hook line 1", StringComparison.Ordinal)
            && h.Model.Pending.Details.Contains("hook line final", StringComparison.Ordinal), "A failing hook opens a dialog with its complete output");
        h.Check(Git("rev-parse", "HEAD") == head && Repositories.Git(fixture.Origin, "rev-parse", "feature/first-push").Trim() == head,
            "Commit & Push does not push after a failed commit");
        h.Shot("hook-dialog");
        h.Click("Copy Output");
        h.Check(h.Tester.Clipboard.Contains("hook line final", StringComparison.Ordinal), "Hook output can be copied");
        h.Click("Commit Without Hooks");
        h.Check(Git("log", "-1", "--format=%s") == "Hooked commit", "The explicit retry commits without hooks");
        h.Check(Repositories.Git(fixture.Origin, "rev-parse", "feature/first-push").Trim() == Git("rev-parse", "HEAD"), "The push follows only after the commit landed");
        File.WriteAllText(Path.Combine(fixture.Push, "hooked-again.txt"), "again\n");
        h.Press(Key.R, Modifiers.Primary);
        h.Click("Stage All");
        h.Type("Commit subject", "Hooks still run");
        h.Click(h.CommitLabel);
        h.Check(h.Model.Pending?.Title == "Commit blocked by a git hook" && Git("log", "-1", "--format=%s") == "Hooked commit", "Skipping hooks applies to one retry only");
        h.Click("Close");
        Git("config", "core.hooksPath", Path.Combine(fixture.Push, "no-hooks"));
        h.Click("Unstage All");
        File.Delete(Path.Combine(fixture.Push, "hooked-again.txt"));
        h.Press(Key.R, Modifiers.Primary);
        h.Check(h.Has(Role.Button, "Folder: feature") && h.Has(Role.TreeItem, "Branch: feature/first-push"), "Slash-delimited branches appear in folders");
        h.DoubleClick("Remote branch: upstream/main", Role.TreeItem);
        h.Check(Git("branch", "--show-current") == "main" && h.Model.Error.Length == 0, "Checking out a remote branch reuses the existing local branch");
        h.DoubleClick("Remote branch: origin/remote-only", Role.TreeItem);
        h.Check(Git("rev-parse", "--abbrev-ref", "remote-only@{u}") == "origin/remote-only", "A remote branch without a local branch is checked out with tracking");
        h.DoubleClick("Branch: feature/first-push", Role.TreeItem);
        h.Check(Git("branch", "--show-current") == "feature/first-push", "Double-clicking a branch checks it out");
        h.Click("Branch: main", Role.TreeItem);
        h.Check(h.Tab.Sha == Git("rev-parse", "main") && Git("branch", "--show-current") == "feature/first-push", "A single click selects the branch's commit without checkout");
        h.Type("Filter", "first");
        h.Check(h.Has(Role.TreeItem, "Branch: feature/first-push") && !h.Has(Role.TreeItem, "Branch: main"), "The sidebar filter keeps matching branches only");
        h.Type("Filter", "");
        Directory.CreateDirectory(Path.Combine(fixture.Push, "bulk"));
        for (var i = 0; i < 400; i++)
            File.WriteAllText(Path.Combine(fixture.Push, "bulk", $"file-{i:D3}.txt"), "bulk\n");
        h.Press(Key.R, Modifiers.Primary);
        h.Click("Local Changes");
        var built = h.Tester.Context.Accessibility.Count(n => n.Name.StartsWith("Unstaged: bulk/", StringComparison.Ordinal));
        h.Check(built is > 0 and < 80 && !h.Has(Role.Button, "Unstaged: bulk/file-399.txt"), "Only visible file rows are built for long change lists");
        h.Click("Unstaged: bulk/file-000.txt");
        h.Press(Key.End);
        h.Check(h.Has(Role.Button, "Unstaged: bulk/file-399.txt"), "End reveals the last file row");
        h.Shot("changes-virtual");
        Directory.Delete(Path.Combine(fixture.Push, "bulk"), true);
        h.Press(Key.R, Modifiers.Primary);
        Concurrent(h, fixture);
        h.Check(launcher.Requests.Count == 2, "No other platform actions were started");
    }
    /// <summary>Two repositories refreshed together both apply; a single global generation dropped the first.</summary>
    static void Concurrent(Harness h, Repositories fixture)
    {
        Repositories.Git(fixture.Repo, "tag", "concurrent-a");
        Repositories.Git(fixture.Push, "tag", "concurrent-b");
        var model = h.Model with
        {
            Watching = false,
            ActiveWorkspace = h.Model.ActiveWorkspace
        };
        using var program = FlintApp.CreateProgram(model, ServicesOf(h));
        using var tester = new Tester(program, new(1440, 900));
        try
        {
            program.Send(new Msg.Open(fixture.Empty));
            program.Send(new Msg.Refresh(fixture.Repo));
            program.Send(new Msg.Refresh(fixture.Push));
            var until = DateTime.UtcNow.AddSeconds(10);
            do
            {
                tester.Settle();
                Thread.Sleep(10);
            } while ((program.PendingCommands > 0 || program.Model.Busy) && DateTime.UtcNow < until);
            tester.Settle();
            h.Check(program.Model.Repositories[fixture.Repo].Snapshot.Refs.Any(r => r.Name == "concurrent-a")
                && program.Model.Repositories[fixture.Push].Snapshot.Refs.Any(r => r.Name == "concurrent-b"), "Concurrent refreshes of two repositories both apply");
            h.Check(!program.Model.Busy && program.Model.Workspace.Repositories.Contains(program.Model.Repositories.Keys.First(k => k.EndsWith("unborn", StringComparison.Ordinal))),
                "Opening a repository completes while other repositories refresh");
        }
        finally
        {
            program.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
    public static IServiceProvider? Services;
    static IServiceProvider ServicesOf(Harness h) => Services ?? throw new InvalidOperationException("Services not set.");
    /// <summary>Long histories: wheel scrolling, keyboard navigation and automatic loading near the end.</summary>
    public static void History(Harness h, Repositories fixture)
    {
        h.OpenRepository(fixture.History);
        h.Check(h.Tester.ContainsText("History 80"), "Long history initially shows its latest commit");
        h.Tester.MoveTo(700, 200);
        h.Tester.Scroll(600);
        h.Tester.Settle();
        h.Check(!h.Tester.Context.Accessibility.Any(n => n.Role == Role.Button && n.Name == "Commit: History 80" && !n.Offscreen),
            "History wheel scrolling can leave the selected commit offscreen");
        h.Shot("history-scrolled");
        double Offset() => h.Tester.Context.Accessibility.Single(n => n.Role == Role.List && n.Key == "commits").Properties?.Number ?? 0;
        var scrolled = Offset();
        h.Click("Repository: flint-fixture");
        // Render past Kumiko's periodic pruning of state for nodes that are no longer built.
        h.Tester.Settle(130);
        h.Click("Repository: long-history");
        h.Check(scrolled > 0 && Math.Abs(Offset() - scrolled) < 1, "Switching tabs restores each repository's history scroll position");
        h.OpenRepository(fixture.Pages);
        h.Check(h.Tab.Snapshot.Commits.Length == 4000 && h.Tab.Snapshot.HasMore, "A long history loads its first page");
        h.Click("Commit: Page 4499");
        h.Press(Key.PageDown);
        h.Check(h.Tab.Sha == h.Tab.Snapshot.Commits[25].Sha, "PageDown moves the commit selection by a page");
        h.Press(Key.End);
        h.WaitFor(() => h.Tab.Snapshot.Commits.Length == 4500 && !h.Tab.Snapshot.HasMore, "Reaching the end of the list loads more history without a button");
        h.Check(h.Tab.Detail?.Subject == "Page 500", "End selects the last loaded commit and keeps it selected after loading more");
        h.Press(Key.Home);
        h.Check(h.Tab.Sha == h.Tab.Snapshot.Commits[0].Sha, "Home selects the newest commit");
        h.Click("Close repository: " + fixture.Pages);
    }
}
