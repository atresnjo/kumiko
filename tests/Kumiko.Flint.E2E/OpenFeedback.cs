using Flint;

/// <summary>
/// Opening a repository waits for several Git commands. Until they answer, the palette closes and nothing on screen
/// changes, so the open feels slower than it is. The tab must appear at once and then fill with the history and the
/// newest commit.
/// </summary>
static class OpenFeedback
{
    public static void Run(Harness h, CountingGit git, Repositories fixture)
    {
        var tester = h.Tester;
        var path = Path.Combine(fixture.Root, "instant");
        Directory.CreateDirectory(Path.Combine(path, "docs"));
        Repositories.Git(path, "init", "-q", "-b", "main");
        Repositories.Git(path, "config", "user.name", "Flint Tester");
        Repositories.Git(path, "config", "user.email", "flint@example.test");
        Repositories.Git(path, "config", "commit.gpgsign", "false");
        File.WriteAllText(Path.Combine(path, "docs", "guide.md"), "first\n");
        Repositories.Git(path, "add", ".");
        Repositories.Git(path, "commit", "-q", "-m", "Start the guide");
        File.WriteAllText(Path.Combine(path, "docs", "guide.md"), "first\nsecond\n");
        Repositories.Git(path, "commit", "-q", "-am", "Extend the guide");
        var previous = h.Model.Workspace.Active;

        git.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Program.Send(new Msg.Open(path));
        tester.Settle();
        h.Check(h.Model.Workspace.Active == path && h.Model.Workspace.Repositories.Contains(path) && h.Model.Current is null,
            "Opening a repository shows its tab before Git answers");
        h.Check(tester.ContainsText("Loading…") && tester.ContainsText("instant"), "The new tab shows its name and a loading state");
        h.Shot("open-loading");
        git.Hold.SetResult();
        git.Hold = null;
        h.Idle();
        h.Check(h.Tab.Snapshot.Path == path && h.Model.Workspace.Repositories.Count(r => r == path) == 1, "The loaded repository fills the tab it opened");
        h.Check(h.Tab.Sha == h.Tab.Snapshot.Commits[0].Sha && h.Tab.Detail?.Subject == "Extend the guide" && h.Tab.Lines.Any(l => l.Text == "second")
            && tester.ContainsText("Extend the guide"), "The opened repository shows the newest commit's details and diff");

        var missing = Path.Combine(fixture.Root, "not-a-repository");
        Directory.CreateDirectory(missing);
        git.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Program.Send(new Msg.Open(missing));
        tester.Settle();
        h.Check(h.Model.Workspace.Active == missing, "A folder that is not yet known also opens a tab at once");
        git.Hold.SetResult();
        git.Hold = null;
        h.Idle();
        h.Check(!h.Model.Workspaces.Any(w => w.Repositories.Contains(missing)) && h.Model.Workspace.Active == path && h.Model.Error.Length > 0,
            "A failed open removes its tab and returns to the previous repository");
        h.Press(Kumiko.UI.Key.Escape);

        h.Program.Send(new Msg.CloseRepo(path));
        h.Idle();
        h.Program.Send(new Msg.Open(Path.Combine(path, "docs")));
        h.Idle();
        h.Check(h.Model.Workspace.Active == path && !h.Model.Workspaces.Any(w => w.Repositories.Contains(Path.Combine(path, "docs"))),
            "Opening a subfolder leaves only the repository root as a tab");
        h.Program.Send(new Msg.CloseRepo(path));
        h.Idle();
        if (previous is not null)
        {
            h.Program.Send(new Msg.SelectRepo(previous));
            h.Idle();
        }
    }
}
