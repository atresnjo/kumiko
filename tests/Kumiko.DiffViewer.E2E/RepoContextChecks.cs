using DiffViewer.Sources;
using Kumiko.Icons;
using Kumiko.Runtime;
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
            Launches(h, repo, scratch, home, github);
            PullViaGit(h, repo, scratch, github);
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
        h.Check(M().Prefetched.Keys.All(k => !k.Contains("/acme/widgets/", StringComparison.Ordinal)) && !M().Prefetched.IsEmpty,
            "the open repository's pull requests are not preloaded through the API; other ones are");
        h.Artifact(t, "repo-context");
        t.TypeText("#");
        t.Settle();
        h.Check(M().Quick!.Items.Select(i => i.Hint).SequenceEqual(["#7", "#9", "#3"]), "# lists the open repository's pull requests first");
        t.Key(Key.Escape);
    }

    /// <summary>
    /// What a T3 action or a terminal asks for: --pr finds the pull request of the checkout's branch, and a running
    /// Diff routes a later launch to the window that already shows it.
    /// </summary>
    static void Launches(Harness h, Repository repo, string scratch, string home, HistoryChecks.FakeGitHub github)
    {
        var feature = Path.Combine(scratch, "widgets-feature");
        h.Check(Arguments.Parse(["--pr"], feature) is { Kind: SourceKind.PullRequest, PullQuery: "", Pull: null } pr && pr.Path == feature &&
            Arguments.Parse(["--repo=box:~/app", "--pr=7"], "/tmp") is { Kind: SourceKind.PullRequest, Host: "box", Path: "~/app", PullQuery: "7" } &&
            Arguments.Parse(["--pr=https://github.com/acme/widgets/pull/7"], feature).PullQuery == "https://github.com/acme/widgets/pull/7",
            "--pr, --pr=N and --pr=URL ask for a pull request of the checkout");
        Model Find(string[] args, string directory, bool reload = false)
        {
            var request = Arguments.Parse(args, directory);
            using var program = DiffApp.CreateProgram(DiffApp.Init with
            {
                Request = request,
                SshHome = home,
                GitHub = new GitHubConfig(github.Gh, github.Api)
            }, new KumikoServices().Add<IFullScreen>(new NoFullScreen()));
            program.RetainHistory = false;
            using var t = new Tester(program, new(1280, 820, DevMode: true));
            t.Settle();
            // Reloading while the pull request is looked up starts the lookup again instead of waiting forever.
            if (reload)
            {
                program.Send(new Msg.Reload());
                program.Send(new Msg.ToggleWhitespace());
            }
            var flashed = false;
            for (int i = 0; i < 2000 && program.Model is { Request.Pull: null, Error: null }; i++)
            {
                flashed |= program.Model.Document.Files.Length > 0;
                Thread.Sleep(5);
                t.Settle(1);
            }
            h.Check(!flashed, $"looking up {string.Join(' ', args)} shows no working tree meanwhile");
            return program.Model;
        }
        var branch = Find(["--pr"], feature);
        h.Check(branch.Request is { Kind: SourceKind.PullRequest, Pull.Number: 9, PullQuery: "" } && branch.Error is null,
            $"--pr in a worktree opens the open pull request of its branch ({branch.Request.Pull?.Number}, {branch.Error})");
        h.Check(Find(["--pr"], feature, reload: true).Request.Pull?.Number == 9, "reloading during the lookup still opens the pull request");
        h.Check(Find(["--pr=7"], feature).Request.Pull?.Number == 7 && Find(["--pr=https://github.com/acme/widgets/pull/7"], "/").Request.Pull?.Number == 7,
            "--pr=N and a pull request URL open that pull request");
        var none = Find(["--pr"], repo.Root);
        h.Check(none is { Request.Pull: null, Loading: false } && none.Error == "No open pull request in acme/widgets has main as its head branch.",
            $"a branch without a pull request says so ({none.Error})");

        // A running Diff focuses the window that already shows what a later launch asks for.
        using var opened = Harness.Open(new(SourceKind.WorkingTree, repo.Root, "HEAD"));
        var worktree = opened.Model;
        h.Check(DiffWindows.Shows(worktree, Arguments.Parse([], repo.Root)) && DiffWindows.Shows(worktree, Arguments.Parse(["--repo=" + repo.Root], "/")) &&
            !DiffWindows.Shows(worktree, Arguments.Parse(["--staged"], repo.Root)) && !DiffWindows.Shows(worktree, Arguments.Parse([], feature)),
            "a launch for the worktree a window shows is routed to it, other sources and worktrees are not");
        h.Check(DiffWindows.Shows(branch, Arguments.Parse(["--pr"], feature)) && !DiffWindows.Shows(branch, Arguments.Parse(["--pr=7"], feature)) &&
            !DiffWindows.Shows(branch, Arguments.Parse([], feature)), "a launch for the --pr a window shows is routed to it");
        var moved = DiffApp.Update(branch, new Msg.OpenProject(repo.Root)).Item1;
        h.Check(!DiffWindows.Shows(moved, Arguments.Parse(["--pr"], feature)), "a window that switched elsewhere no longer takes that launch");
    }

    /// <summary>
    /// A pull request of the open repository comes from Git alone: ls-remote names its head and GitHub's merge ref
    /// (whose first parent is the base), missing commits are fetched from the checkout's remote (a bare repository
    /// standing in for GitHub through url.insteadOf), and merge base to head is diffed there. No API request is made.
    /// A pull request Git cannot reach falls back to the API.
    /// </summary>
    static void PullViaGit(Harness h, Repository repo, string scratch, HistoryChecks.FakeGitHub github)
    {
        var origin = Path.Combine(scratch, "widgets-origin.git");
        repo.Git("init", "-q", "--bare", "-b", "main", origin);
        repo.Git("config", "url." + origin + ".insteadOf", "git@github-work:acme/widgets.git");
        repo.Git("push", "-q", "origin", "main");
        var contributor = Path.Combine(scratch, "contributor");
        repo.Git("clone", "-q", origin, contributor);
        void Publish(string file, string text)
        {
            repo.Git("-C", contributor, "checkout", "-q", "-B", "sorting");
            File.WriteAllText(Path.Combine(contributor, "src", file), text);
            repo.Git("-C", contributor, "add", "-A");
            repo.Git("-C", contributor, "commit", "-q", "-m", "Add widget sorting");
            repo.Git("-C", contributor, "push", "-q", "-f", "origin", "HEAD:refs/pull/7/head");
            // GitHub's test merge of the head into the base: its first parent is the base's tip.
            repo.Git("-C", contributor, "checkout", "-q", "--detach", "origin/main");
            repo.Git("-C", contributor, "merge", "-q", "--no-ff", "-m", "Merge pull request 7", "sorting");
            repo.Git("-C", contributor, "push", "-q", "-f", "origin", "HEAD:refs/pull/7/merge");
        }
        Publish("sort.ts", "export const order = \"desc\";\n");
        var account = new GitHubAccount(new GitHubConfig(github.Gh, github.Api), "github.com", "alice");
        DiffRequest Request(int number) => new(SourceKind.PullRequest, repo.Root)
        {
            Pull = new PullRequest(account, "acme/widgets", number, "Add widget sorting", "alice", false, DateTimeOffset.UnixEpoch,
                $"https://github.com/acme/widgets/pull/{number}")
        };
        IEnumerable<string> Since(int count) => github.Fetches.Skip(count).Select(f => f.Path);
        var before = github.Fetches.Count;
        var fetched = Loader.Load(Request(7), false, CancellationToken.None).GetAwaiter().GetResult();
        h.Check(fetched.Files.Select(f => (f.Path, f.Status)).SequenceEqual([("src/sort.ts", Core.FileStatus.Added)]) &&
            fetched is { Title: "acme/widgets #7", Detail: "Add widget sorting" },
            $"a pull request of the open repository shows its changes from the merge base ({string.Join(", ", fetched.Files.Select(f => f.Path))})");
        var head = repo.Git("-C", contributor, "rev-parse", "sorting").Trim();
        h.Check(!Since(before).Any() && repo.Git("cat-file", "-t", head).Trim() == "commit",
            $"its head is fetched into the checkout without asking GitHub's API ({string.Join(", ", Since(before))})");
        Publish("a.ts", "export const a = 3;\n");
        var updated = Loader.Load(Request(7), false, CancellationToken.None).GetAwaiter().GetResult();
        h.Check(updated.Files.Select(f => f.Path).SequenceEqual(["src/a.ts", "src/sort.ts"]) && !Since(before).Any(), "an updated pull request shows its new head");
        before = github.Fetches.Count;
        string failure;
        try
        {
            Loader.Load(Request(8), false, CancellationToken.None).GetAwaiter().GetResult();
            failure = "loaded";
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            failure = error.Message;
        }
        h.Check(Since(before).Contains("/repos/acme/widgets/pulls/8"), $"a pull request Git cannot reach falls back to the API ({failure})");
    }
}
