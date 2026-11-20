using DiffViewer.Sources;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// The open repository at the top of the palette: its worktrees from Git and its open pull requests from GitHub,
/// found through its remote. GitHub is HistoryChecks' local server and gh its script; the SSH configuration that maps
/// an alias to GitHub lives in a temporary home.
/// </summary>
internal static class RepoContextChecks
{
    public static void Run(Harness h)
    {
        if (OperatingSystem.IsWindows())
            return;
        Remotes(h);
        var temp = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal))
            temp = "/private" + temp;
        var scratch = Path.Combine(temp, "kumiko-diff-e2e", "context-" + Environment.ProcessId);
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, true);
        var home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(Path.Combine(home, ".ssh"));
        File.WriteAllText(Path.Combine(home, ".ssh", "config"), "Host github-work\n  HostName github.com\n  User git\n");
        try
        {
            using var repo = new Repository("widgets", scratch);
            repo.Write("src/a.ts", "export const a = 1;\n");
            repo.Commit("initial");
            repo.Git("remote", "add", "origin", "git@github-work:acme/widgets.git");
            repo.Git("worktree", "add", "-q", "-b", "feature", Path.Combine(scratch, "widgets-feature"));
            repo.Write("src/a.ts", "export const a = 2;\n");
            using var github = new HistoryChecks.FakeGitHub();
            Palette(h, repo, scratch, home, github);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    static void Remotes(Harness h)
    {
        string Name(string url, Func<string, string>? resolve = null) => RepoInfo.Parse(url, resolve) is { } remote ? remote.Host + " " + remote.Repository : "none";
        h.Check(Name("git@github.com:acme/widgets.git") == "github.com acme/widgets" && Name("https://github.com/acme/widgets") == "github.com acme/widgets" &&
            Name("https://me@ghe.example.com/acme/widgets.git/") == "ghe.example.com acme/widgets" &&
            Name("ssh://git@github.com:22/acme/widgets.git") == "github.com acme/widgets",
            "scp-like, https and ssh:// remotes name their host and repository");
        h.Check(Name("git@github-work:acme/widgets.git", alias => alias == "github-work" ? "github.com" : alias) == "github.com acme/widgets",
            "an SSH alias resolves to its HostName");
        h.Check(Name("/srv/git/widgets.git") == "none" && Name("file:///srv/git/widgets.git") == "none" && Name("../widgets") == "none" &&
            Name("git@github.com:widgets") == "none", "local paths and remotes without an owner name no repository");
    }

    static void Palette(Harness h, Repository repo, string scratch, string home, HistoryChecks.FakeGitHub github)
    {
        var store = new ProjectStore(Path.Combine(scratch, "state"), scratch, []);
        using var program = Harness.Open(new(SourceKind.WorkingTree, repo.Root, "HEAD"), m => m with
        {
            Store = store,
            Cached = true,
            SshHome = home,
            GitHub = new GitHubConfig(github.Gh, github.Api),
            Recent = [Path.Combine(scratch, "elsewhere")]
        });
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        Model M() => program.Model;
        t.Settle();
        t.Keystroke('k', Modifiers.Primary);
        for (int i = 0; i < 2000 && (M().Context is not { Loading: false } || M().Pulls.Loading || !M().Pulls.Loaded); i++)
        {
            Thread.Sleep(5);
            t.Settle(1);
        }
        var context = M().Context!;
        h.Check(context.Error is null && context.Remote == new GitRemote("github.com", "acme/widgets"),
            $"the open repository is found through its remote and SSH alias ({context.Remote}, {context.Error})");
        var items = M().Quick!.Items;
        var groups = items.Select(i => i.Group).Distinct().ToArray();
        h.Check(groups.Take(3).SequenceEqual(["Worktrees", "Pull requests · widgets", "Recent"]),
            $"the open repository's worktrees and pull requests come first, then recent projects ({string.Join(", ", groups)})");
        var worktrees = items.Where(i => i.Group == "Worktrees").ToArray();
        h.Check(worktrees.Length == 2 && worktrees[0].Detail.StartsWith("current", StringComparison.Ordinal) && worktrees[1].Label == "widgets/widgets-feature" &&
            worktrees[1].Hint == "feature" && worktrees[1].Icon == Hugeicons.GitFork, "the worktrees come from Git, the current one marked and each with its branch");
        var pulls = items.Where(i => i.Group == "Pull requests · widgets").ToArray();
        h.Check(pulls.Select(i => i.Hint).SequenceEqual(["#7", "#9"]) && pulls[1].Icon == Hugeicons.GitPullRequestDraft && github.Listings.Count == 1,
            "the repository's open pull requests are listed from one request, drafts marked");
        h.Check(items.Where(i => i.Group == "Pull requests").Select(i => i.Hint).SequenceEqual(["#3"]),
            "other pull requests leave out the ones of the open repository");
        h.Artifact(t, "repo-context");
        t.TypeText("#");
        t.Settle();
        h.Check(M().Quick!.Items.Select(i => i.Hint).SequenceEqual(["#7", "#9", "#3"]), "# lists the open repository's pull requests first");
        t.Key(Key.Escape);
    }
}
