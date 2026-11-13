using System.Diagnostics;
using System.Security.Cryptography;
using DiffViewer;
using DiffViewer.Sources;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>Collects checks, timings and artifact hashes for report.json.</summary>
internal sealed class Harness(string output)
{
    public string Output { get; } = output;
    public List<string> Checks { get; } = [];
    public SortedDictionary<string, double> Timings { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Hashes { get; } = new(StringComparer.Ordinal);

    public void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("FAILED: " + name);
        Checks.Add(name);
    }

    public void Timing(string name, double milliseconds)
    {
        Timings[name] = Math.Round(milliseconds, 3);
        Console.WriteLine($"{name}: {milliseconds:F3}");
    }

    /// <summary>Writes PNG, tree and warnings for a settled frame and requires a warning-free scene.</summary>
    public void Artifact(Tester tester, string name)
    {
        tester.Settle();
        var stem = Path.Combine(Output, name);
        tester.Screenshot(stem + ".png");
        File.WriteAllText(stem + ".txt", tester.Dump());
        tester.WriteWarnings(stem + ".warnings.json");
        var warnings = tester.Context.Warnings;
        Check(warnings.Count == 0, $"{name} has no layout, identity or contrast warnings" +
            (warnings.Count == 0 ? "" : $" (first: {warnings[0].Code} {warnings[0].Message} at {warnings[0].Path})"));
        Hashes[name] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stem + ".png")));
    }

    /// <summary>Loads a request synchronously into a fresh program, as the app does before its first frame.</summary>
    public static Program<Model, Msg> Open(DiffRequest request, Func<Model, Model>? adjust = null, KumikoServices? services = null)
    {
        var document = Loader.Load(request, false, CancellationToken.None).GetAwaiter().GetResult();
        var model = DiffApp.Update(DiffApp.Init with
        {
            Request = request
        }, new Msg.Loaded(0, document)).Item1;
        var program = DiffApp.CreateProgram(adjust is null ? model : adjust(model), services ?? new KumikoServices().Add<IFullScreen>(new NoFullScreen()));
        program.RetainHistory = false;
        return program;
    }
}

/// <summary>Temporary Git repositories with isolated configuration; nothing reads the user's Git settings.</summary>
internal sealed class Repository : IDisposable
{
    public string Root
    {
        get;
    }

    /// <summary>A fresh repository, by default in a per-process temporary folder; parent places it elsewhere.</summary>
    public Repository(string name, string? parent = null)
    {
        Root = parent is null ? Path.Combine(Path.GetTempPath(), "kumiko-diff-e2e", name + "-" + Environment.ProcessId) : Path.Combine(parent, name);
        if (Directory.Exists(Root))
            Directory.Delete(Root, true);
        Directory.CreateDirectory(Root);
        Git("init", "-q", "-b", "main");
    }

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo(DiffViewer.Sources.Git.Executable)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Kumiko E2E";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "e2e@example.invalid";
        start.Environment["GIT_AUTHOR_DATE"] = start.Environment["GIT_COMMITTER_DATE"] = "2026-01-01T00:00:00Z";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        return output;
    }

    public void Write(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Commit(string message)
    {
        Git("add", "-A");
        Git("commit", "-q", "-m", message);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, true);
        }
        catch (IOException)
        {
        }
    }
}
