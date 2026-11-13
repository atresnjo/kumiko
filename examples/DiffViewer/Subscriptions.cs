using DiffViewer.Sources;
using Kumiko.Runtime;

namespace DiffViewer;

internal sealed partial class DiffApp
{
    /// <summary>
    /// A load runs while the model is loading, keyed by its generation: a reload replaces the key, which cancels
    /// the previous load. The workspace watcher uses native file notifications and wakes nothing while idle.
    /// </summary>
    public static IEnumerable<Sub<Msg>> Subscriptions(Model model)
    {
        var generation = model.Generation;
        var request = model.Request;
        var whitespace = model.IgnoreWhitespace;
        var whole = model.Zen?.Path;
        // A pull request still loading in the background is shown by that load; a failed one loads here.
        var prefetching = model is { Zen: null, Request.Pull: { } pull } &&
            model.Prefetched.GetValueOrDefault(PrefetchKey(pull, whitespace)) is { Document: null, Failed: false };
        if (model.Loading && !prefetching)
            yield return Sub<Msg>.Run($"load:{generation}",
                async token => new Msg.Loaded(generation, await Loader.Load(request, whitespace, token, whole).ConfigureAwait(false)),
                error => new Msg.Failed(generation, error.Message));
        if (request.Watches && model.Document.Root.Length > 0)
        {
            var root = model.Document.Root;
            var extra = request.Kind == SourceKind.Paths ? request.Path : null;
            yield return new Sub<Msg>($"watch:{root}:{extra}", send => new Watcher(root, extra, () => send(new Msg.Changed())));
        }
        // Zen mode holds the window in full screen for as long as it lasts.
        if (model.Zen is not null)
            yield return Sub<Msg>.From<IFullScreen>("zen", static (window, send) => window.Hold(() => send(new Msg.LeftFullScreen())));
        // The cache lists projects at once; the walk of the home folder runs once the palette is first used.
        if (model.Store is { } store && !model.Cached)
            yield return Sub<Msg>.Run("projects:cache", _ => Task.Run(() => (Msg)new Msg.ProjectsFound(ProjectFiles.Cached(store), false)),
                error => new Msg.ProjectsFound([], false));
        else if (model.Store is { } scanner && model.Scanning)
            yield return Sub<Msg>.Run("projects:scan", token => Task.Run(() => (Msg)new Msg.ProjectsFound(ProjectFiles.Scan(scanner, token), true), token),
                error => new Msg.ProjectsFound(model.Projects, true));
    }
}

/// <summary>Coalesces file notifications for 250 ms. Object files and Git's own churn are ignored.</summary>
internal sealed class Watcher : IDisposable
{
    readonly List<FileSystemWatcher> watchers = [];
    readonly Timer debounce;
    readonly object gate = new();
    bool disposed;

    public Watcher(string root, string? extra, Action changed)
    {
        debounce = new(_ =>
        {
            lock (gate)
                if (disposed)
                    return;
            changed();
        }, null, Timeout.Infinite, Timeout.Infinite);
        Add(root);
        if (extra is not null && Directory.Exists(extra))
            Add(extra);
    }

    void Add(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 65536
        };
        FileSystemEventHandler handler = (_, args) => Changed(args.Name);
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (_, args) => Changed(args.Name);
        watcher.Error += (_, _) => Changed(null);
        watcher.EnableRaisingEvents = true;
        watchers.Add(watcher);
    }

    void Changed(string? name)
    {
        var path = name?.Replace('\\', '/') ?? "";
        // HEAD, the index and refs change what Git compares; objects, logs and lock files only churn.
        if (path.StartsWith(".git/", StringComparison.Ordinal) && !(path == ".git/HEAD" || path == ".git/index" || path.StartsWith(".git/refs/", StringComparison.Ordinal)))
            return;
        if (path.Contains("/node_modules/", StringComparison.Ordinal) || path.StartsWith("node_modules/", StringComparison.Ordinal))
            return;
        lock (gate)
            if (!disposed)
                debounce.Change(250, Timeout.Infinite);
    }

    public void Dispose()
    {
        lock (gate)
            disposed = true;
        foreach (var watcher in watchers)
            watcher.Dispose();
        debounce.Dispose();
    }
}
