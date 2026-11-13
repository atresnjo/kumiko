using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Pull requests and commits in the ⌘K palette. GitHub is a local HTTP server and gh a script that reports two
/// accounts with made-up tokens, so the user's accounts, tokens and network are never used.
/// </summary>
internal static class HistoryChecks
{
    public static void Run(Harness h)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var repo = new Repository("history");
        repo.Write("src/a.ts", "export const a = 1;\n");
        repo.Commit("first: add a");
        repo.Write("src/a.ts", "export const a = 2;\n");
        repo.Commit("second: change a");
        repo.Write("src/b.ts", "export const b = 1;\n");
        repo.Commit("third: add b");
        using var github = new FakeGitHub();
        var config = new GitHubConfig(github.Gh, github.Api)
        {
            CacheFor = TimeSpan.FromMilliseconds(400)
        };
        using var program = Harness.Open(new DiffRequest(SourceKind.WorkingTree, repo.Root, "HEAD"), m => m with
        {
            GitHub = config
        });
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        Model M() => program.Model;
        void Until(Func<bool> done, string what)
        {
            for (int i = 0; i < 2000 && !done(); i++)
            {
                Thread.Sleep(5);
                t.Settle(1);
            }
            h.Check(done(), what);
        }

        t.Keystroke('k', Modifiers.Primary);
        Until(() => M().Pulls.Loaded && !M().Pulls.Loading, "opening the palette loads pull requests in the background");
        var pulls = M().Pulls.Items;
        h.Check(pulls.Length == 2 && pulls.Select(p => (p.Repository, p.Number)).Order().SequenceEqual([("acme/gears", 3), ("acme/widgets", 7)]),
            $"pull requests of every gh account are listed once ({string.Join(", ", pulls.Select(p => p.Repository + "#" + p.Number))})");
        h.Check(pulls.Single(p => p.Number == 3).Account.Login == "bob" && github.Searches.Order().SequenceEqual(["token-alice", "token-bob"]),
            "each account searches with its own token");
        h.Check(M().Quick!.Items.Count(i => i.Group == "Pull requests") == 2 && t.ContainsText("Add widget sorting"),
            "the palette lists pull requests");
        int Requests(string repository) => github.Fetches.Count(f => f.Path.StartsWith("/repos/" + repository + "/", StringComparison.Ordinal));
        Until(() => M().Prefetched.Values.Count(p => p.Document is not null) == 1 && M().Prefetched.Values.Count(p => p.Failed) == 1,
            "the listed pull requests load in the background");
        h.Check(Requests("acme/widgets") > 0 && Requests("acme/gears") == 1, "both visible pull requests are fetched, the one GitHub refuses once");
        h.Artifact(t, "pulls");
        t.TypeText("#gear");
        t.Settle();
        h.Check(M().Quick!.Items is [{ Group: "Pull requests", Label: "Fix gear ratio" }], "# limits the palette to pull requests and searches them");

        t.Key(Key.Escape);
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        h.Check(github.Searches.Count == 2 && !M().Pulls.Loading, "pull requests are cached instead of searched again");
        Thread.Sleep(500);
        t.Settle();
        t.Key(Key.Escape);
        t.Keystroke('k', Modifiers.Primary);
        Until(() => github.Searches.Count == 4 && !M().Pulls.Loading, "after the cache time the palette searches again");
        t.TypeText(">refresh pull");
        t.Settle();
        h.Check(M().Quick!.Items.FirstOrDefault()?.Label == "Refresh Pull Requests", "the commands list Refresh Pull Requests");
        t.Key(Key.Enter);
        Until(() => github.Searches.Count == 6 && !M().Pulls.Loading, "Refresh Pull Requests searches at once");
        h.Check(M().Quick is not null, "refreshing keeps the palette open");

        t.Key(Key.Escape);
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        t.TypeText("#widget");
        t.Settle();
        var fetched = Requests("acme/widgets");
        h.Check(Requests("acme/gears") == 1, "a refused pull request is not fetched again while it is unchanged");
        t.Key(Key.Enter);
        h.Check(M().Request is { Kind: SourceKind.PullRequest, Pull.Number: 7 } && M().Quick is null, "Enter opens the pull request");
        h.Check(!M().Loading && M().Document.Files.Length == 4 && Requests("acme/widgets") == fetched,
            "a prefetched pull request opens at once, without asking GitHub again");
        var files = M().Document.Files;
        h.Check(M().Error is null && files.Select(f => (f.Path, f.Status)).SequenceEqual([
            ("src/added.ts", FileStatus.Added), ("src/gone.ts", FileStatus.Deleted), ("src/moved.ts", FileStatus.Renamed), ("src/sort.ts", FileStatus.Modified)]),
            $"the pull request's files load with their status ({M().Error}{string.Join(", ", files.Select(f => f.Path + ":" + f.Status))})");
        var sort = files.Single(f => f.Path == "src/sort.ts");
        h.Check(sort.Additions == 1 && sort.Deletions == 1 && sort.Text(sort.Lines.First(l => l.Kind == LineKind.Added)) == "export const order = \"desc\";",
            "a file is compared from the merge base to the head");
        h.Check(files.Single(f => f.Path == "src/moved.ts").OldPath == "src/old.ts", "a renamed file keeps its old path");
        h.Check(github.Fetches.All(f => f.Token == (f.Path.StartsWith("/repos/acme/gears/", StringComparison.Ordinal) ? "token-bob" : "token-alice")) &&
            github.Fetches.Any(f => f.Path.Contains("ref=base-sha", StringComparison.Ordinal)),
            "the pull request is read with the account that found it, from its merge base");
        h.Check(M().Document.Title == "acme/widgets #7" && M().Document.Detail == "Add widget sorting", "the title names the pull request");
        h.Artifact(t, "pull");

        // A pull request opened while it still loads in the background waits for that load instead of loading twice.
        github.Updated = "2026-11-08T09:00:00Z";
        github.Hold.Reset();
        var before = Requests("acme/widgets");
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        t.TypeText(">refresh pull");
        t.Settle();
        t.Key(Key.Enter);
        Until(() => !M().Pulls.Loading, "the refreshed list arrives");
        h.Check(M().Prefetched.Keys.All(k => !k.Contains("/widgets/", StringComparison.Ordinal)), "a pull request updated on GitHub drops its earlier load");
        t.Key(Key.Escape);
        t.Keystroke('k', Modifiers.Primary);
        Until(() => M().Prefetched.Count(p => p.Key.Contains("/widgets/", StringComparison.Ordinal) && p.Value is { Document: null, Failed: false }) == 1,
            "the updated pull request loads again in the background once listed");
        t.TypeText("#widget");
        t.Settle();
        t.Key(Key.Enter);
        t.Settle();
        h.Check(M().Loading && M().Request.Pull?.Number == 7, "the pull request opens while its background load runs");
        github.Hold.Set();
        Until(() => !M().Loading, "the background load shows the pull request");
        h.Check(M().Document.Files.Length == 4 && Requests("acme/widgets") - before == 9,
            $"the pull request is fetched once, not again for opening it ({Requests("acme/widgets") - before} requests)");

        Commits(h, t, program, repo, Until);
    }

    static void Commits(Harness h, Tester t, Program<Model, Msg> program, Repository repo, Action<Func<bool>, string> until)
    {
        Model M() => program.Model;
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        t.TypeText("@");
        until(() => M().Quick!.Items.Length == 3, "@ lists the open repository's commits");
        h.Check(M().Quick!.Items.Select(i => i.Label).SequenceEqual(["third: add b", "second: change a", "first: add a"]) &&
            M().Quick!.Items.All(i => i.Group == "Commits"), "commits are newest first");
        h.Artifact(t, "commits");
        t.TypeText("second");
        t.Settle();
        t.Key(Key.Enter);
        var second = repo.Git("rev-parse", "HEAD~1").Trim();
        h.Check(M().Request is { Kind: SourceKind.Revisions } r && r.Target == second && r.Base == repo.Git("rev-parse", "HEAD~2").Trim(),
            "Enter shows the commit against its parent");
        until(() => !M().Loading, "the commit loads");
        h.Check(M().Document.Files is [{ Path: "src/a.ts", Additions: 1, Deletions: 1 }] && M().Document.Detail.Contains("second: change a", StringComparison.Ordinal),
            "the commit's change is shown with its subject");

        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        t.TypeText("@first");
        until(() => M().Quick!.Items.Length == 1, "searching commits finds the first one");
        t.Key(Key.Enter);
        until(() => !M().Loading, "the first commit loads");
        h.Check(M().Document.Files is [{ Path: "src/a.ts", Status: FileStatus.Added }], "the first commit is compared with an empty tree");

        repo.Write("src/c.ts", "export const c = 1;\n");
        repo.Commit("fourth: add c");
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        t.TypeText("@");
        until(() => M().Quick!.Items.Length == 4, "a new commit is listed the next time the palette opens");
        t.Key(Key.Escape);
    }

    /// <summary>The GitHub REST API for one pull request, and a gh script that names two accounts.</summary>
    sealed class FakeGitHub : IDisposable
    {
        readonly HttpListener listener = new();
        readonly string folder = Path.Combine(Path.GetTempPath(), "kumiko-diff-e2e", "github-" + Environment.ProcessId);
        public ConcurrentQueue<string> Searches { get; } = new();
        /// <summary>Pull request reads wait while this is reset, as a slow network would.</summary>
        public ManualResetEventSlim Hold { get; } = new(true);
        public string Updated { get; set; } = "2026-11-07T12:00:00Z";
        public ConcurrentQueue<(string Token, string Path)> Fetches { get; } = new();
        public string Api
        {
            get;
        }
        public string Gh
        {
            get;
        }

        public FakeGitHub()
        {
            int port = 0;
            for (int attempt = 0; attempt < 20 && port == 0; attempt++)
            {
                var candidate = Random.Shared.Next(40000, 60000);
                try
                {
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add($"http://127.0.0.1:{candidate}/");
                    listener.Start();
                    port = candidate;
                }
                catch (HttpListenerException)
                {
                }
            }
            Api = $"http://127.0.0.1:{port}/";
            Directory.CreateDirectory(folder);
            Gh = Path.Combine(folder, "gh");
            File.WriteAllText(Gh, """
                #!/bin/sh
                if [ "$1 $2" = "auth status" ]; then
                  printf '%s' '{"hosts":{"github.com":[{"state":"success","active":true,"host":"github.com","login":"alice"},{"state":"success","active":false,"host":"github.com","login":"bob"}]}}'
                elif [ "$1 $2" = "auth token" ]; then
                  printf 'token-%s\n' "$6"
                else
                  exit 1
                fi
                """);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(Gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            _ = Serve();
        }

        async Task Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                var token = (context.Request.Headers["Authorization"] ?? "").Replace("Bearer ", "", StringComparison.Ordinal);
                var path = context.Request.RawUrl ?? "";
                var (status, body) = Answer(token, path);
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        (int, string) Answer(string token, string path)
        {
            if (path.StartsWith("/search/issues", StringComparison.Ordinal))
            {
                Searches.Enqueue(token);
                var widgets = Issue("acme/widgets", 7, "Add widget sorting", "alice", false, Updated);
                return (200, token == "token-alice" ? $$"""{"items":[{{widgets}}]}""" : $$"""{"items":[{{widgets}},{{Issue("acme/gears", 3, "Fix gear ratio", "carol", true, "2026-11-07T12:00:00Z")}}]}""");
            }
            Fetches.Enqueue((token, path));
            Hold.Wait(TimeSpan.FromSeconds(20));
            return path switch
            {
                "/repos/acme/widgets/pulls/7" => (200, """{"number":7,"title":"Add widget sorting","base":{"sha":"tip-sha"},"head":{"sha":"head-sha"}}"""),
                "/repos/acme/widgets/compare/tip-sha...head-sha?per_page=1" => (200, """{"merge_base_commit":{"sha":"base-sha"}}"""),
                "/repos/acme/widgets/pulls/7/files?per_page=100&page=1" => (200, """
                    [{"filename":"src/sort.ts","status":"modified"},{"filename":"src/added.ts","status":"added"},
                     {"filename":"src/moved.ts","previous_filename":"src/old.ts","status":"renamed"},{"filename":"src/gone.ts","status":"removed"}]
                    """),
                "/repos/acme/widgets/pulls/7/files?per_page=100&page=2" => (200, "[]"),
                "/repos/acme/widgets/contents/src/sort.ts?ref=base-sha" => (200, "export const order = \"asc\";\nexport const size = 1;\n"),
                "/repos/acme/widgets/contents/src/sort.ts?ref=head-sha" => (200, "export const order = \"desc\";\nexport const size = 1;\n"),
                "/repos/acme/widgets/contents/src/added.ts?ref=head-sha" => (200, "export const added = true;\n"),
                "/repos/acme/widgets/contents/src/old.ts?ref=base-sha" => (200, "export const moved = 1;\n"),
                "/repos/acme/widgets/contents/src/moved.ts?ref=head-sha" => (200, "export const moved = 2;\n"),
                "/repos/acme/widgets/contents/src/gone.ts?ref=base-sha" => (200, "export const gone = 1;\n"),
                _ => (404, """{"message":"Not Found"}""")
            };
        }

        static string Issue(string repository, int number, string title, string author, bool draft, string updated) =>
            $"{{\"number\":{number},\"title\":\"{title}\",\"draft\":{(draft ? "true" : "false")},\"user\":{{\"login\":\"{author}\"}}," +
            $"\"html_url\":\"https://github.com/{repository}/pull/{number}\",\"repository_url\":\"https://api.github.com/repos/{repository}\"," +
            $"\"updated_at\":\"{updated}\",\"pull_request\":{{}}}}";

        public void Dispose()
        {
            Hold.Set();
            listener.Close();
            Directory.Delete(folder, true);
        }
    }
}
