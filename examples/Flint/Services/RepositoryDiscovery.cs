using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flint.Services;

public interface IRepositoryDiscovery
{
    /// <summary>Repositories found by the previous scan, read from the state directory; empty when none was saved.</summary>
    ImmutableArray<string> Known();
    Task<ImmutableArray<string>> Discover(CancellationToken token);
}
/// <summary>
/// The original's scan policy: stop at each .git, skip hidden folders, dependency/build folders, app bundles, symlinks
/// and other mounted volumes, cap depth and count, and cache results for the next launch.
/// </summary>
public sealed class RepositoryDiscovery(IEnumerable<string> roots, string? stateDirectory = null) : IRepositoryDiscovery
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
    string? KnownFile => stateDirectory is null ? null : Path.Combine(stateDirectory, "known-repos.json");
    public ImmutableArray<string> Known()
    {
        try
        {
            return KnownFile is { } file && File.Exists(file) ? JsonSerializer.Deserialize(File.ReadAllText(file), DiscoveryJson.Default.ImmutableArrayString) : [];
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
    public Task<ImmutableArray<string>> Discover(CancellationToken token) => Task.Run(() =>
    {
        var found = new ConcurrentBag<string>();
        var mounts = Mounts();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var skipPaths = new HashSet<string>(StringComparer.Ordinal) { Path.Combine(home, "go", "pkg") };
        var starts = roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        void Walk(string directory, int depth, bool leaf)
        {
            if (found.Count >= MaxRepositories || token.IsCancellationRequested)
                return;
            try
            {
                if (Directory.Exists(Path.Combine(directory, ".git")) || File.Exists(Path.Combine(directory, ".git")))
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
                    if (name.StartsWith('.') || Skip.Contains(name) || (child.Attributes & FileAttributes.ReparsePoint) != 0
                        || skipPaths.Contains(child.FullName) || mounts.Contains(child.FullName))
                        continue;
                    children.Add((child.FullName, Bundles.Any(b => name.EndsWith(b, StringComparison.OrdinalIgnoreCase))));
                }
                if (depth < 2)
                    Parallel.ForEach(children, new ParallelOptions { CancellationToken = token }, c => Walk(c.Path, depth + 1, c.Leaf));
                else
                    foreach (var (path, bundle) in children)
                        Walk(path, depth + 1, bundle);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        foreach (var start in starts)
            Walk(start, 0, false);
        token.ThrowIfCancellationRequested();
        var result = found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        if (KnownFile is { } file)
        {
            try
            {
                Directory.CreateDirectory(stateDirectory!);
                var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(result, DiscoveryJson.Default.ImmutableArrayString));
                File.Move(temp, file, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }, token);
    /// <summary>Mount points other than the root volume; network and VM mounts hold tool-managed clones and cost most of a walk.</summary>
    static HashSet<string> Mounts()
    {
        var mounts = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                var root = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);
                if (root.Length > 0 && root != "/" && !(OperatingSystem.IsWindows() && root.Length <= 2))
                    mounts.Add(root);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return mounts;
    }
}
[JsonSerializable(typeof(ImmutableArray<string>))]
partial class DiscoveryJson : JsonSerializerContext;
