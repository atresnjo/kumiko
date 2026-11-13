using System.Text.Json;
using Flint;
using Flint.Services;
using Kumiko.UI;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/flint");
Directory.CreateDirectory(output);
if (args.Contains("--rtl-edges-only", StringComparer.Ordinal))
{
    PhysicalEdges.Run(output);
    return;
}
using var fixture = new Repositories();
var stateDirectory = Path.Combine(fixture.Root, "state");
var runner = new ProcessRunner(new Dictionary<string, string>
{
    ["GIT_CONFIG_GLOBAL"] = Path.Combine(fixture.Root, "no-global-config"),
    ["GIT_CONFIG_NOSYSTEM"] = "1"
});
var git = new CountingGit(new GitService(runner, stateDirectory, new RepositoryCache(stateDirectory)));
var launcher = new RecordingLauncher();
var accounts = new FixedAccounts();
var registrations = new ServiceCollection();
registrations.AddSingleton<IGitService>(git);
registrations.AddSingleton<IGitHubAccounts>(accounts);
registrations.AddSingleton<ILauncher>(launcher);
registrations.AddFlint(options =>
{
    options.StateDirectory = stateDirectory;
    options.DiscoveryRoots = [fixture.Root];
});
using var services = registrations.BuildServiceProvider(new ServiceProviderOptions() { ValidateScopes = true, ValidateOnBuild = true });
FlintModel Restore(bool offscreen = false)
{
    var scope = services.CreateAsyncScope();
    try
    {
        return scope.ServiceProvider.GetRequiredService<FlintStartup>().Restore(fixture.Root, offscreen);
    }
    finally
    {
        scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
using var program = FlintApp.CreateProgram(Restore(), services);
if (args.Contains("--memory-only", StringComparer.Ordinal))
    program.RetainHistory = false;
try
{
    using var tester = new Tester(program, new(1440, 900, DevMode: true));
    tester.Context.ReduceMotion = true;
    var h = new Harness(program, tester, output);
    Parity.Services = services;
    void Check(bool value, string name) => h.Check(value, name);
    if (args.Contains("--notifications-only", StringComparer.Ordinal) || args.Contains("--memory-only", StringComparer.Ordinal))
    {
        h.OpenRepository(fixture.Repo);
        if (args.Contains("--notifications-only", StringComparer.Ordinal))
            Notifications.Run(h, fixture);
        else
            MemoryRetention.Run(h, fixture, output, services);
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
        Console.WriteLine($"{h.Checks.Count} Flint focused checks passed; artifacts: {output}");
        return;
    }
    IconSubset.Run(output, Check);
    ToolPath.Run(fixture, Check);
    if (args.Contains("--tool-path-only", StringComparer.Ordinal))
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
        Console.WriteLine($"{h.Checks.Count} Flint tool path checks passed; artifacts: {output}");
        return;
    }
    OpenLatency.Run(fixture, runner, Check);
    if (args.Contains("--open-latency-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"{h.Checks.Count} Flint open latency checks passed");
        return;
    }
    string Read(string path) => File.ReadAllText(Path.Combine(fixture.Repo, path));

    CurrentBranch.Run(h, fixture);
    if (args.Contains("--current-branch-only", StringComparer.Ordinal))
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
        Console.WriteLine($"{h.Checks.Count} Flint current branch checks passed; artifacts: {output}");
        return;
    }

    Remotes.Run(h, fixture);
    if (args.Contains("--remotes-only", StringComparer.Ordinal))
    {
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
        Console.WriteLine($"{h.Checks.Count} Flint remote checks passed; artifacts: {output}");
        return;
    }

    h.OpenRepository(Path.Combine(fixture.Repo, "src"));
    Notifications.Run(h, fixture);
    Check(h.Tab.Snapshot.Path == fixture.Repo, "Subdirectory resolves to repository root");
    Check(tester.ContainsText("Merge feature"), "Real merge history is visible");
    if (args.Contains("--resize-only", StringComparer.Ordinal))
    {
        ResizeStability.Run(h, output);
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
        Console.WriteLine($"{h.Checks.Count} Flint resize checks passed; artifacts: {output}");
        return;
    }
    ResizeStability.Run(h, output);
    h.Click("Commit: Merge feature");
    Check(h.Tab.Detail?.Parents.Length == 2, "Merge detail includes both parents");
    h.Click("Changes");
    Check(h.Tab.Patch.Contains("feature line", StringComparison.Ordinal), "Merge patch compares first parent");
    FilePaneResize.Run(h);
    CommitFiles.Run(h, services, output);
    SidebarLayout.Run(h, fixture);
    h.Shot("history");
    h.Click("Commit");
    h.Click("Commit: Commit applied by maintainer");
    Check(tester.ContainsText("Committed by Maintainer Bot"), "Commit detail shows a separate committer identity and time");
    h.Click("Local Changes");
    h.Click("Unstaged: src/hello world.txt");
    Check(tester.ContainsText("needle"), "Unstaged file opens its real diff");
    WorkingPaneResize.Run(h);
    SidebarMotion.Run(h, output);
    h.Click("Find");
    h.Type("Find in diff", "needle");
    Check(tester.ContainsText("1 of 3"), "Search counts repeated case-insensitive code matches only");
    h.Click("Next");
    Check(tester.ContainsText("2 of 3"), "Search advances to the next match");
    h.Click("Previous");
    h.Shot("changes-search");
    h.Click("Done");
    LongLines.Run(h, fixture);
    h.Click("Unstaged: words.txt");
    var replaced = h.Tab.Lines.Single(l => l.Kind == '+' && l.Text.StartsWith("alpha", StringComparison.Ordinal));
    Check(replaced.Words.SequenceEqual([6, 5]), "Word diff marks only the replaced word in a paired line");
    Check(h.Tab.Lines.Single(l => l.Kind == '-' && l.Text.StartsWith("alpha", StringComparison.Ordinal)).Words.SequenceEqual([6, 4]), "Word diff marks the removed word");
    h.Click("Unstaged: src/hello world.txt");
    Check(h.Tab.Lines.Where(l => l.Kind is '+' or '-').All(l => l.Words.IsEmpty), "Rewritten lines without a shared word get no word emphasis");
    h.Click("Stage File");
    Check(h.Tab.Snapshot.Staged.Any(f => f.Path == "src/hello world.txt"), "Stage File updates Git index");
    h.Click("Staged: src/hello world.txt");
    h.Click("Unstage File");
    Check(h.Tab.Snapshot.Unstaged.Any(f => f.Path == "src/hello world.txt"), "Unstage preserves working copy");
    h.Click("Stage All");
    h.Type("Commit subject", "Port verified through UI");
    h.Type("Description", "Real Git, injected services and Kumiko input.");
    h.Click(h.CommitLabel);
    Check(Repositories.Git(fixture.Repo, "log", "-1", "--format=%s").Trim() == "Port verified through UI", "Commit writes entered subject to Git");
    Check(h.Tab.Snapshot.Staged.IsEmpty, "Successful commit clears staged files");
    Check(h.Tab.Subject == "", "Successful commit clears the draft");
    Check(tester.ContainsText("Committed: Port verified through UI"), "Successful commit reports a toast");
    h.Shot("committed");
    var parentBeforeAmend = Repositories.Git(fixture.Repo, "rev-parse", "HEAD~1").Trim();
    tester.Get(Role.Checkbox, "Amend").Click();
    h.Idle();
    Check(h.Tab.Subject == "Port verified through UI", "Amend preloads the prior commit message");
    h.Type("Commit subject", "Amended through Flint");
    h.Click("Amend");
    Check(Repositories.Git(fixture.Repo, "log", "-1", "--format=%s").Trim() == "Amended through Flint", "Amend rewrites the previous commit message");
    Check(Repositories.Git(fixture.Repo, "rev-parse", "HEAD~1").Trim() == parentBeforeAmend, "Amend replaces HEAD instead of adding a commit");
    h.Click("New Branch");
    h.Type("Branch name", "test/from-flint");
    h.Press(Key.Enter);
    Check(Repositories.Git(fixture.Repo, "branch", "--show-current").Trim() == "test/from-flint", "New branch creates and checks out branch");
    File.AppendAllText(Path.Combine(fixture.Repo, "src/hello world.txt"), "stash me\n");
    h.Press(Key.R, Modifiers.Primary);
    h.Click("Stash");
    Check(h.Tab.Snapshot.Stashes.Length == 1, "Stash saves working changes");
    h.DoubleClick(h.NameStarting(Role.TreeItem, "Stash: ")!, Role.TreeItem);
    Check(Read("src/hello world.txt").Contains("stash me", StringComparison.Ordinal), "Double-clicking a stash pops it");
    h.Click("Local Changes");
    h.Click("Unstaged: src/hello world.txt");
    h.Press(Key.Backspace);
    Check(h.Model.Pending?.Title == "Revert src/hello world.txt?", "Backspace on a focused file asks before reverting");
    h.Click("Cancel");
    Check(Read("src/hello world.txt").Contains("stash me", StringComparison.Ordinal), "Cancel revert preserves changes");
    h.Click("Unstaged: src/hello world.txt");
    h.Press(Key.Delete);
    h.Click("Revert");
    Check(!Read("src/hello world.txt").Contains("stash me", StringComparison.Ordinal), "Confirmed revert restores index version");
    Check(Directory.EnumerateFiles(Path.Combine(stateDirectory, "trash"), "*", SearchOption.AllDirectories).Any(), "Revert keeps recoverable backup");
    var worktree = Path.Combine(fixture.Root, "worktree-fixture");
    Repositories.Git(fixture.Repo, "worktree", "add", "-b", "test/worktree", worktree);
    for (var i = 0; i < 200; i++)
        File.WriteAllText(Path.Combine(worktree, $"bulk-{i}.txt"), "bulk\n");
    h.Press(Key.R, Modifiers.Primary);
    h.Click("Worktree: worktree-fixture", Role.TreeItem);
    Check(h.Tab.Snapshot.Path == worktree, "A single click opens a sidebar worktree in its own tab");
    h.Click("Repository: flint-fixture");
    h.Menu("Worktree: worktree-fixture", "Delete Worktree…", Role.TreeItem);
    Check(h.Model.Pending?.Message.Contains("200 uncommitted changes", StringComparison.Ordinal) == true, "Worktree deletion reports its uncommitted changes first");
    h.Click("Cancel");
    Check(Directory.Exists(worktree), "Cancelling worktree removal preserves files");
    h.Menu("Worktree: worktree-fixture", "Delete Worktree…", Role.TreeItem);
    h.Click("Delete Worktree");
    Check(!Directory.Exists(worktree), "Confirmed worktree deletion removes the folder immediately");
    Check(!Repositories.Git(fixture.Repo, "worktree", "list", "--porcelain").Contains(worktree, StringComparison.Ordinal), "Worktree metadata is pruned");
    Check(!h.Model.Workspaces.Any(w => w.Repositories.Contains(worktree)), "Deleted worktree is removed from every workspace tab set");
    h.WaitFor(() => !Directory.EnumerateDirectories(fixture.Root, ".flint-trash-*").Any(), "Renamed worktree files are deleted in the background");
    var loadsBefore = git.Loads;
    var statusesBefore = git.Statuses;
    File.WriteAllText(Path.Combine(fixture.Repo, "watched.txt"), "native file notification\n");
    h.WaitFor(() => tester.ContainsText("watched.txt"), "Native watcher refreshes files without explicit refresh or idle polling", 5);
    h.Idle();
    Check(git.Statuses > statusesBefore && git.Loads == loadsBefore, "A working-tree edit runs status only, not a full snapshot load");
    h.Click("Unstaged: watched.txt");
    h.Menu("Unstaged: watched.txt", "Copy Path");
    Check(tester.Clipboard == "watched.txt", "Context menu copies selected path to Tester private clipboard");
    h.Menu("Unstaged: watched.txt", "Reveal in Finder");
    h.Menu("Unstaged: watched.txt", "Open in Zed");
    var launches = launcher.Requests.ToArray();
    Check(launches.Length == 2 && launches[0].Kind == LaunchKind.Reveal && launches[1].Kind == LaunchKind.Editor
        && launches.All(l => l.Paths.SequenceEqual([Path.Combine(fixture.Repo, "watched.txt")])), "File actions go to the injected launcher with absolute paths");
    h.Click("Unstaged: watched.txt");
    h.Press(Key.Backspace);
    h.Click("Revert");
    Check(!File.Exists(Path.Combine(fixture.Repo, "watched.txt")), "Reverting untracked file removes it after backup");
    Parity.Run(h, fixture, launcher);
    h.OpenRepository(fixture.Empty);
    Check(h.Model.Workspace.Repositories.Length == 3, "Opening another repository adds a tab");
    h.Click("Local Changes");
    h.Click("Stage All");
    h.Click("Unstage All");
    Check(h.Tab.Snapshot.Staged.IsEmpty, "Unstage all works on unborn HEAD");
    h.Click("Stage All");
    h.Type("Commit subject", "Initial commit");
    h.Click(h.CommitLabel);
    Check(h.Tab.Snapshot.Commits.Length == 1, "Unborn repository can create its first commit");
    h.Click("All Commits");
    h.Click("Commit: Initial commit");
    Check(h.Tab.Detail?.Files.Any(f => f.Path == "new ü.txt") == true, "Root commit preserves Unicode file name");
    h.OpenRepository(fixture.Repo);
    Check(h.Model.Workspace.Repositories.Length == 3, "Reopening repository selects existing tab");
    RecentTabs.Run(h, output);
    Workspaces.Run(h, fixture);
    h.Click("Quick Launch");
    h.Type("Quick Launch", "flnt");
    h.Shot("quick-launch");
    h.Press(Key.Escape);
    OpenFeedback.Run(h, git, fixture);
    if (args.Contains("--open-feedback-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"{h.Checks.Count} Flint checks passed through open feedback");
        return;
    }
    h.OpenRepository(Path.Combine(fixture.Root, "missing"));
    Check(!h.Model.Busy && h.Model.Error.Length > 0, "Invalid repository reports recoverable error");
    h.Press(Key.Escape);
    h.Press(Key.R, Modifiers.Primary);
    Check(h.Model.Error.Length == 0, "Refresh recovers from failed operation");
    h.Idle();
    var restored = services.GetRequiredService<IWorkspaceStorage>().Load()!;
    var warm = Restore();
    Check(warm.Workspaces.Select(workspace => (workspace.Id, workspace.Name, workspace.Active, workspace.Account)).SequenceEqual(
        restored.Workspaces.Select(workspace => (workspace.Id, workspace.Name, workspace.Active, workspace.Account)))
        && warm.Workspaces.SelectMany(workspace => workspace.Repositories).SequenceEqual(restored.Workspaces.SelectMany(workspace => workspace.Repositories)) && warm.Recent.SequenceEqual(restored.Recent)
        && warm.Repositories.ContainsKey(fixture.Repo), "DI startup restores persisted workspaces and repository cache using shared configuration");
    var isolated = Restore(offscreen: true);
    Check(isolated.Repositories.IsEmpty && isolated.Known.IsEmpty && !isolated.Watching
        && isolated.Workspaces.Length == 1 && isolated.Workspace.Name == "Default" && isolated.Workspace.Repositories.IsEmpty, "Offscreen DI startup skips persisted state, discovery and watchers");
    Check(restored.Workspaces.Any(w => w.Name == "Review") && restored.Recent.Contains(fixture.Repo), "Injected storage persists workspaces and recent repositories");
    using (var cold = FlintApp.CreateProgram(FlintApp.Init with
    {
        Workspaces = restored.Workspaces,
        ActiveWorkspace = restored.ActiveWorkspace,
        Watching = false
    }, services))
    using (var coldTester = new Tester(cold, new(1440, 900)))
    {
        try
        {
            coldTester.Get(Role.Button, "Repository: unborn").Click();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            do
            {
                coldTester.Settle();
                if (cold.PendingCommands == 0 && cold.Model.Current?.Detail is not null)
                    break;
                Thread.Sleep(10);
            } while (DateTime.UtcNow < deadline);
            Check(cold.Model.Current?.Detail?.Subject == "Initial commit", "Cold persisted tab loads its snapshot and initial detail on activation");
        }
        finally
        {
            cold.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
    Parity.History(h, fixture);
    h.Click("Close repository: " + fixture.History);
    h.Shot("final");
    tester.Resize(1000, 700);
    tester.Settle();
    h.Shot("compact");
    using (var replay = FlintApp.CreateProgram(FlintApp.Init, services))
    {
        // The runtime's recorded events restore the UI without executing Git effects.
        var log = Path.Combine(output, "session.jsonl");
        program.Save(log);
        replay.Replay(log);
        using var replayTester = new Tester(replay, new(1440, 900));
        replayTester.Screenshot(Path.Combine(output, "replay.png"));
        Check(replay.Model.Workspace.Repositories.SequenceEqual(program.Model.Workspace.Repositories), "Recorded session replays repository tabs");
    }
    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(h.Checks, ReportJson.Default.ListString));
    Console.WriteLine($"{h.Checks.Count} Flint E2E checks passed; artifacts: {output}");
}
finally
{
    program.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
partial class ReportJson : System.Text.Json.Serialization.JsonSerializerContext;
