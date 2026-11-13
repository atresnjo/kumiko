using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DiskMap.Core;
using Kumiko.Runtime;

namespace DiskMap.E2E;

/// <summary>
/// The scanner against real temporary folders: totals, sparse files, hard and symbolic links, unreadable folders,
/// awkward names, cancellation, progress pacing and speed against one stat per file.
/// </summary>
internal static partial class ScanChecks
{
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Link(string existing, string created);

    public static void Run(Harness h)
    {
        Totals(h);
        Links(h);
        Unreadable(h);
        Names(h);
        Cancel(h);
        Progress(h);
        Speed(h);
    }

    static void Totals(Harness h)
    {
        using var f = new Fixture("totals");
        f.Write("a.bin", 10_000);
        f.Write("nested/deeper/b.bin", 1_000_000);
        f.Write("nested/c.txt", 1);
        f.Folder("empty");
        var sparse = Path.Combine(f.Root, "sparse.img");
        using (var stream = File.Create(sparse))
            stream.SetLength(200_000_000);
        var root = Scanner.Scan(f.Root);
        h.Check(root.Files == 4 && Items.At(root, "nested").Files == 2, $"file counts include nested folders ({root.Files})");
        h.Check(Items.At(root, "a.bin").Apparent == 10_000 && Items.At(root, "nested/deeper/b.bin").Apparent == 1_000_000,
            "apparent size is the file length");
        h.Check(Items.At(root, "a.bin").Size >= 10_000 && Items.At(root, "a.bin").Size % 512 == 0, "disk size counts allocated blocks");
        h.Check(Items.Consistent(root), "every folder's totals are the sums of its children");
        h.Check(Items.At(root, "empty") is { IsFolder: true, Size: 0, Files: 0 }, "an empty folder is an empty node");
        var image = Items.At(root, "sparse.img");
        h.Check(image.Apparent == 200_000_000, "a sparse file reports its logical length as apparent size");
        if (OperatingSystem.IsMacOS())
            h.Check(image.Size < image.Apparent / 10, $"a sparse file's disk size is what it allocates ({image.Size})");
        var again = Scanner.Scan(f.Root, 1);
        h.Check(Summary(again) == Summary(root), "a scan on one thread and on many build the same tree");
    }

    static string Summary(Item item) => item.Name + ":" + item.Size + ":" + item.Files + "[" + string.Join(",", item.Children.Select(Summary)) + "]";

    static void Links(Harness h)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var f = new Fixture("links");
        var original = f.Write("data/original.bin", 4_000_000);
        h.Check(Link(original, Path.Combine(f.Root, "data", "second.bin")) == 0 && Link(original, Path.Combine(f.Root, "copy.bin")) == 0,
            "the fixture hard-links one file twice");
        Directory.CreateSymbolicLink(Path.Combine(f.Root, "loop"), f.Root);
        Directory.CreateSymbolicLink(Path.Combine(f.Root, "data", "up"), Path.Combine(f.Root, "data"));
        File.CreateSymbolicLink(Path.Combine(f.Root, "alias.bin"), original);
        var clock = Stopwatch.StartNew();
        var root = Scanner.Scan(f.Root);
        h.Check(clock.ElapsedMilliseconds < 5000, "links to ancestors do not loop");
        h.Check(root.Apparent < 4_100_000, $"three links to 4 MB count once ({root.Apparent})");
        h.Check(Items.At(root, "loop").Kind == ItemKind.Link && Items.At(root, "data/up").Kind == ItemKind.Link &&
            Items.At(root, "alias.bin").Kind == ItemKind.Link, "symbolic links are items of their own and never followed");
        h.Check(Items.Consistent(root), "linked trees keep consistent totals");
    }

    static void Unreadable(Harness h)
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;
        using var f = new Fixture("unreadable");
        f.Write("open/a.bin", 50_000);
        f.Write("closed/secret.bin", 70_000);
        var closed = Path.Combine(f.Root, "closed");
        File.SetUnixFileMode(closed, UnixFileMode.None);
        try
        {
            var root = Scanner.Scan(f.Root);
            h.Check(root.Errors == 1 && Items.At(root, "closed").Errors == 1, $"an unreadable folder counts as one error ({root.Errors})");
            h.Check(Items.At(root, "open/a.bin").Apparent == 50_000 && root.Files == 1, "the rest of the scan still counts");
        }
        finally
        {
            File.SetUnixFileMode(closed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    static void Names(Harness h)
    {
        using var f = new Fixture("names");
        string[] names = ["with space.txt", "Ünïcødé 文件 ✓.txt", "emoji 🧪 test", new string('n', 200) + ".log", ".hidden"];
        foreach (var name in names)
            f.Write(name, 100);
        var root = Scanner.Scan(f.Root);
        h.Check(names.All(n => root.Child(n) is { Apparent: 100 }), "names with spaces, Unicode and long components arrive intact");
    }

    /// <summary>Generates folders of small files; returns the file count.</summary>
    static int Generate(Fixture f, int folders, int files)
    {
        var data = new byte[700];
        Parallel.For(0, folders, i =>
        {
            var folder = f.Folder($"d{i / 50:D2}/f{i:D4}");
            for (int j = 0; j < files; j++)
                File.WriteAllBytes(Path.Combine(folder, $"file{j:D3}.dat"), data);
        });
        return folders * files;
    }

    static void Cancel(Harness h)
    {
        using var f = new Fixture("cancel");
        Generate(f, 200, 50);
        var scanner = new Scanner(f.Root);
        scanner.Start();
        scanner.Dispose();
        try
        {
            scanner.Result.Wait(5000);
            h.Check(scanner.Result.IsCanceled, "a disposed scan is cancelled");
        }
        catch (AggregateException)
        {
            h.Check(scanner.Result.IsCanceled, "a disposed scan is cancelled, not failed");
        }
        // A newer scan replaces the subscription key: the older one's result never reaches the model.
        var stale = DiskMapApp.Update(DiskMapApp.Start(f.Root) with
        {
            Generation = 5
        }, new Msg.Scanned(4, [], Scanner.Scan(f.Root), TimeSpan.Zero)).Item1;
        h.Check(!stale.Loaded && stale.Scanning is not null, "a result of an older scan is ignored");
    }

    /// <summary>Progress arrives about ten times a second, and nothing arrives after the result.</summary>
    static void Progress(Harness h)
    {
        using var f = new Fixture("progress");
        Generate(f, 600, 100);
        var messages = new List<(long Ticks, Msg Message)>();
        using var finished = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        using (new ScanJob(f.Root, 1, [], message =>
        {
            lock (messages)
                messages.Add((clock.ElapsedTicks, message));
            if (message is Msg.Scanned)
                finished.Set();
        }))
        {
            h.Check(finished.Wait(60_000), "the scan job reports its result");
            Thread.Sleep(350);
        }
        lock (messages)
        {
            var progress = messages.Where(m => m.Message is Msg.Progress).Select(m => m.Ticks).ToArray();
            double seconds = clock.Elapsed.TotalSeconds;
            h.Check(messages[^1].Message is Msg.Scanned, "no progress arrives after the result: the timer stopped");
            h.Check(progress.Length <= seconds * 11 + 1, $"progress arrives at most about ten times a second ({progress.Length} in {seconds:F2} s)");
            var scanned = (Msg.Scanned)messages[^1].Message;
            h.Check(scanned.Item.Files == 60_000, $"the job scanned every file ({scanned.Item.Files})");
        }
    }

    /// <summary>100k files: the parallel bulk scanner against one stat per entry on one thread.</summary>
    static void Speed(Harness h)
    {
        using var f = new Fixture("speed");
        int count = Generate(f, 1000, 100);
        Scanner.Scan(f.Root);
        var clock = Stopwatch.StartNew();
        var root = Scanner.Scan(f.Root);
        double fast = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        long baseline = 0;
        foreach (var path in Directory.EnumerateFiles(f.Root, "*", SearchOption.AllDirectories))
            baseline += new FileInfo(path).Length;
        double slow = clock.Elapsed.TotalMilliseconds;
        h.Timing("scan-100k-files-ms", fast);
        h.Timing("stat-per-file-100k-ms", slow);
        h.Check(root.Files == count && root.Apparent == baseline, $"the scan counts all {count} files");
        h.Check(fast < slow, $"scanning 100k files beats one stat per file ({fast:F0} ms vs {slow:F0} ms)");
    }
}
