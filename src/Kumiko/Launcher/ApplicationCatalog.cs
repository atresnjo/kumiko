using Kumiko.Rendering;

namespace Kumiko.Desktop;

/// <summary>A launchable installed application. Identity is an OS bundle/desktop ID or Start menu relative path.</summary>
public sealed record InstalledApplication(string Id, string Name, string Path);

/// <summary>Scans files on workers; native metadata and launches run on the App thread. No automatic refresh.</summary>
public sealed partial class ApplicationCatalog : IDisposable
{
    readonly App app;
    readonly string[] roots;
    readonly CancellationTokenSource stop = new();
    bool disposed;
    public ApplicationCatalog(App app) : this(app, null)
    {
    }

    internal ApplicationCatalog(App app, string[]? roots)
    {
        app.CheckThread();
        this.app = app;
        if (roots is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden catalogs require explicit fixture roots.");
        this.roots = roots ?? DefaultRoots();
        app.Disposing += Dispose;
    }

    public async Task<IReadOnlyList<InstalledApplication>> EnumerateAsync(CancellationToken cancellation = default)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation);
        var token = linked.Token;
        var paths = await Task.Run(() => Scan(token), token).ConfigureAwait(false);
        var result = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var canceled = token.Register(() => result.TrySetCanceled(token));
        try
        {
            app.Dispatch(() =>
            {
                if (token.IsCancellationRequested)
                    return;
                try
                {
                    var entries = new List<InstalledApplication>();
                    var offset = 0;
                    Batch();
                    void Batch()
                    {
                        if (token.IsCancellationRequested)
                            return;
                        try
                        {
                            var end = Math.Min(offset + 16, paths.Count);
                            while (offset < end)
                                if (Metadata(paths[offset++]) is { } entry)
                                    entries.Add(entry);
                            if (offset < paths.Count)
                                app.Dispatch(Batch);
                            else
                                result.TrySetResult(Normalize(entries));
                        }
                        catch (Exception error)
                        {
                            result.TrySetException(error);
                        }
                    }
                }
                catch (Exception error)
                {
                    result.TrySetException(error);
                }
            });
        }
        catch (ObjectDisposedException)
        {
            result.TrySetCanceled();
        }
        return await result.Task.ConfigureAwait(false);
    }

    /// <summary>Deduplicates identity and normalized absolute path, preserving the first (higher priority) entry.</summary>
    public static IReadOnlyList<InstalledApplication> Normalize(IEnumerable<InstalledApplication> applications)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var result = new List<InstalledApplication>();
        foreach (var item in applications)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) || !System.IO.Path.IsPathFullyQualified(item.Path))
                continue;
            var full = System.IO.Path.GetFullPath(item.Path);
            var canonical = full;
            if (ids.Contains(item.Id) || paths.Contains(canonical))
                continue;
            ids.Add(item.Id);
            paths.Add(canonical);
            result.Add(item with
            {
                Path = canonical
            });
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray();
    }

    public Task LaunchAsync(InstalledApplication application)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(application);
        if (app.Options.Hidden)
            throw new InvalidOperationException("Hidden applications cannot launch installed apps.");
        if (!System.IO.Path.IsPathFullyQualified(application.Path))
            throw new ArgumentException("An application requires an absolute path.", nameof(application));
        return LaunchNative(application);
    }

    List<(string Path, string Id)> Scan(CancellationToken token)
    {
        var result = new List<(string, string)>();
        var desktopIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
            Visit(root, root, 0);
        return result;

        void Visit(string directory, string root, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory) || depth > 8)
                return;
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
                {
                    token.ThrowIfCancellationRequested();
                    if (OperatingSystem.IsMacOS() && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                        result.Add((Canonical(path), path));
                    else if (OperatingSystem.IsWindows() && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        result.Add((path, "start-menu:" + System.IO.Path.GetRelativePath(root, path).ToUpperInvariant()));
                    else if (OperatingSystem.IsLinux() && path.EndsWith(".desktop", StringComparison.Ordinal))
                    {
                        var id = System.IO.Path.GetRelativePath(root, path).Replace(System.IO.Path.DirectorySeparatorChar, '-');
                        // A user Hidden=true entry must mask its system equivalent.
                        if (desktopIds.Add(id))
                            result.Add((Canonical(path), id));
                    }
                    else if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is null)
                        Visit(path, root, depth + 1);
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                // A disappearing or inaccessible subfolder does not hide other applications.
            }
        }
    }

    static string Canonical(string path)
    {
        var info = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is null ? System.IO.Path.GetFullPath(path) : info.ResolveLinkTarget(true)?.FullName ?? path;
    }

    static string[] DefaultRoots()
    {
        if (OperatingSystem.IsMacOS())
            return [System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"), "/Applications", "/System/Applications", "/System/Library/CoreServices/Applications"];
        if (OperatingSystem.IsWindows())
            return [Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)];
        var user = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share");
        return [System.IO.Path.Combine(user, "applications"), .. (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") ?? "/usr/local/share:/usr/share").Split(':', StringSplitOptions.RemoveEmptyEntries).Select(p => System.IO.Path.Combine(p, "applications"))];
    }

    public void Dispose()
    {
        if (disposed)
            return;
        app.CheckThread();
        disposed = true;
        app.Disposing -= Dispose;
        stop.Cancel();
        stop.Dispose();
    }
}

/// <summary>Bounded lifetime admission avoids repeated atlas uploads. Retrieve on the owning UI thread.</summary>
public sealed class FileIconCache
{
    readonly Dictionary<(string Path, int Pixels), Image?> images = new();
    public int Count
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            return images.Count;
        }
    }
    public Image? Get(string path, int pixels = 64)
    {
        Runtime.KumikoExecution.CheckUI();
        App.Current.CheckThread();
        if (!System.IO.Path.IsPathFullyQualified(path) || pixels is < 16 or > 256)
            throw new ArgumentOutOfRangeException(nameof(path));
        if (images.TryGetValue((path, pixels), out var image))
            return image;
        if (images.Count >= 128)
            return null;
        image = LauncherIcons.Load(path, pixels);
        images[(path, pixels)] = image;
        return image;
    }
}
