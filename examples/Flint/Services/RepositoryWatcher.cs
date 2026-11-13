namespace Flint.Services;

public interface IRepositoryWatcher
{
    /// <summary>Reports coalesced changes. The argument is true when Git metadata (HEAD, index, refs) changed and
    /// a full reload is needed; false when only working-tree files changed and `git status` suffices.</summary>
    IDisposable Watch(string path, Action<bool> changed);
}
/// <summary>Native file notifications and a one-shot debounce; no idle timer or render polling.</summary>
public sealed class RepositoryWatcher : IRepositoryWatcher
{
    public IDisposable Watch(string path, Action<bool> changed) => new WatchHandle(path, changed);
    sealed class WatchHandle : IDisposable, IAsyncDisposable
    {
        readonly List<FileSystemWatcher> watchers = [];
        readonly Timer debounce;
        readonly object gate = new();
        bool disposed;
        bool full;
        Task stopped = Task.CompletedTask;
        public WatchHandle(string path, Action<bool> changed)
        {
            debounce = new(_ =>
            {
                bool metadata;
                lock (gate)
                {
                    if (disposed)
                        return;
                    metadata = full;
                    full = false;
                }
                changed(metadata);
            }, null, Timeout.Infinite, Timeout.Infinite);
            Add(path, false);
            var dotgit = Path.Combine(path, ".git");
            if (File.Exists(dotgit))
            {
                var content = File.ReadAllText(dotgit).Trim();
                if (content.StartsWith("gitdir: ", StringComparison.Ordinal))
                {
                    var metadata = Path.GetFullPath(Path.Combine(path, content[8..]));
                    Add(metadata, true);
                    var commonFile = Path.Combine(metadata, "commondir");
                    if (File.Exists(commonFile))
                        Add(Path.GetFullPath(Path.Combine(metadata, File.ReadAllText(commonFile).Trim())), true);
                }
            }
        }
        void Add(string path, bool metadata)
        {
            if (!Directory.Exists(path))
                return;
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 32768
            };
            FileSystemEventHandler handler = (_, args) => Changed(args.Name?.Replace('\\', '/') ?? "", metadata);
            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Renamed += (_, args) => Changed(args.Name?.Replace('\\', '/') ?? "", metadata);
            watcher.Error += (_, _) => Schedule(true);
            watchers.Add(watcher);
            watcher.EnableRaisingEvents = true;
        }
        void Changed(string name, bool metadata)
        {
            if (name.EndsWith(".lock", StringComparison.Ordinal))
                return;
            if (metadata)
            {
                if (Meta(name))
                    Schedule(true);
                return;
            }
            if (name == ".git" || name.StartsWith(".git/", StringComparison.Ordinal))
            {
                if (Meta(name[Math.Min(name.Length, 5)..]))
                    Schedule(true);
                return;
            }
            if (name.Contains("node_modules/", StringComparison.Ordinal))
                return;
            Schedule(false);
        }
        /// <summary>The original's GIT_META set: HEAD, index, packed refs, refs, fetch/merge heads, the stash log and worktrees.</summary>
        static bool Meta(string name) => name is "HEAD" or "index" or "packed-refs" or "FETCH_HEAD" or "ORIG_HEAD" or "MERGE_HEAD"
            || name.StartsWith("refs/", StringComparison.Ordinal) || name.StartsWith("logs/refs/stash", StringComparison.Ordinal)
            || name.StartsWith("worktrees", StringComparison.Ordinal);
        void Schedule(bool metadata)
        {
            lock (gate)
                if (!disposed)
                {
                    full |= metadata;
                    debounce.Change(120, Timeout.Infinite);
                }
        }
        public async ValueTask DisposeAsync()
        {
            Dispose();
            await stopped.ConfigureAwait(false);
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                stopped = debounce.DisposeAsync().AsTask();
                foreach (var watcher in watchers)
                    watcher.Dispose();
            }
        }
    }
}
