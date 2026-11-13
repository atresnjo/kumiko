using System.Collections.Immutable;

namespace DiskMap.Core;

/// <summary>
/// Edits of a scanned tree by path copying: the edited folder and its ancestors are rebuilt, every other node is
/// shared. A trail runs from the root to a folder, each entry a child of the one before.
/// </summary>
public static class Tree
{
    /// <summary>The full path of a trail's last folder, or of a child of it when a name is given.</summary>
    public static string PathOf(string root, ImmutableArray<Item> trail, string? name = null)
    {
        var path = root;
        for (int i = 1; i < trail.Length; i++)
            path = Path.Combine(path, trail[i].Name);
        return name is null ? path : Path.Combine(path, name);
    }

    /// <summary>Follows names from the root; stops at the deepest folder that still exists.</summary>
    public static ImmutableArray<Item> Follow(Item root, IEnumerable<string> names)
    {
        var trail = ImmutableArray.CreateBuilder<Item>();
        trail.Add(root);
        var at = root;
        foreach (var name in names)
        {
            if (at.Child(name) is not { IsFolder: true } next)
                break;
            trail.Add(next);
            at = next;
        }
        return trail.ToImmutable();
    }

    /// <summary>Replaces the trail's last folder with a rescanned copy and returns the new trail.</summary>
    public static ImmutableArray<Item> Replace(ImmutableArray<Item> trail, Item replacement) => Rebuild(trail, trail.Length - 1, replacement);

    /// <summary>Removes children of the trail's last folder by name and returns the new trail.</summary>
    public static ImmutableArray<Item> Remove(ImmutableArray<Item> trail, ImmutableHashSet<string> names)
    {
        var folder = trail[^1];
        var kept = folder.Children.Where(c => !names.Contains(c.Name));
        var own = folder.Size - folder.Children.Sum(c => c.Size);
        return Rebuild(trail, trail.Length - 1, Item.Folder(folder.Name, own, folder.Modified, kept, folder.Errors - folder.Children.Sum(c => c.Errors)));
    }

    static ImmutableArray<Item> Rebuild(ImmutableArray<Item> trail, int index, Item replacement)
    {
        // A fresh local array, so the purity analyzer sees writes to frame-local storage only.
        var rebuilt = new Item[trail.Length];
        trail.CopyTo(rebuilt);
        rebuilt[index] = replacement;
        for (int i = index - 1; i >= 0; i--)
        {
            var parent = trail[i];
            var old = trail[i + 1];
            var own = parent.Size - parent.Children.Sum(c => c.Size);
            var errors = parent.Errors - parent.Children.Sum(c => c.Errors);
            var children = parent.Children.Select(c => ReferenceEquals(c.Contents, old.Contents) ? rebuilt[i + 1] : c);
            rebuilt[i] = Item.Folder(parent.Name, own, parent.Modified, children, errors);
        }
        return ImmutableArray.Create(rebuilt);
    }

    /// <summary>The trail to a descendant path, or null when it lies outside the scanned root.</summary>
    public static ImmutableArray<string>? Names(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".")
            return [];
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return null;
        return [.. relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)];
    }
}
