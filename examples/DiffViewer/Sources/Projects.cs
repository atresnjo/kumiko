using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiffViewer.Sources;

/// <summary>A repository checkout. Main is the main checkout's path when this is a linked worktree.</summary>
public sealed record Project(string Path, string Name, string Branch, string? Main = null)
{
    public bool Worktree => Main is not null;
    /// <summary>The main checkout this project belongs to; worktrees of one repository share it.</summary>
    public string Family => Main ?? Path;
}

/// <summary>
/// Finds repositories and their worktrees without starting Git. The walk is Flint's: it stops at each .git, skips
/// hidden, dependency and build folders, bundles, symlinks and other volumes, and caps depth and count. Worktrees
/// come from each repository's own metadata, so checkouts outside the scanned folders are listed too.
/// </summary>
public static class Projects
{
    static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "node_modules", "Library", "Applications", "Movies", "Music", "Pictures", "target", "dist", "build", "vendor",
        "Pods", "DerivedData", "__pycache__", "venv", "bin", "obj"
    };
    static readonly string[] Bundles = [".app", ".bundle", ".framework", ".xcarchive", ".photoslibrary", ".musiclibrary", ".tvlibrary",
        ".xcassets", ".lproj", ".kext", ".plugin"];
    const int MaxDepth = 12;
    const int MaxRepositories = 10000;

    /// <summary>Repository folders under the roots, in ordinal order.</summary>
    public static ImmutableArray<string> Walk(IEnumerable<string> roots, CancellationToken token)
    {
        var found = new ConcurrentBag<string>();
        var mounts = Mounts();
        var starts = roots.Where(Directory.Exists).Select(System.IO.Path.GetFullPath).Distinct(StringComparer.Ordinal).ToArray();
        void Visit(string directory, int depth, bool leaf)
        {
            if (found.Count >= MaxRepositories || token.IsCancellationRequested)
                return;
            try
            {
                var git = System.IO.Path.Combine(directory, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    found.Add(directory);
                    return;
                }
                if (leaf || depth >= MaxDepth)
                    return;
                var children = new List<(string Path, bool Leaf)>();
                foreach (var child in new DirectoryInfo(directory).EnumerateDirectories())
                {
                    var name = child.Name;
                    if (name.StartsWith('.') || Skip.Contains(name) || (child.Attributes & FileAttributes.ReparsePoint) != 0 || mounts.Contains(child.FullName))
                        continue;
                    children.Add((child.FullName, Bundles.Any(b => name.EndsWith(b, StringComparison.OrdinalIgnoreCase))));
                }
                if (depth < 2)
                    Parallel.ForEach(children, new ParallelOptions { CancellationToken = token }, c => Visit(c.Path, depth + 1, c.Leaf));
                else
                    foreach (var (path, bundle) in children)
                        Visit(path, depth + 1, bundle);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
            }
        }
        foreach (var start in starts)
            Visit(start, 0, false);
        token.ThrowIfCancellationRequested();
        return [.. found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Describes each repository and adds its linked worktrees, main checkouts first, each followed by its
    /// worktrees. A linked worktree found by the walk joins its main checkout's family.
    /// </summary>
    public static ImmutableArray<Project> Expand(IEnumerable<string> repositories)
    {
        var byPath = new Dictionary<string, Project>(StringComparer.Ordinal);
        foreach (var path in repositories)
        {
            if (Describe(path) is not { } project)
                continue;
            byPath.TryAdd(project.Path, project);
            var main = project.Family;
            if (!byPath.ContainsKey(main) && Describe(main) is { } owner)
                byPath[main] = owner;
            foreach (var worktree in Worktrees(main))
                byPath.TryAdd(worktree.Path, worktree);
        }
        var families = byPath.Values.GroupBy(p => p.Family, StringComparer.Ordinal)
            .OrderBy(g => System.IO.Path.GetFileName(g.Key), StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key, StringComparer.Ordinal);
        return [.. families.SelectMany(g => g.OrderBy(p => p.Worktree).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase))];
    }

    /// <summary>The checkout at a path, or null when it has no .git folder or file.</summary>
    public static Project? Describe(string path)
    {
        try
        {
            path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
            var git = System.IO.Path.Combine(path, ".git");
            var name = System.IO.Path.GetFileName(path);
            if (Directory.Exists(git))
                return new(path, name, Branch(git));
            if (!File.Exists(git))
                return null;
            var line = File.ReadLines(git).FirstOrDefault() ?? "";
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
                return null;
            var gitDir = System.IO.Path.GetFullPath(line["gitdir:".Length..].Trim(), path);
            // A linked worktree's metadata lives in <main>/.git/worktrees/<id>; a submodule's elsewhere.
            var worktrees = System.IO.Path.GetDirectoryName(gitDir);
            string? main = null;
            if (worktrees is not null && System.IO.Path.GetFileName(worktrees) == "worktrees" && System.IO.Path.GetDirectoryName(worktrees) is { } common
                && System.IO.Path.GetFileName(common) == ".git")
                main = System.IO.Path.GetDirectoryName(common);
            return new(path, main is null ? name : System.IO.Path.GetFileName(main) + "/" + name, Branch(gitDir), main);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Linked worktrees of a main checkout whose folders still exist.</summary>
    public static IEnumerable<Project> Worktrees(string main)
    {
        var metadata = System.IO.Path.Combine(main, ".git", "worktrees");
        if (!Directory.Exists(metadata))
            yield break;
        var repository = System.IO.Path.GetFileName(main);
        foreach (var entry in Directory.EnumerateDirectories(metadata))
        {
            string? path = null;
            try
            {
                var gitFile = File.ReadAllText(System.IO.Path.Combine(entry, "gitdir")).Trim();
                path = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(gitFile, entry));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
            if (path is not null && Directory.Exists(path))
                yield return new(path, repository + "/" + System.IO.Path.GetFileName(path), Branch(entry), main);
        }
    }

    /// <summary>The checked-out branch, or the abbreviated commit of a detached HEAD.</summary>
    public static string Branch(string gitDir)
    {
        try
        {
            var head = File.ReadLines(System.IO.Path.Combine(gitDir, "HEAD")).FirstOrDefault()?.Trim() ?? "";
            if (head.StartsWith("ref: refs/heads/", StringComparison.Ordinal))
                return head["ref: refs/heads/".Length..];
            return head.Length >= 7 ? head[..7] : head;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>Mount points other than the root volume; network and VM mounts cost most of a walk.</summary>
    static HashSet<string> Mounts()
    {
        var mounts = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar);
                if (root.Length > 0 && root != "/" && !(OperatingSystem.IsWindows() && root.Length <= 2))
                    mounts.Add(root);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
        return mounts;
    }
}

/// <summary>
/// Where the known-repository cache and the recent list live (~/.kumiko-diff by default, DIFF_HOME or
/// --state-dir=PATH), and the folders a scan walks: the home folder plus DIFF_ROOTS (platform path separator).
/// </summary>
public sealed record ProjectStore(string Directory, string Home, ImmutableArray<string> Roots)
{
    public const int RecentLimit = 30;

    /// <summary>A path shortened with ~ for the home folder.</summary>
    public string Tilde(string path) => Home.Length > 1 && path.StartsWith(Home + "/", StringComparison.Ordinal) ? "~" + path[Home.Length..] : path;
}

/// <summary>Reads and writes a store's JSON files. Every call does I/O; run them from commands and subscriptions.</summary>
public static class ProjectFiles
{
    public static ProjectStore Default(IReadOnlyList<string> args)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var state = args.FirstOrDefault(a => a.StartsWith("--state-dir=", StringComparison.Ordinal))?["--state-dir=".Length..]
            ?? Environment.GetEnvironmentVariable("DIFF_HOME") ?? Path.Combine(home, ".kumiko-diff");
        var extra = (Environment.GetEnvironmentVariable("DIFF_ROOTS") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return new(Path.GetFullPath(state), home, [home, .. extra]);
    }

    public static ImmutableArray<string> Recent(ProjectStore store) => Read(Path.Combine(store.Directory, "recent.json"));

    public static void SaveRecent(ProjectStore store, ImmutableArray<string> recent) => Write(store, "recent.json", recent);

    /// <summary>The previous scan and the recent list, described again from disk; no walk.</summary>
    public static ImmutableArray<Project> Cached(ProjectStore store) =>
        Projects.Expand([.. Read(Path.Combine(store.Directory, "known.json")), .. Recent(store)]);

    /// <summary>Walks the roots, saves the repositories found and describes them with the recent list.</summary>
    public static ImmutableArray<Project> Scan(ProjectStore store, CancellationToken token)
    {
        var found = Projects.Walk(store.Roots, token);
        Write(store, "known.json", found);
        return Projects.Expand([.. found, .. Recent(store)]);
    }

    static ImmutableArray<string> Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), ProjectJson.Default.ImmutableArrayString) : [];
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    static void Write(ProjectStore store, string name, ImmutableArray<string> paths)
    {
        try
        {
            Directory.CreateDirectory(store.Directory);
            var file = Path.Combine(store.Directory, name);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(paths, ProjectJson.Default.ImmutableArrayString));
            File.Move(temp, file, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSerializable(typeof(ImmutableArray<string>))]
internal sealed partial class ProjectJson : JsonSerializerContext;
