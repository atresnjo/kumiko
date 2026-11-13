using System.IO.Enumeration;

namespace Kumiko.Desktop;

public enum FileChangeKind
{
    Created,
    Changed,
    Deleted,
    Renamed,
    Hint
}

public sealed record FileChange(FileChangeKind Kind, string Path, string? OldPath = null);
public sealed record FileWatchBatch(IReadOnlyList<FileChange> Changes, bool Overflow, string? Error = null);
public sealed record FileWatcherOptions(bool Recursive = false, TimeSpan? Debounce = null, IReadOnlyList<string>? Ignore = null, int EntryLimit = 4096, bool HintsOnly = false);
/// <summary>
/// Event-driven native file watching through the runtime's FSEvents,
/// ReadDirectoryChangesW and inotify backends. Events are hints, not a journal;
/// overflow requires rescanning. Dispose on the owning App thread.
/// </summary>
public sealed class FileWatcher : IDisposable
{
    readonly App app;
    readonly System.IO.FileSystemWatcher watcher;
    readonly FileWatcherOptions options;
    readonly string[] ignores;
    readonly string? file;
    readonly object gate = new();
    readonly List<FileChange> pending = new();
    readonly TimeSpan debounce, maximumWait;
    IDisposable? timer, deadline;
    bool disposed, overflow;
    string? error;
    public event Action<FileWatchBatch>? Changed;
    public FileWatcher(App app, string path, FileWatcherOptions? options = null)
    {
        app.CheckThread();
        this.app = app;
        this.options = options ?? new();
        debounce = this.options.Debounce ?? TimeSpan.FromMilliseconds(50);
        if (debounce < TimeSpan.Zero || debounce.TotalMilliseconds > uint.MaxValue - 1 || this.options.EntryLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        maximumWait = TimeSpan.FromMilliseconds(Math.Max(debounce.TotalMilliseconds, Math.Min(1000, debounce.TotalMilliseconds * 4)));
        ignores = this.options.Ignore?.ToArray() ?? [];
        if (ignores.Any(pattern => string.IsNullOrEmpty(pattern)))
            throw new ArgumentException("Ignore patterns cannot be empty.", nameof(options));
        path = Path.GetFullPath(path);
        if (!Directory.Exists(path))
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("The watched file or directory must exist.", path);
            file = path;
            path = Path.GetDirectoryName(path)!;
        }

        watcher = new(path)
        {
            IncludeSubdirectories = this.options.Recursive && file is null,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        watcher.Created += (_, e) => Enqueue(new(FileChangeKind.Created, e.FullPath));
        watcher.Changed += (_, e) => Enqueue(new(FileChangeKind.Changed, e.FullPath));
        watcher.Deleted += (_, e) => Enqueue(new(FileChangeKind.Deleted, e.FullPath));
        watcher.Renamed += (_, e) => Enqueue(new(FileChangeKind.Renamed, e.FullPath, e.OldFullPath));
        watcher.Error += (_, e) => Enqueue(null, e.GetException().Message);
        app.Disposing += Dispose;
        watcher.EnableRaisingEvents = true;
    }

    void Enqueue(FileChange? change, string? failure = null)
    {
        lock (gate)
        {
            if (disposed)
                return;
            if (change is { } entry)
            {
                bool included = Included(entry.Path);
                if (entry.Kind == FileChangeKind.Renamed && entry.OldPath is { } oldPath)
                {
                    bool oldIncluded = Included(oldPath);
                    if (!included && !oldIncluded)
                        return;
                    if (!included)
                        entry = new(FileChangeKind.Deleted, oldPath);
                    else if (!oldIncluded)
                        entry = new(FileChangeKind.Created, entry.Path);
                }
                else if (!included)
                    return;
                if (options.HintsOnly)
                    entry = new(FileChangeKind.Hint, file ?? watcher.Path);
                if (!overflow && !pending.Contains(entry))
                {
                    if (pending.Count == options.EntryLimit)
                    {
                        pending.Clear();
                        overflow = true;
                    }
                    else
                        pending.Add(entry);
                }
            }

            if (failure is not null)
            {
                overflow = true;
                error = failure;
            }

            timer?.Dispose();
            try
            {
                // The first event fixes the batch deadline. Further events may
                // move the trailing edge, but cannot starve delivery forever.
                deadline ??= app.Schedule(maximumWait, Deliver);
                timer = app.Schedule(debounce, Deliver);
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    bool Included(string path)
    {
        bool ignoreCase = OperatingSystem.IsWindows();
        if (file is not null && !path.Equals(file, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return false;
        string relative = Path.GetRelativePath(watcher.Path, path).Replace(Path.DirectorySeparatorChar, '/');
        foreach (string pattern in ignores)
        {
            string normalized = pattern.Replace(Path.DirectorySeparatorChar, '/').TrimEnd('/');
            string prefix = relative;
            while (prefix.Length > 0)
            {
                int separator = prefix.LastIndexOf('/');
                string name = prefix[(separator + 1)..];
                if (FileSystemName.MatchesSimpleExpression(normalized, prefix, ignoreCase) ||
                    (!normalized.Contains('/') && FileSystemName.MatchesSimpleExpression(normalized, name, ignoreCase)))
                    return false;
                if (separator < 0)
                    break;
                prefix = prefix[..separator];
            }
        }
        return true;
    }

    void Deliver()
    {
        app.CheckThread();
        FileWatchBatch batch;
        lock (gate)
        {
            if (disposed)
                return;
            batch = new(Array.AsReadOnly(pending.ToArray()), overflow, error);
            pending.Clear();
            overflow = false;
            error = null;
            timer?.Dispose();
            timer = null;
            deadline?.Dispose();
            deadline = null;
        }

        Changed?.Invoke(batch);
    }

    public void Dispose()
    {
        app.CheckThread();
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            timer?.Dispose();
            deadline?.Dispose();
            pending.Clear();
        }

        app.Disposing -= Dispose;
        watcher.Dispose();
    }
}
