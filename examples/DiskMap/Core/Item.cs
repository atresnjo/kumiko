using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DiskMap.Core;

public enum ItemKind : byte
{
    Folder,
    File,
    /// <summary>A symbolic link, counted by its own size and never followed.</summary>
    Link,
    /// <summary>A mount point of another volume; shown empty and never entered.</summary>
    Volume,
    /// <summary>Items too small to draw, merged by the treemap; never produced by a scan.</summary>
    Rest
}

/// <summary>What tile areas and list order measure.</summary>
public enum Metric : byte
{
    /// <summary>Blocks allocated on disk, which is what freeing an item gives back.</summary>
    Disk,
    /// <summary>Logical file lengths, as Finder's "size" shows them.</summary>
    Apparent,
    Files
}

/// <summary>
/// What a folder holds: its children sorted by disk size, largest first, and the counts of everything beneath it.
/// Children stay out of model JSON: dev mode serializes the model every frame, and a home folder has millions of
/// items that the trail, tiles and list would each repeat. The counts and sizes still change whenever a subtree does.
/// </summary>
public sealed record Contents([property: JsonIgnore] ImmutableArray<Item> Children, int Files, int Folders, int Errors);

/// <summary>
/// One scanned item, 40 bytes stored inline in its folder's array: most items are files, and a home folder has tens
/// of millions of them, so only folders own an object (their <see cref="Contents"/>). Names repeat across folders
/// and are shared by the scanner. The tree is immutable; removing or rescanning an item copies only its ancestors.
/// Modified is seconds since 1970.
/// </summary>
public readonly record struct Item(string Name, ItemKind Kind, long Size, long Apparent, uint Modified, Contents? Contents)
{
    static readonly Contents Empty = new([], 0, 0, 0);

    public bool IsFolder => Kind == ItemKind.Folder;
    /// <summary>Has children worth opening.</summary>
    public bool Opens => Kind == ItemKind.Folder && Children.Length > 0;
    public ImmutableArray<Item> Children => (Contents ?? Empty).Children;
    public int Files => Contents?.Files ?? (Kind == ItemKind.File ? 1 : 0);
    public int Folders => Contents?.Folders ?? 0;
    public int Errors => Contents?.Errors ?? 0;

    public long Value(Metric metric) => metric switch
    {
        Metric.Disk => Size,
        Metric.Apparent => Apparent,
        _ => Files
    };

    /// <summary>Children ordered by a metric, largest first, ties by name.</summary>
    public ImmutableArray<Item> Ranked(Metric metric) => metric == Metric.Disk || Children.Length < 2 ? Children :
        [.. Children.OrderByDescending(c => c.Value(metric)).ThenBy(c => c.Name, StringComparer.Ordinal)];

    public Item? Child(string name)
    {
        foreach (var child in Children)
            if (child.Name == name)
                return child;
        return null;
    }

    /// <summary>The largest child by disk size.</summary>
    public Item? Largest => Children.IsDefaultOrEmpty ? null : Children[0];

    /// <summary>
    /// A folder over children, summing their totals; ordering is by disk size, then name, so every scan of the same
    /// files produces the same tree.
    /// </summary>
    public static Item Folder(string name, long ownSize, long modified, IEnumerable<Item> children, int errors = 0) =>
        Sum(name, ownSize, modified, children.OrderByDescending(c => c.Size).ThenBy(c => c.Name, StringComparer.Ordinal).ToImmutableArray(), errors);

    /// <summary>A folder over children already in <see cref="Order"/>.</summary>
    public static Item Sum(string name, long ownSize, long modified, ImmutableArray<Item> sorted, int errors)
    {
        long size = ownSize;
        long apparent = 0;
        int files = 0;
        int folders = 0;
        long latest = modified;
        foreach (var child in sorted)
        {
            size += child.Size;
            apparent += child.Apparent;
            files += child.Files;
            folders += child.Folders + (child.Kind == ItemKind.Folder ? 1 : 0);
            errors += child.Errors;
            latest = Math.Max(latest, child.Modified);
        }
        return new(name, ItemKind.Folder, size, apparent, Seconds(latest), new(sorted, files, folders, errors));
    }

    /// <summary>The tree's order: disk size descending, then name.</summary>
    public static int Order(Item a, Item b)
    {
        int bySize = b.Size.CompareTo(a.Size);
        return bySize != 0 ? bySize : string.CompareOrdinal(a.Name, b.Name);
    }

    public static Item File(string name, long size, long apparent, long modified, ItemKind kind = ItemKind.File) =>
        new(name, kind, size, apparent, Seconds(modified), null);

    /// <summary>An item drawn in place of others, such as merged small items, with the totals they add up to.</summary>
    public static Item Merged(string name, long size, long apparent, int files, int folders, long modified) =>
        new(name, ItemKind.Rest, size, apparent, Seconds(modified), new([], files, folders, 0));

    static uint Seconds(long seconds) => (uint)Math.Clamp(seconds, 0, uint.MaxValue);
}
