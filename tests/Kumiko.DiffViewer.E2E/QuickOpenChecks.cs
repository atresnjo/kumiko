using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Project discovery, worktrees and the ⌘K palette against repositories in a temporary folder. The store's state
/// folder and roots are temporary too; the user's home folder, recent list and cache are never read.
/// </summary>
internal static class QuickOpenChecks
{
    public static void Run(Harness h)
    {
        // Git reports real paths; macOS's temporary folder sits behind the /var symbolic link.
        var temp = Path.GetTempPath();
        if (OperatingSystem.IsMacOS() && temp.StartsWith("/var/", StringComparison.Ordinal))
            temp = "/private" + temp;
        var scratch = Path.Combine(temp, "kumiko-diff-e2e", "projects-" + Environment.ProcessId);
        if (Directory.Exists(scratch))
            Directory.Delete(scratch, true);
        var code = Path.Combine(scratch, "code");
        var elsewhere = Path.Combine(scratch, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        try
        {
            using var alpha = new Repository("alpha", code);
            alpha.Write("src/app.ts", "export const value = 1;\n");
            alpha.Commit("initial");
            alpha.Git("worktree", "add", "-q", "-b", "feature", Path.Combine(elsewhere, "alpha-feature"));
            alpha.Git("worktree", "add", "-q", "--detach", Path.Combine(elsewhere, "alpha-detached"));
            var head = alpha.Git("rev-parse", "--short=7", "HEAD").Trim();
            using var beta = new Repository("beta", Path.Combine(code, "nested"));
            using var hidden = new Repository("gamma", Path.Combine(code, ".cache"));
            using var dependency = new Repository("delta", Path.Combine(code, "node_modules"));
            using var outside = new Repository("epsilon", Path.Combine(scratch, "linked"));
            Directory.CreateSymbolicLink(Path.Combine(code, "link"), Path.Combine(scratch, "linked"));

            var walked = Projects.Walk([code], CancellationToken.None);
            h.Check(walked.Select(p => Path.GetRelativePath(code, p)).SequenceEqual(["alpha", "nested/beta"]),
                $"discovery skips hidden, dependency and symlinked folders ({string.Join(", ", walked)})");
            var projects = Projects.Expand(walked);
            h.Check(projects.Select(p => (p.Name, p.Branch)).SequenceEqual([("alpha", "main"), ("alpha/alpha-detached", head), ("alpha/alpha-feature", "feature"), ("beta", "main")]),
                $"worktrees outside the roots are listed after their repository with their own branch ({string.Join(", ", projects.Select(p => p.Name + "@" + p.Branch))})");
            h.Check(projects.Where(p => p.Worktree).All(p => p.Main == projects[0].Path), "worktrees belong to their main checkout");
            var feature = projects.Single(p => p.Branch == "feature");
            h.Check(Projects.Expand([feature.Path]).Select(p => p.Name).SequenceEqual(["alpha", "alpha/alpha-detached", "alpha/alpha-feature"]),
                "a worktree found on its own brings its repository and siblings");

            var store = new ProjectStore(Path.Combine(scratch, "state"), scratch, [code]);
            h.Check(ProjectFiles.Cached(store).IsEmpty, "an empty state folder lists no cached projects");
            ProjectFiles.SaveRecent(store, [beta.Root]);
            h.Check(ProjectFiles.Recent(store).SequenceEqual([beta.Root]), "the recent list is saved and read back");
            h.Check(ProjectFiles.Cached(store).Select(p => p.Name).SequenceEqual(["beta"]), "before a scan the cache holds recent projects");
            var scanned = ProjectFiles.Scan(store, CancellationToken.None);
            h.Check(scanned.SequenceEqual(projects) && ProjectFiles.Cached(store).SequenceEqual(projects), "a scan is cached for the next launch");

            Palette(h, store, alpha, projects, feature);
            Arguments(h, alpha, scratch);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    static void Palette(Harness h, ProjectStore store, Repository alpha, System.Collections.Immutable.ImmutableArray<Project> projects, Project feature)
    {
        alpha.Write("src/app.ts", "export const value = 2;\n");
        File.WriteAllText(Path.Combine(feature.Path, "src", "app.ts"), "export const value = 3;\nexport const feature = true;\n");
        using var program = Harness.Open(new(SourceKind.WorkingTree, alpha.Root, "HEAD"), m => m with
        {
            Store = store,
            Recent = [],
            Cached = true
        });
        using var t = new Tester(program, new(1280, 820, DevMode: true));
        t.Context.ReduceMotion = true;
        Model M() => program.Model;
        program.Send(new Msg.ProjectsFound(projects, true));
        t.Settle();
        program.Send(new Msg.Move(1));
        t.Settle();
        var cursor = M().Cursor;

        t.Keystroke('k', Modifiers.Primary);
        t.Settle();
        var quick = M().Quick;
        h.Check(quick is not null && t.Context.Focus != 0, "Command+K opens the palette with its field focused");
        h.Check(quick!.Items.Take(3).All(i => i.Group == "Worktrees") && quick.Items[0].Detail.StartsWith("current", StringComparison.Ordinal),
            "the open project's worktrees come first, the current one marked");
        h.Check(t.HasText("feature") && t.HasText(projects[1].Branch), "worktree rows show their branch or detached commit");
        h.Artifact(t, "projects");
        t.TypeText("feat");
        t.Settle();
        h.Check(M().Quick!.Items[0].Label == "alpha/alpha-feature" && M().Cursor == cursor, "typing ranks the worktree first and leaves the diff cursor alone");
        h.Artifact(t, "projects-query");
        var generation = M().Generation;
        var old = M().Document;
        t.Key(Key.Enter);
        h.Check(M() is { Quick: null, Loading: true, Document.Files.IsEmpty: true, Search: null } m && m.Request.Path == feature.Path && m.Generation == generation + 1,
            "Enter opens the worktree, dropping the previous diff");
        var document = Loader.Load(M().Request, false, CancellationToken.None).GetAwaiter().GetResult();
        program.Send(new Msg.Loaded(M().Generation - 1, old));
        t.Settle();
        h.Check(!ReferenceEquals(M().Document, old), "a late result from the previous project is ignored");
        program.Send(new Msg.Loaded(M().Generation, document));
        t.Settle();
        h.Check(M().Document.Files is [{ Additions: 2 }] && M().Recent.SequenceEqual([document.Root]), "the worktree's diff loads and becomes the most recent project");
        program.Send(new Msg.OpenProject(alpha.Root));
        t.Settle();
        program.Send(new Msg.Loaded(M().Generation, Loader.Load(M().Request, false, CancellationToken.None).GetAwaiter().GetResult()));
        t.Settle();
        h.Check(M().Recent.Length == 2 && M().Recent[0] == M().Document.Root && M().Recent[1] == document.Root, "recent projects are most recent first, without repeats");

        t.Keystroke('P', Modifiers.Primary | Modifiers.Shift);
        t.Settle();
        h.Check(M().Quick is { Query: ">" } commands && commands.Items.All(i => i.Group == "Commands"), "Shift+Command+P lists commands only");
        t.TypeText("split");
        t.Settle();
        h.Artifact(t, "commands");
        t.Key(Key.Enter);
        h.Check(M() is { Quick: null, Mode: ViewMode.Split }, "a command row runs and closes the palette");
        t.Keystroke('o', Modifiers.Primary);
        t.Settle();
        h.Check(M().Quick is not null, "Command+O opens the palette");
        t.Key(Key.Escape);
        h.Check(M().Quick is null, "Escape closes the palette");
    }

    static void Arguments(Harness h, Repository alpha, string scratch)
    {
        h.Check(Sources.Arguments.Parse([alpha.Root], "/") is { Kind: SourceKind.WorkingTree, Base: "HEAD" } folder && folder.Path == alpha.Root,
            "a folder argument names the repository");
        h.Check(Sources.Arguments.Parse([alpha.Root, "main"], "/") is { Kind: SourceKind.WorkingTree, Base: "main" } revision && revision.Path == alpha.Root,
            "a folder argument may be followed by a revision");
        h.Check(!Sources.Arguments.InRepository(new(SourceKind.WorkingTree, scratch)) && Sources.Arguments.InRepository(new(SourceKind.WorkingTree, Path.Combine(alpha.Root, "src"))),
            "a folder outside any checkout is recognised before Git runs");
    }
}
