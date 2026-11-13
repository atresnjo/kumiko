using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DiskMap.Core;

/// <summary>How far a scan has come; read at any time while it runs.</summary>
public sealed record ScanProgress(long Files, long Folders, long Bytes, int Errors, string Current, TimeSpan Elapsed);

/// <summary>
/// Scans a folder on several threads. Workers take folders depth first from a shared stack, list them with one
/// buffer each, and queue their subfolders; a folder becomes a node once its last subfolder finishes, so finished
/// subtrees are freed of their scan state at once. Links are never followed and other volumes are never entered.
/// </summary>
public sealed class Scanner : IDisposable
{
    sealed class Pending(string name, string path, long modified, Pending? parent)
    {
        public readonly string Name = name;
        public readonly string Path = path;
        public readonly long Modified = modified;
        public readonly Pending? Parent = parent;
        /// <summary>Exactly sized: files are written when the folder is read, subfolders as they finish.</summary>
        public Item[] Children = [];
        public int Filled;
        public int Errors;
        public int Remaining;
    }

    readonly Stack<Pending> work = new();
    readonly object gate = new();
    volatile bool cancelled;
    readonly Stopwatch clock = new();
    readonly HardLinks links = new();
    readonly Names names = new();
    readonly HashSet<long> devices = [];
    readonly HashSet<string> skipped = new(StringComparer.Ordinal);
    readonly TaskCompletionSource<Item> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly string root;
    Thread[] threads = [];
    int idle;
    long files;
    long folders;
    long bytes;
    int errors;
    volatile string current = "";

    public Scanner(string root)
    {
        this.root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
        if (this.root.Length == 0)
            this.root = "/";
    }

    /// <summary>Completes with the scanned root, or is cancelled by <see cref="Dispose"/>.</summary>
    public Task<Item> Result => done.Task;

    public ScanProgress Progress => new(Interlocked.Read(ref files), Interlocked.Read(ref folders), Interlocked.Read(ref bytes),
        Volatile.Read(ref errors), current, clock.Elapsed);

    /// <summary>Scans and waits for the result; used by tests and offscreen snapshots.</summary>
    public static Item Scan(string root, int threads = 0)
    {
        using var scanner = new Scanner(root);
        scanner.Start(threads);
        return scanner.Result.GetAwaiter().GetResult();
    }

    public void Start(int count = 0)
    {
        if (!Directory.Exists(root))
        {
            done.TrySetException(new DirectoryNotFoundException($"{root} is not a folder."));
            return;
        }
        if (OperatingSystem.IsMacOS())
        {
            devices.Add(MacDirectory.Device(root));
            // The system volume reaches the data volume through firmlinks such as /Users; count those once.
            if (root == "/")
            {
                devices.Add(MacDirectory.Device("/System/Volumes/Data"));
                skipped.Add("/System/Volumes/Data");
            }
        }
        clock.Start();
        var name = System.IO.Path.GetFileName(root);
        lock (gate)
            work.Push(new(name.Length == 0 ? root : name, root, 0, null)
            {
                Remaining = 1
            });
        count = count > 0 ? count : Math.Clamp(Environment.ProcessorCount, 4, 16);
        threads = new Thread[count];
        for (int i = 0; i < count; i++)
        {
            threads[i] = new Thread(Work)
            {
                IsBackground = true,
                Name = "DiskMap scan " + i,
                Priority = ThreadPriority.BelowNormal
            };
            threads[i].Start();
        }
    }

    void Work()
    {
        var entries = new Entries(links, names);
        try
        {
            while (Next() is { } folder)
                Read(folder, entries);
        }
        catch (Exception error)
        {
            done.TrySetException(error);
            cancelled = true;
            lock (gate)
                Monitor.PulseAll(gate);
        }
    }

    /// <summary>The next folder to read, waiting while others may still queue more; null once all are read.</summary>
    Pending? Next()
    {
        lock (gate)
        {
            while (true)
            {
                if (cancelled || done.Task.IsCompleted)
                    return null;
                if (work.TryPop(out var folder))
                    return folder;
                idle++;
                if (idle == threads.Length)
                {
                    Monitor.PulseAll(gate);
                    return null;
                }
                Monitor.Wait(gate);
                idle--;
            }
        }
    }

    void Read(Pending folder, Entries entries)
    {
        entries.Clear();
        current = folder.Path;
        bool foreign = false;
        bool read = skipped.Contains(folder.Path) ? true :
            OperatingSystem.IsMacOS() ? MacDirectory.Read(folder.Path, devices, entries, out foreign) : PortableDirectory.Read(folder.Path, entries);
        if (!read)
            folder.Errors++;
        folder.Errors += entries.Errors;
        var subfolders = entries.Folders;
        folder.Children = entries.Files.Count + subfolders.Count == 0 ? [] : new Item[entries.Files.Count + subfolders.Count];
        entries.Files.CopyTo(folder.Children);
        folder.Filled = entries.Files.Count;
        Interlocked.Add(ref files, entries.Files.Count);
        Interlocked.Add(ref bytes, entries.Bytes);
        Interlocked.Add(ref errors, folder.Errors);
        if (foreign)
            folder.Errors = -1;
        folder.Remaining = subfolders.Count + 1;
        if (subfolders.Count > 0)
        {
            lock (gate)
            {
                foreach (var (name, modified) in subfolders)
                    work.Push(new(name, System.IO.Path.Join(folder.Path, name), modified, folder));
                Monitor.PulseAll(gate);
            }
        }
        Interlocked.Add(ref folders, subfolders.Count);
        Finish(folder);
    }

    /// <summary>Counts down a folder; the last finishing child turns it into a node and finishes its parent.</summary>
    void Finish(Pending folder)
    {
        while (Interlocked.Decrement(ref folder.Remaining) == 0)
        {
            Item node;
            if (folder.Errors < 0)
                node = new(folder.Name, ItemKind.Volume, 0, 0, (uint)Math.Max(0, folder.Modified), null);
            else
            {
                var children = folder.Children;
                Array.Sort(children, 0, folder.Filled, Comparer<Item>.Create(Item.Order));
                var sorted = folder.Filled == children.Length ? ImmutableCollectionsMarshal.AsImmutableArray(children) : [.. children.AsSpan(0, folder.Filled)];
                node = Item.Sum(folder.Name, 0, folder.Modified, sorted, folder.Errors);
                folder.Children = [];
            }
            if (folder.Parent is not { } parent)
            {
                clock.Stop();
                done.TrySetResult(node);
                lock (gate)
                    Monitor.PulseAll(gate);
                return;
            }
            parent.Children[Interlocked.Increment(ref parent.Filled) - 1] = node;
            folder = parent;
        }
    }

    public void Dispose()
    {
        cancelled = true;
        done.TrySetCanceled();
        lock (gate)
            Monitor.PulseAll(gate);
    }
}
