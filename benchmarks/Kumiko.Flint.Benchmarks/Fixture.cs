using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Flint.Git;
using Flint.Services;

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "kumiko-flint-perf-" + Guid.NewGuid().ToString("N"));
    public IProcessRunner Runner
    {
        get;
    }
    public IGitService Git
    {
        get;
    }
    public string History => Path.Combine(Root, "history-10000");
    public string Small => Path.Combine(Root, "small");
    public string Diff => Path.Combine(Root, "large-diff");
    public string Changes => Path.Combine(Root, "changes-5000");
    public Fixture()
    {
        Directory.CreateDirectory(Root);
        Runner = new ProcessRunner(new Dictionary<string, string>
        {
            ["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "no-global-config"),
            ["GIT_CONFIG_NOSYSTEM"] = "1"
        });
        Git = new GitService(Runner, Path.Combine(Root, "state"));
        Import(History, 10000, "first line\n");
        Import(Small, 25, "small repository\n");
        Import(Diff, 1, string.Join('\n', Enumerable.Range(0, 5000).Select(i => "old line " + i)) + "\n");
        File.WriteAllText(Path.Combine(Diff, "benchmark.txt"), string.Join('\n', Enumerable.Range(0, 5000).Select(i => "needle new line " + i)) + "\n");
        Import(Changes, 1, "tracked file\n");
        var directory = Path.Combine(Changes, "files");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 5000; i++)
            File.WriteAllText(Path.Combine(directory, $"file-{i:D5}.txt"), "working change\n");
    }
    void Import(string directory, int commits, string content)
    {
        Directory.CreateDirectory(directory);
        Run(directory, "init", "-b", "main");
        Run(directory, "config", "user.name", "Flint benchmark");
        Run(directory, "config", "user.email", "benchmark@example.test");
        Run(directory, "config", "core.hooksPath", Path.Combine(Root, "no-hooks"));
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        start.ArgumentList.Add("fast-import");
        start.ArgumentList.Add("--quiet");
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "no-global-config");
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        var writer = process.StandardInput;
        writer.Write($"blob\nmark :1\ndata {Encoding.UTF8.GetByteCount(content)}\n{content}\n");
        for (var index = 0; index < commits; index++)
        {
            var subject = "Benchmark history " + index;
            writer.Write($"commit refs/heads/main\nmark :{index + 2}\ncommitter Flint benchmark <benchmark@example.test> {1792065600 + index} +0000\ndata {subject.Length}\n{subject}\n");
            if (index > 0)
                writer.Write($"from :{index + 1}\n");
            else
                writer.Write("M 100644 :1 benchmark.txt\n");
            writer.Write('\n');
        }
        writer.Write("done\n");
        writer.Close();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException(error.GetAwaiter().GetResult());
        output.GetAwaiter().GetResult();
        Run(directory, "reset", "--hard", "HEAD");
    }
    public void Run(string path, params string[] args) => Runner.Run("git", path, args, CancellationToken.None).GetAwaiter().GetResult();
    public Snapshot Load(string path, int limit = 4000) => Git.Load(path, limit, CancellationToken.None).GetAwaiter().GetResult();
    public Flint.RepoTab Tab(Snapshot snapshot)
    {
        var detail = Git.Detail(snapshot.Path, snapshot.Commits[0].Sha, CancellationToken.None).GetAwaiter().GetResult();
        var patch = Git.Patch(snapshot.Path, detail, null, false, CancellationToken.None).GetAwaiter().GetResult();
        return new(snapshot, Sha: detail.Sha, Detail: detail, Patch: patch, Rows: Flint.Diff.Parse(patch));
    }
    public void Dispose() => Directory.Delete(Root, true);
}

/// <summary>Benchmarks never read gh state or launch applications.</summary>
sealed class NoAccounts : IGitHubAccounts
{
    public Task<ImmutableArray<GhAccount>> List(CancellationToken token) => Task.FromResult(ImmutableArray<GhAccount>.Empty);
}
sealed class NoLauncher : ILauncher
{
    public Task Launch(LaunchRequest request, CancellationToken token) => Task.CompletedTask;
}
