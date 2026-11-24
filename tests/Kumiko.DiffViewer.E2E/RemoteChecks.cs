using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Remote checkouts over SSH. The ssh client is a script (DIFF_SSH) that logs each session, then runs the remote
/// command through /bin/sh in a temporary home with isolated Git settings, the way sshd runs it in the login shell.
/// No host, SSH configuration or key is ever used. Results must match the same checkout loaded locally.
/// </summary>
internal static class RemoteChecks
{
    public static void Run(Harness h)
    {
        if (OperatingSystem.IsWindows())
            return;
        var temp = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal))
            temp = "/private" + temp;
        var scratch = Path.Combine(temp, "kumiko-diff-e2e", "remote-" + Environment.ProcessId);
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, true);
        var home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(home);
        var log = Path.Combine(scratch, "sessions.log");
        var ssh = FakeSsh(scratch, home, log);
        var previous = Environment.GetEnvironmentVariable("DIFF_SSH");
        Environment.SetEnvironmentVariable("DIFF_SSH", ssh);
        try
        {
            // An apostrophe and a space: every path must survive the remote shell's quoting.
            using var repo = new Repository("o'neil app", home);
            Fixture(repo);
            Arguments_(h);
            Views(h, repo, scratch);
            Loads(h, repo, log);
            Palette(h, repo, scratch);
            Failures(h);
            SshHostChecks.Run(h, scratch, home);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DIFF_SSH", previous);
            Directory.Delete(scratch, true);
        }
    }

    static void Fixture(Repository repo)
    {
        repo.Write(".gitignore", "dist/\n");
        repo.Write("src/app.ts", string.Join('\n', Enumerable.Range(1, 40).Select(i => $"export const value{i} = {i};")) + "\n");
        repo.Write("src/remove-me.ts", "export const gone = true;\n");
        repo.Write("src/old-name.ts", string.Join('\n', Enumerable.Range(1, 30).Select(i => $"// line {i}")) + "\n");
        repo.Write("README.md", "# Fixture\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "logo.bin"), [0, 1, 2, 3, 0, 255]);
        repo.Commit("initial");
        repo.Write("src/app.ts", string.Join('\n', Enumerable.Range(1, 40).Select(i => i == 20 ? "export const value20 = 200;" : $"export const value{i} = {i};")) + "\n");
        File.Delete(Path.Combine(repo.Root, "src/remove-me.ts"));
        repo.Git("mv", "src/old-name.ts", "src/new-name.ts");
        repo.Write("README.md", "# Fixture, staged\n");
        repo.Git("add", "README.md");
        repo.Write("src/new view.tsx", "export const View = () => <div>{1}</div>;\n");
        repo.Write("src/ünïcode 'q'.ts", "export const café = \"crème\";\n");
        repo.Write("dist/out.js", "ignored();\n");
        File.WriteAllBytes(Path.Combine(repo.Root, "logo.bin"), [0, 1, 2, 3, 0, 254, 9]);
        File.WriteAllBytes(Path.Combine(repo.Root, "latin1.ts"), [0x2F, 0x2F, 0x20, 0xE9, 0x0A]);
    }

    static void Arguments_(Harness h)
    {
        h.Check(Arguments.Parse(["box:~/code/app"], "/tmp") is { Kind: SourceKind.WorkingTree, Host: "box", Path: "~/code/app", Base: "HEAD" },
            "host:~/path opens a remote working tree");
        h.Check(Arguments.Parse(["me@box:/srv/app", "main..feature"], "/tmp") is { Kind: SourceKind.Revisions, Host: "me@box", Path: "/srv/app", Base: "main", Target: "feature" },
            "user@host:/path takes revisions after it");
        h.Check(Arguments.Parse(["ssh://me@box:2222/srv/app"], "/tmp") is { Host: "ssh://me@box:2222", Path: "/srv/app" } &&
            Arguments.Parse(["ssh://box/~/app"], "/tmp") is { Host: "ssh://box", Path: "~/app" },
            "ssh:// URLs keep user and port for ssh, and /~/ names the remote home");
        h.Check(Arguments.Parse(["--ssh=box", "--repo=/srv/app", "--staged"], "/tmp") is { Kind: SourceKind.Staged, Host: "box", Path: "/srv/app" } &&
            Arguments.Parse(["--ssh=box", "~/app", "HEAD~1"], "/tmp") is { Kind: SourceKind.WorkingTree, Host: "box", Path: "~/app", Base: "HEAD~1" } &&
            Arguments.Parse(["--repo=box:/srv/app"], "/tmp") is { Host: "box", Path: "/srv/app" },
            "--ssh=HOST with --repo or a leading path, and --repo=host:/path, select a remote checkout");
        h.Check(Arguments.Parse(["HEAD~1"], "/tmp") is { Host: null, Base: "HEAD~1" } && Arguments.Parse(["origin:main"], "/tmp").Host is null &&
            Arguments.Parse(["main..feature"], "/tmp").Host is null,
            "revisions, even with a colon, are never taken for a host");
        h.Check(Arguments.InRepository(new(SourceKind.WorkingTree, "/no/such/folder") { Host = "box" }),
            "a remote checkout opens directly instead of in the project palette");
    }

    /// <summary>The window over a remote checkout, and opening remote checkouts from the palette.</summary>
    static void Views(Harness h, Repository repo, string scratch)
    {
        var store = new ProjectStore(Path.Combine(scratch, "views-state"), scratch, []);
        var request = new DiffRequest(SourceKind.WorkingTree, "~/" + Path.GetFileName(repo.Root), "HEAD") { Host = "box" };
        using var program = Harness.Open(request, m => m with { Store = store, Recent = ["box:/srv/billing", "me@devbox:~/code/api"], Cached = true });
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        t.Settle();
        h.Check(t.HasText("Working tree on box") && t.HasText("src/app.ts"), "the window shows the remote diff and names its host");
        h.Artifact(t, "remote-working-tree");
        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        var recent = program.Model.Quick!.Items.Where(i => i.Group == "Recent").ToArray();
        h.Check(recent.Length == 2 && recent.All(i => i.Icon == Hugeicons.FolderCloud) && recent[0].Detail == "box:/srv/billing",
            "recent remote checkouts are listed with their host and a remote folder icon");
        h.Artifact(t, "remote-palette");
        t.TypeText("me@devbox:~/code/api");
        t.Settle();
        h.Check(program.Model.Quick!.Items is [{ Icon: var icon }] && icon == Hugeicons.FolderCloud, "a typed host:path is offered as the only row");
        h.Artifact(t, "remote-palette-path");
        t.Key(Key.Enter);
        h.Check(program.Model.Request is { Kind: SourceKind.WorkingTree, Host: "me@devbox", Path: "~/code/api" }, "Enter opens the typed checkout over SSH");
    }

    static void Loads(Harness h, Repository repo, string log)
    {
        var remotePath = "~/" + Path.GetFileName(repo.Root);
        var remote = new DiffRequest(SourceKind.WorkingTree, remotePath, "HEAD") { Host = "box" };
        File.WriteAllText(log, "");
        var far = Load(remote);
        var sessions = File.ReadAllLines(log);
        var near = Load(new(SourceKind.WorkingTree, repo.Root, "HEAD"));
        h.Check(Same(far, near), $"a remote working tree matches the same checkout loaded locally ({string.Join(", ", far.Files.Select(f => f.Path))})");
        h.Check(far.Files.Length == 8 && far.Files.Any(f => f.Path == "src/ünïcode 'q'.ts") && far.Files.Single(f => f.Path == "logo.bin").Binary,
            "remote paths with spaces, quotes and Unicode, and binary files, load");
        h.Check(far.Root == repo.Root && far.Detail.Contains("box", StringComparison.Ordinal), $"the document names the remote root and its host ({far.Root}, {far.Detail})");
        // toplevel, HEAD, diff, untracked, blob sizes, blobs, working files: one session each, never one per file.
        h.Check(sessions.Length is > 0 and <= 7 && sessions.All(s => s.StartsWith("box\t", StringComparison.Ordinal)),
            $"every Git call runs over SSH on the host, in a bounded number of sessions ({sessions.Length})");
        h.Check(sessions.Count(s => s.Contains("tar", StringComparison.Ordinal)) == 1, "the working-tree files arrive in one stream, not one session each");
        h.Check(!remote.Watches, "a remote checkout starts no local file watcher");

        var staged = Load(remote with { Kind = SourceKind.Staged });
        h.Check(Same(staged, Load(new(SourceKind.Staged, repo.Root, "HEAD"))) && staged.Files.Length == 2, "remote staged changes match local ones");
        repo.Commit("second");
        repo.Git("remote", "add", "origin", "https://github.com/acme/billing.git");
        var (worktrees, url) = RepoInfo.Read(new Place(remotePath, "box"), CancellationToken.None).GetAwaiter().GetResult();
        h.Check(worktrees is [{ Main: true, Branch: "main" } main] && main.Path == repo.Root && url == "https://github.com/acme/billing.git",
            "a remote checkout's worktrees and remote are read over SSH");
        var commits = Commits.Log(new Place(remotePath, "box"), CancellationToken.None).GetAwaiter().GetResult();
        h.Check(commits.Select(c => c.Subject).SequenceEqual(["second", "initial"]), "commits are read from the remote repository");
        var range = Load(new(SourceKind.Revisions, remotePath, commits[1].Sha, commits[0].Sha) { Host = "box" });
        h.Check(Same(range, Load(new(SourceKind.Revisions, repo.Root, commits[1].Sha, commits[0].Sha))) && range.Files.Length == 8,
            "a remote commit range matches the local one");
        // A generated megabyte file is not carried over SSH; the same range read locally shows it.
        repo.Write("docs/schema.json", string.Concat(Enumerable.Repeat("{\"field\": \"value\"},\n", 80_000)));
        repo.Write("src/app.ts", "export const changed = true;\n");
        repo.Commit("third");
        var third = repo.Git("rev-parse", "HEAD").Trim();
        var large = Load(new(SourceKind.Revisions, remotePath, commits[0].Sha, third) { Host = "box" });
        var schema = large.Files.Single(f => f.Path == "docs/schema.json");
        h.Check(schema is { Binary: true, Omitted: > 1_000_000 } && large.Files.Single(f => f.Path == "src/app.ts").Additions == 1 &&
            Load(new(SourceKind.Revisions, repo.Root, commits[0].Sha, third)).Files.Single(f => f.Path == "docs/schema.json").Additions == 80_000,
            $"a file over 1 MiB is not carried over SSH but named with its size, small ones are; locally it shows ({schema.Omitted})");
    }

    /// <summary>Switching sources, commits and recents keep the host; remote and local projects never mix.</summary>
    static void Palette(Harness h, Repository repo, string scratch)
    {
        var store = new ProjectStore(Path.Combine(scratch, "state"), scratch, []);
        var request = new DiffRequest(SourceKind.WorkingTree, "~/" + Path.GetFileName(repo.Root), "HEAD") { Host = "box" };
        var model = DiffApp.Update(DiffApp.Init with { Request = request, Store = store }, new Msg.Loaded(0, Load(request))).Item1;
        var id = "box:" + repo.Root;
        h.Check(model.ProjectId == id && model.Recent.FirstOrDefault() == id, $"a loaded remote checkout is remembered with its host ({model.Recent.FirstOrDefault()})");
        var palette = QuickOpen.Build(model, "");
        h.Check(palette.Single(i => i.Id == "project:" + id).Detail.StartsWith("current", StringComparison.Ordinal),
            "the palette marks the remote checkout as current");
        var local = model with { Recent = [repo.Root, .. model.Recent] };
        h.Check(!QuickOpen.Build(local, "").Single(i => i.Id == "project:" + repo.Root).Detail.StartsWith("current", StringComparison.Ordinal),
            "a local project with the remote path is not marked current");
        var staged = DiffApp.Update(model, new Msg.SetSource(SourceKind.Staged)).Item1;
        h.Check(staged.Request is { Kind: SourceKind.Staged, Host: "box" } && staged.Request.Path == repo.Root, "switching to staged changes keeps the host");
        var commit = new Commit(new string('a', 40), new string('b', 40), "subject", "author", "now");
        h.Check(DiffApp.Update(model, new Msg.OpenCommit(commit)).Item1.Request is { Kind: SourceKind.Revisions, Host: "box" }, "opening a commit keeps the host");
        h.Check(QuickOpen.Build(model with { CommitsOf = repo.Root, Commits = [commit] }, "@").All(i => i.Id != "commit:" + commit.Sha) &&
            QuickOpen.Build(model with { CommitsOf = id, Commits = [commit] }, "@").Any(i => i.Id == "commit:" + commit.Sha),
            "the palette lists the remote checkout's commits, never a local repository's at the same path");
        var reopened = DiffApp.Update(DiffApp.Init, new Msg.OpenProject(id)).Item1;
        h.Check(reopened.Request is { Kind: SourceKind.WorkingTree, Host: "box" } && reopened.Request.Path == repo.Root, "a recent remote checkout reopens on its host");
        var typed = QuickOpen.Build(DiffApp.Init, "box:~/app");
        h.Check(typed.FirstOrDefault()?.Message is Msg.OpenProject { Path: "box:~/app" }, "a typed host:path opens a remote checkout from the palette");
    }

    static void Failures(Harness h)
    {
        string Error(DiffRequest request)
        {
            var task = Loader.Load(request, false, CancellationToken.None);
            if (!task.Wait(TimeSpan.FromSeconds(20)))
                return "timed out";
            return "loaded";
        }
        string Message(DiffRequest request)
        {
            try
            {
                return Error(request);
            }
            catch (AggregateException error) when (error.InnerException is IOException io)
            {
                return io.Message;
            }
        }
        var down = Message(new(SourceKind.WorkingTree, "~/app", "HEAD") { Host = "down" });
        h.Check(down.Contains("Connection refused", StringComparison.Ordinal), $"an unreachable host fails with SSH's message ({down})");
        var plain = Message(new(SourceKind.WorkingTree, "~", "HEAD") { Host = "box" });
        h.Check(plain.StartsWith("box:~ is not a Git repository. Press ⌘K and choose box", StringComparison.Ordinal),
            $"a remote folder outside any repository is named, with how to choose one ({plain})");
        var missing = Message(new(SourceKind.WorkingTree, "~/missing", "HEAD") { Host = "box" });
        h.Check(missing.Contains("missing", StringComparison.Ordinal) && !missing.Contains("timed out", StringComparison.Ordinal),
            $"a missing remote folder fails with Git's message ({missing})");
    }

    /// <summary>The same files, statuses, counts and shown text.</summary>
    static bool Same(DiffDocument a, DiffDocument b) =>
        a.Files.Length == b.Files.Length && a.Files.Zip(b.Files).All(p => p.First.Path == p.Second.Path && p.First.Status == p.Second.Status &&
            p.First.OldPath == p.Second.OldPath && p.First.Additions == p.Second.Additions && p.First.Deletions == p.Second.Deletions &&
            p.First.Binary == p.Second.Binary && p.First.NewLines.SequenceEqual(p.Second.NewLines) && p.First.OldLines.SequenceEqual(p.Second.OldLines));

    static DiffDocument Load(DiffRequest request) => Loader.Load(request, false, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>An ssh stand-in: skips options, logs host and command, then runs the command as sshd would.</summary>
    static string FakeSsh(string scratch, string home, string log)
    {
        var path = Path.Combine(scratch, "ssh");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            while [ $# -gt 0 ]; do
              case "$1" in
                -o|-p|-F|-i|-l|-S) shift 2 ;;
                --) shift; break ;;
                -*) shift ;;
                *) break ;;
              esac
            done
            host="$1"; shift
            printf '%s\t%s\n' "$host" "$*" >> '{{log}}'
            if [ "$host" = down ]; then echo "ssh: connect to host down port 22: Connection refused" >&2; exit 255; fi
            export HOME='{{home}}' GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
            cd "$HOME" || exit 1
            exec /bin/sh -c "$*"
            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
