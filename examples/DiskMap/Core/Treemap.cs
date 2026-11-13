using System.Collections.Immutable;

namespace DiskMap.Core;

public enum TileShape : byte
{
    /// <summary>An item drawn whole: a file, or a folder without room to show its contents.</summary>
    Leaf,
    /// <summary>A folder with a header and its children laid out inside.</summary>
    Frame
}

/// <summary>
/// One placed item. Key is the same for an item wherever it shows, derived from its path below the scanned root,
/// so a tile glides from its nested place when its folder is opened. Hue is the rank of its top-level ancestor
/// and Parent the index of the frame holding it, or -1.
/// </summary>
public readonly record struct Tile(Item Item, ulong Key, int Depth, int Hue, float X, float Y, float W, float H, TileShape Shape, long Value, int Parent)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Contains(float x, float y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>Tiles in paint order: every frame comes before the tiles inside it.</summary>
public sealed record TreemapLayout(ImmutableArray<Tile> Tiles, float Width, float Height)
{
    public static TreemapLayout Empty { get; } = new([], 0, 0);

    /// <summary>The deepest tile under a point, or -1.</summary>
    public int Hit(float x, float y)
    {
        for (int i = Tiles.Length - 1; i >= 0; i--)
            if (Tiles[i].Contains(x, y))
                return i;
        return -1;
    }

    /// <summary>Names leading from the laid out folder to a tile.</summary>
    public ImmutableArray<string> NamesOf(int index)
    {
        int depth = Tiles[index].Depth;
        var names = new string[depth + 1];
        for (int at = index; at >= 0; at = Tiles[at].Parent)
            names[Tiles[at].Depth] = Tiles[at].Item.Name;
        return ImmutableArray.Create(names);
    }

    public int IndexOf(ulong key)
    {
        for (int i = 0; i < Tiles.Length; i++)
            if (Tiles[i].Key == key)
                return i;
        return -1;
    }
}

/// <summary>
/// Squarified treemaps (Bruls, Huizing and van Wijk), nested breadth first so the largest folders get detail
/// first. Items too small to see are merged into one tile per folder, and a budget bounds the tiles of a frame.
/// </summary>
public static class Treemap
{
    public const float Header = 22;
    public const float Inset = 3;
    public const float Gap = 2;
    const float MinArea = 220;
    const float FrameWidth = 72;
    const float FrameHeight = 54;
    const int MaxDepth = 3;
    const int Budget = 700;
    const int PerFolder = 160;

    /// <summary>The stable identity of a child, given its parent's.</summary>
    public static ulong KeyOf(ulong parent, string name)
    {
        ulong hash = parent ^ 0xcbf29ce484222325UL;
        foreach (char c in name)
            hash = (hash ^ c) * 0x100000001b3UL;
        return (hash ^ 0x2f) * 0x100000001b3UL;
    }

    /// <summary>The key of a trail's last folder; the scanned root's key is 1.</summary>
    public static ulong KeyOf(ImmutableArray<Item> trail)
    {
        ulong key = 1;
        for (int i = 1; i < trail.Length; i++)
            key = KeyOf(key, trail[i].Name);
        return key;
    }

    /// <summary>Lays out a folder's children in a width × height area.</summary>
    public static TreemapLayout Build(Item folder, ulong key, Metric metric, float width, float height)
    {
        if (width < 1 || height < 1 || !folder.Opens)
            return new([], Math.Max(0, width), Math.Max(0, height));
        var tiles = ImmutableArray.CreateBuilder<Tile>();
        // An append-only builder read from the front is the breadth-first queue.
        var queue = ImmutableArray.CreateBuilder<(Item Folder, ulong Key, int Depth, int Hue, int Parent, float X, float Y, float W, float H)>();
        queue.Add((folder, key, 0, -1, -1, 0, 0, width, height));
        for (int head = 0; head < queue.Count; head++)
        {
            var job = queue[head];
            var items = Items(job.Folder, metric, job.W * job.H, Math.Min(PerFolder, Math.Max(8, Budget - tiles.Count)));
            if (items.Length == 0)
                continue;
            var rects = Squarify(items, metric, job.X, job.Y, job.W, job.H);
            for (int i = 0; i < items.Length; i++)
            {
                var node = items[i];
                var (x, y, w, h) = Shrink(rects[i]);
                if (w < 1 || h < 1)
                    continue;
                var hue = job.Depth == 0 ? i : job.Hue;
                var childKey = node.Kind == ItemKind.Rest ? KeyOf(job.Key, "\u0001rest") : KeyOf(job.Key, node.Name);
                bool frame = node.Opens && job.Depth + 1 < MaxDepth && w >= FrameWidth && h >= FrameHeight && tiles.Count < Budget;
                tiles.Add(new(node, childKey, job.Depth, hue, x, y, w, h, frame ? TileShape.Frame : TileShape.Leaf, node.Value(metric), job.Parent));
                if (frame)
                    queue.Add((node, childKey, job.Depth + 1, hue, tiles.Count - 1, x + Inset, y + Header, w - Inset * 2, h - Header - Inset));
            }
        }
        return new(tiles.ToImmutable(), width, height);
    }

    /// <summary>Leaves a gap between neighbours, keeping at least a pixel.</summary>
    static (float X, float Y, float W, float H) Shrink((float X, float Y, float W, float H) r)
    {
        float gx = r.W > Gap * 3 ? Gap / 2 : 0;
        float gy = r.H > Gap * 3 ? Gap / 2 : 0;
        return (r.X + gx, r.Y + gy, r.W - gx * 2, r.H - gy * 2);
    }

    /// <summary>
    /// A folder's children with a positive value, largest first. Once items fall below a visible area (or the
    /// count limit), the rest are merged into one Rest node unless only a single item would be merged.
    /// </summary>
    public static ImmutableArray<Item> Items(Item folder, Metric metric, float area, int limit)
    {
        var ranked = folder.Ranked(metric);
        long total = 0;
        foreach (var child in ranked)
            total += Math.Max(0, child.Value(metric));
        if (total <= 0)
            return [];
        double scale = area / total;
        var items = ImmutableArray.CreateBuilder<Item>();
        for (int i = 0; i < ranked.Length; i++)
        {
            var child = ranked[i];
            long value = child.Value(metric);
            if (value <= 0)
                break;
            bool small = value * scale < MinArea || items.Count >= limit - 1;
            if (small && i < ranked.Length - 1 && ranked[i + 1].Value(metric) > 0)
            {
                items.Add(Rest(ranked, i, metric));
                break;
            }
            items.Add(child);
        }
        return items.ToImmutable();
    }

    static Item Rest(ImmutableArray<Item> ranked, int from, Metric metric)
    {
        long size = 0;
        long apparent = 0;
        int files = 0;
        int folders = 0;
        int count = 0;
        long modified = 0;
        for (int i = from; i < ranked.Length; i++)
        {
            var child = ranked[i];
            if (child.Value(metric) <= 0)
                break;
            size += child.Size;
            apparent += child.Apparent;
            files += child.Files;
            folders += child.Folders + (child.IsFolder ? 1 : 0);
            modified = Math.Max(modified, child.Modified);
            count++;
        }
        return Item.Merged($"{count} smaller items", size, apparent, files, folders, modified);
    }

    /// <summary>Squarified rectangles for items sorted largest first, filling the area exactly.</summary>
    public static (float X, float Y, float W, float H)[] Squarify(ImmutableArray<Item> items, Metric metric, float x, float y, float w, float h)
    {
        int n = items.Length;
        var result = new (float X, float Y, float W, float H)[n];
        double total = 0;
        foreach (var item in items)
            total += item.Value(metric);
        var areas = new double[n];
        for (int i = 0; i < n; i++)
            areas[i] = items[i].Value(metric) / total * w * h;
        double rx = x;
        double ry = y;
        double rw = w;
        double rh = h;
        int start = 0;
        while (start < n)
        {
            double side = Math.Min(rw, rh);
            double sum = 0;
            double worst = double.MaxValue;
            int end = start;
            while (end < n)
            {
                double next = sum + areas[end];
                double ratio = Worst(areas[start], areas[end], next, side);
                if (end > start && ratio > worst)
                    break;
                worst = ratio;
                sum = next;
                end++;
            }
            bool last = end == n;
            if (rw >= rh)
            {
                double column = last ? rw : sum / rh;
                double cy = ry;
                for (int i = start; i < end; i++)
                {
                    double cell = i == end - 1 ? ry + rh - cy : areas[i] / column;
                    result[i] = ((float)rx, (float)cy, (float)column, (float)cell);
                    cy += cell;
                }
                rx += column;
                rw -= column;
            }
            else
            {
                double row = last ? rh : sum / rw;
                double cx = rx;
                for (int i = start; i < end; i++)
                {
                    double cell = i == end - 1 ? rx + rw - cx : areas[i] / row;
                    result[i] = ((float)cx, (float)ry, (float)cell, (float)row);
                    cx += cell;
                }
                ry += row;
                rh -= row;
            }
            start = end;
        }
        return result;
    }

    /// <summary>The worst aspect ratio of a row of areas between largest and smallest, summing to sum, along side.</summary>
    static double Worst(double largest, double smallest, double sum, double side)
    {
        if (sum <= 0 || smallest <= 0)
            return double.MaxValue;
        double s2 = side * side;
        double sum2 = sum * sum;
        return Math.Max(s2 * largest / sum2, sum2 / (s2 * smallest));
    }
}
