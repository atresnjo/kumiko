using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DiskMap;

/// <summary>An item below the open folder, by the names leading to it, and its node when picked.</summary>
internal sealed record Pick(ImmutableArray<string> Names, Item Item)
{
    public string Name => Names[^1];
}

/// <summary>The context menu of an item, opened where the pointer was.</summary>
internal sealed record Menu(Pick Item, Point At);

/// <summary>Items waiting for the user to confirm moving them to the Trash, by full path.</summary>
internal sealed record Confirm(ImmutableArray<string> Paths, long Size);

internal sealed record Model
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("map");
    /// <summary>The scanned folder.</summary>
    public string Root { get; init; } = "";
    /// <summary>The user's home folder, written as ~ in the breadcrumb.</summary>
    public string Home { get; init; } = "";
    /// <summary>Increments with every scan or rescan; results of older ones are ignored.</summary>
    public long Generation
    {
        get; init;
    }
    /// <summary>The folder being scanned, by names below the root; empty scans the root. Null while idle.</summary>
    public ImmutableArray<string>? Scanning
    {
        get; init;
    }
    public ScanProgress? Progress
    {
        get; init;
    }
    public TimeSpan ScanTime
    {
        get; init;
    }
    public string? Error
    {
        get; init;
    }
    /// <summary>From the scanned root to the open folder; empty until the first scan finishes.</summary>
    public ImmutableArray<Item> Trail { get; init; } = [];
    public Pick? Selected
    {
        get; init;
    }
    /// <summary>Full paths of items marked for the Trash.</summary>
    public ImmutableSortedSet<string> Marked { get; init; } = ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public Metric Metric
    {
        get; init;
    }
    public string Filter { get; init; } = "";
    /// <summary>Changes when ⌘F asks for the filter field, which then takes focus.</summary>
    public int FilterFocus
    {
        get; init;
    }
    /// <summary>Changes whenever the selected row should be scrolled into view in the list.</summary>
    public long Reveal
    {
        get; init;
    }
    /// <summary>The treemap's size, reported by the view; the layout is computed for it.</summary>
    public float Width
    {
        get; init;
    }
    public float Height
    {
        get; init;
    }
    public TreemapLayout Layout { get; init; } = TreemapLayout.Empty;
    /// <summary>The open folder's items as the list shows them: ordered by the metric, matching the filter.</summary>
    public ImmutableArray<Item> Items { get; init; } = [];
    /// <summary>Tiles glide to the current layout: it came from opening a folder or changing the metric, not a resize.</summary>
    public bool Glide
    {
        get; init;
    }
    public Menu? Menu
    {
        get; init;
    }
    public Confirm? Confirm
    {
        get; init;
    }
    public bool Help
    {
        get; init;
    }
    public Volume? Volume
    {
        get; init;
    }
    /// <summary>A short result shown in the status line, such as a failed move to the Trash.</summary>
    public string? Notice
    {
        get; init;
    }

    public Item? Tree => Trail.IsEmpty ? null : Trail[0];
    public Item? Folder => Trail.IsEmpty ? null : Trail[^1];
    public bool Loaded => !Trail.IsEmpty;
    public ImmutableArray<string> Names => [.. Trail.Skip(1).Select(n => n.Name)];
    public string FolderPath => DiskMap.Core.Tree.PathOf(Root, Trail);
    public string PathOf(Pick pick) => Path.Combine(FolderPath, Path.Combine([.. pick.Names]));
    public ulong FolderKey => Treemap.KeyOf(Trail);
}
