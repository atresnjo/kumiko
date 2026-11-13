using System.Diagnostics;
using System.Security.Cryptography;
using DiskMap.Core;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiskMap.E2E;

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

    public void Timing(string name, double value)
    {
        Timings[name] = Math.Round(value, 3);
        Console.WriteLine($"{name}: {value:F3}");
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

    /// <summary>A program over the demo tree with a recording desk; nothing reaches Finder or the Trash.</summary>
    public static (Program<Model, Msg> Program, RecordingDesk Desk) Demo(float width = 0, float height = 0)
    {
        var desk = new RecordingDesk();
        var model = DiskMapApp.Show(Core.Demo.Root, Core.Demo.Home(), width, height) with
        {
            Home = "/Users/demo"
        };
        var program = DiskMapApp.CreateProgram(model, new KumikoServices().Add<IDesk>(desk));
        program.RetainHistory = false;
        return (program, desk);
    }

    /// <summary>Frames until a condition holds; effects and subscriptions answer on workers.</summary>
    public static void Until(Tester tester, Func<bool> condition, string what, int milliseconds = 20_000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > milliseconds)
                throw new TimeoutException("Timed out waiting for " + what);
            Thread.Sleep(2);
            tester.Frame();
        }
        tester.Settle();
    }
}

/// <summary>A per-process folder under the system temporary folder, removed afterwards.</summary>
internal sealed class Fixture : IDisposable
{
    public string Root
    {
        get;
    }

    public Fixture(string name)
    {
        Root = Path.Combine(Path.GetTempPath(), "kumiko-diskmap-e2e", name + "-" + Environment.ProcessId);
        Remove();
        Directory.CreateDirectory(Root);
    }

    public string Write(string path, int bytes)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var data = new byte[bytes];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 31 + 7);
        File.WriteAllBytes(full, data);
        return full;
    }

    public string Folder(string path) => Directory.CreateDirectory(Path.Combine(Root, path)).FullName;

    void Remove()
    {
        if (!Directory.Exists(Root))
            return;
        foreach (var directory in Directory.EnumerateDirectories(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Directory.Delete(Root, true);
    }

    public void Dispose()
    {
        try
        {
            Remove();
        }
        catch (IOException)
        {
        }
    }
}

internal static class Items
{
    /// <summary>The item at a slash-separated path below a scanned root.</summary>
    public static Item At(Item root, string path)
    {
        var at = root;
        foreach (var name in path.Split('/'))
            at = at.Child(name) ?? throw new KeyNotFoundException($"{path}: no {name} in {at.Name}");
        return at;
    }

    /// <summary>Checks every folder's totals against its children, all the way down.</summary>
    public static bool Consistent(Item folder)
    {
        if (!folder.IsFolder)
            return true;
        long size = folder.Children.Sum(c => c.Size);
        long apparent = folder.Children.Sum(c => c.Apparent);
        int files = folder.Children.Sum(c => c.Files);
        return folder.Size >= size && folder.Apparent == apparent && folder.Files == files && folder.Children.All(Consistent);
    }
}
