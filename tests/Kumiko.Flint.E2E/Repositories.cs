using System.Diagnostics;
using System.Text;

/// <summary>
/// Disposable repositories under the temp folder. "Remotes" are local bare repositories in the same folder; no
/// network remote, user repository or global configuration is touched.
/// </summary>
sealed class Repositories : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "kumiko-flint-" + Guid.NewGuid().ToString("N"));
    public string Repo => Path.Combine(Root, "flint-fixture");
    public string History => Path.Combine(Root, "long-history");
    public string Empty => Path.Combine(Root, "unborn");
    public string Push => Path.Combine(Root, "push-fixture");
    public string Origin => Path.Combine(Root, "origin.git");
    public string Upstream => Path.Combine(Root, "upstream.git");
    public string Pages => Path.Combine(Root, "pagination");
    public string Hooks => Path.Combine(Root, "failing-hooks");
    public Repositories()
    {
        Init(Repo);
        Root = Path.GetDirectoryName(Git(Repo, "rev-parse", "--show-toplevel").Trim())!;
        Directory.CreateDirectory(Path.Combine(Repo, "src"));
        File.WriteAllText(Path.Combine(Repo, "src/hello world.txt"), "hello\nold line\n");
        File.WriteAllText(Path.Combine(Repo, "words.txt"), "alpha beta gamma\nkeep\n");
        Git(Repo, "add", ".");
        Git(Repo, "commit", "-m", "Initial history");
        Git(Repo, "switch", "-c", "feature/search");
        File.WriteAllText(Path.Combine(Repo, "feature.txt"), "feature line\n");
        Git(Repo, "add", ".");
        Git(Repo, "commit", "-m", "Add search feature");
        Git(Repo, "switch", "main");
        File.WriteAllText(Path.Combine(Repo, "README.md"), "Flint fixture\n");
        Git(Repo, "add", ".");
        Git(Repo, "commit", "-m", "Document client");
        Git(Repo, "merge", "--no-ff", "feature/search", "-m", "Merge feature");
        Git(Repo, "tag", "-a", "v1.0", "-m", "Fixture release");
        File.WriteAllText(Path.Combine(Repo, "notes.txt"), "committed by someone else\n");
        Git(Repo, "add", ".");
        Git(Repo, ["commit", "-m", "Commit applied by maintainer"], ("GIT_COMMITTER_NAME", "Maintainer Bot"), ("GIT_COMMITTER_EMAIL", "bot@example.test"));
        File.WriteAllText(Path.Combine(Repo, "src/hello world.txt"), "hello\nneedle NEEDLE needle\n");
        File.WriteAllText(Path.Combine(Repo, "words.txt"), "alpha delta gamma\nkeep\n");
        File.WriteAllText(Path.Combine(Repo, "new ü.txt"), "new file\n");
        Init(Empty);
        File.WriteAllText(Path.Combine(Empty, "new ü.txt"), "first file\n");
        Init(History);
        File.WriteAllText(Path.Combine(History, "history.txt"), "history fixture\n");
        Git(History, "add", ".");
        Git(History, "commit", "-m", "History 0");
        var parent = Git(History, "rev-parse", "HEAD").Trim();
        var tree = Git(History, "rev-parse", "HEAD^{tree}").Trim();
        for (var index = 1; index <= 80; index++)
            parent = Git(History, "commit-tree", tree, "-p", parent, "-m", "History " + index).Trim();
        Git(History, "update-ref", "refs/heads/main", parent);
        // Push fixture: main tracks a remote other than origin; origin exists for first pushes.
        Git(Root, "init", "-q", "--bare", "-b", "main", Origin);
        Git(Root, "init", "-q", "--bare", "-b", "main", Upstream);
        Init(Push);
        File.WriteAllText(Path.Combine(Push, "push.txt"), "pushed\n");
        Git(Push, "add", ".");
        Git(Push, "commit", "-m", "Push fixture");
        Git(Push, "remote", "add", "origin", Origin);
        Git(Push, "remote", "add", "upstream", Upstream);
        Git(Push, "push", "-q", "-u", "upstream", "main");
        Git(Push, "push", "-q", "origin", "main:remote-only");
        Git(Push, "fetch", "-q", "origin");
        Directory.CreateDirectory(Path.Combine(Push, "picks"));
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
            File.WriteAllText(Path.Combine(Push, "picks", name), name + "\n");
        Directory.CreateDirectory(Hooks);
        var hook = Path.Combine(Hooks, "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\necho 'lint-staged: checking 1 file'\nfor i in 1 2 3 4 5; do echo \"hook line $i\"; done\necho 'hook line final' >&2\nexit 1\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Import(Pages, 4500);
    }
    static void Init(string path)
    {
        Directory.CreateDirectory(path);
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.name", "Flint Tester");
        Git(path, "config", "user.email", "flint@example.test");
        Git(path, "config", "commit.gpgsign", "false");
        Git(path, "config", "core.hooksPath", Path.Combine(path, "no-hooks"));
    }
    /// <summary>A linear history through fast-import, faster than thousands of commit processes.</summary>
    static void Import(string path, int commits)
    {
        Init(path);
        var input = new StringBuilder("blob\nmark :1\ndata 6\npages\n\n");
        for (var index = 0; index < commits; index++)
        {
            var subject = "Page " + index;
            input.Append($"commit refs/heads/main\nmark :{index + 2}\ncommitter Flint Tester <flint@example.test> {1792065600 + index} +0000\ndata {subject.Length}\n{subject}\n");
            input.Append(index > 0 ? $"from :{index + 1}\n\n" : "M 100644 :1 pages.txt\n\n");
        }
        Git(path, ["fast-import", "--quiet"], input: input.ToString());
        Git(path, "reset", "-q", "--hard", "main");
    }
    public static string Git(string path, params string[] args) => Git(path, args, []);
    public static string Git(string path, string[] args, params (string Key, string Value)[] environment) => Git(path, args, null, environment);
    static string Git(string path, string[] args, string? input, params (string Key, string Value)[] environment)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input is not null };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(path, "no-global-config");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_AUTHOR_DATE"] = "2026-10-15T12:00:00Z";
        start.Environment["GIT_COMMITTER_DATE"] = "2026-10-15T12:00:00Z";
        foreach (var (key, value) in environment)
            start.Environment[key] = value;
        using var process = Process.Start(start)!;
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        var error = process.StandardError.ReadToEndAsync();
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.GetAwaiter().GetResult());
        return text;
    }
    public void Dispose() => Directory.Delete(Root, true);
}
