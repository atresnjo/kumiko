using System.Collections.Immutable;
using DiskMap.Core;

namespace DiskMap.E2E;

/// <summary>Treemap geometry: proportional, non-overlapping, contained, squarish, bounded and stable in identity.</summary>
internal static class LayoutChecks
{
    public static void Run(Harness h)
    {
        Squarified(h);
        Nested(h);
        Merging(h);
        Identity(h);
        Metrics(h);
    }

    static Item Files(params long[] sizes) => Item.Folder("f", 0, 0, sizes.Select((s, i) => Item.File($"x{i}", s, s, 0)));

    static void Squarified(Harness h)
    {
        var folder = Files(600, 600, 400, 400, 300, 200, 200, 100, 60, 40, 30, 20, 10);
        var items = folder.Children;
        var rects = Treemap.Squarify(items, Metric.Disk, 10, 20, 600, 400);
        double total = items.Sum(i => i.Size);
        bool proportional = true;
        for (int i = 0; i < items.Length; i++)
            proportional &= Math.Abs(rects[i].W * rects[i].H - items[i].Size / total * 600 * 400) < 1;
        h.Check(proportional, "each area is its share of the whole");
        h.Check(Math.Abs(rects.Sum(r => (double)r.W * r.H) - 600 * 400) < 1, "the rectangles fill the area exactly");
        h.Check(rects.All(r => r.X >= 10 - .01 && r.Y >= 20 - .01 && r.X + r.W <= 610.01 && r.Y + r.H <= 420.01), "every rectangle stays inside the area");
        h.Check(!Overlap(rects), "no two rectangles overlap");
        double worst = rects.Max(r => Math.Max(r.W / r.H, r.H / r.W));
        // A slice layout cuts the long side into strips, one per item.
        double slice = items.Max(i => Math.Max(600 * (i.Size / total) / 400, 400 / (600 * (i.Size / total))));
        h.Timing("squarified-worst-aspect", worst);
        h.Check(worst < slice / 4 && worst < 6, $"squarified tiles stay near square ({worst:F2} against {slice:F0} for slices)");
        var empty = Treemap.Squarify(Files(1).Children, Metric.Disk, 0, 0, 0, 0);
        h.Check(empty.All(r => float.IsFinite(r.W) && float.IsFinite(r.H)), "a zero area gives finite rectangles");
    }

    static bool Overlap((float X, float Y, float W, float H)[] rects)
    {
        for (int i = 0; i < rects.Length; i++)
            for (int j = i + 1; j < rects.Length; j++)
            {
                var a = rects[i];
                var b = rects[j];
                if (Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X) > .01 && Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y) > .01)
                    return true;
            }
        return false;
    }

    static void Nested(Harness h)
    {
        var home = Demo.Home();
        var layout = Treemap.Build(home, 1, Metric.Disk, 1100, 700);
        var tiles = layout.Tiles;
        h.Check(tiles.All(t => float.IsFinite(t.X) && float.IsFinite(t.Y) && t.W > 0 && t.H > 0), "every tile has a finite positive size");
        bool contained = true;
        bool ordered = true;
        for (int i = 0; i < tiles.Length; i++)
        {
            var t = tiles[i];
            if (t.Parent < 0)
                contained &= t.X >= 0 && t.Y >= 0 && t.Right <= 1100.01 && t.Bottom <= 700.01;
            else
            {
                var p = tiles[t.Parent];
                ordered &= t.Parent < i && p.Shape == TileShape.Frame && t.Depth == p.Depth + 1;
                // Children sit below the header, inside the frame's inset.
                contained &= t.X >= p.X && t.Right <= p.Right + .01 && t.Y >= p.Y + Treemap.Header - .01 && t.Bottom <= p.Bottom + .01;
            }
        }
        h.Check(ordered, "every frame comes before the tiles inside it");
        h.Check(contained, "tiles stay inside their frame's body, below its header");
        var siblings = tiles.GroupBy(t => t.Parent).All(g => !Overlap([.. g.Select(t => (t.X, t.Y, t.W, t.H))]));
        h.Check(siblings, "tiles of the same folder never overlap");
        h.Check(tiles.Max(t => t.Depth) <= 2, "folders nest at most three levels deep");
        h.Check(tiles.Where(t => t.Shape == TileShape.Frame).All(t => t.W >= 72 && t.H >= 54), "a frame is large enough to show its header and contents");
        var keys = tiles.Select(t => t.Key).ToArray();
        h.Check(tiles.GroupBy(t => t.Parent).All(g => g.Select(t => t.Key).Distinct().Count() == g.Count()) && keys.Distinct().Count() == keys.Length,
            "tile keys are unique");
        var tiny = Treemap.Build(home, 1, Metric.Disk, 60, 40);
        h.Check(tiny.Tiles.All(t => t.Shape == TileShape.Leaf), "a small area shows leaves only");
        h.Check(Treemap.Build(home, 1, Metric.Disk, 0, 400).Tiles.IsEmpty, "a zero-width area lays out nothing");
    }

    static void Merging(Harness h)
    {
        var many = Item.Folder("many", 0, 0, Enumerable.Range(0, 20_000).Select(i => Item.File($"f{i}", 4096 + i % 7, 4096, 0))
            .Append(Item.File("huge", 400_000_000, 400_000_000, 0)));
        var layout = Treemap.Build(many, 1, Metric.Disk, 1000, 700);
        h.Check(layout.Tiles.Length <= 160, $"20k files make a bounded number of tiles ({layout.Tiles.Length})");
        var rest = layout.Tiles.Single(t => t.Item.Kind == ItemKind.Rest);
        h.Check(rest.Item.Files == 20_000 - (layout.Tiles.Length - 2) && rest.Item.Size == many.Size - layout.Tiles.Where(t => t.Item.Kind != ItemKind.Rest).Sum(t => t.Item.Size),
            "tiny items merge into one tile that keeps their total");
        h.Check(layout.Tiles.Where(t => t.Item.Kind != ItemKind.Rest).All(t => t.W * t.H >= 30), "no tile is drawn below a visible area");
        var budget = Treemap.Build(Demo.Home(), 1, Metric.Disk, 2400, 1600);
        h.Check(budget.Tiles.Length <= 860, $"a huge window keeps the tile budget ({budget.Tiles.Length})");
    }

    /// <summary>A folder's children have the same keys nested and opened, so they glide instead of snapping.</summary>
    static void Identity(Harness h)
    {
        var home = Demo.Home();
        var outer = Treemap.Build(home, 1, Metric.Disk, 1100, 700);
        var cargo = home.Child(".cargo")!.Value;
        var inner = Treemap.Build(cargo, Treemap.KeyOf(1, ".cargo"), Metric.Disk, 1100, 700);
        var nested = outer.Tiles.Where(t => t.Depth == 1 && outer.Tiles[t.Parent].Item.Name == ".cargo").Select(t => t.Key).ToHashSet();
        var opened = inner.Tiles.Where(t => t.Depth == 0).Select(t => t.Key).ToHashSet();
        h.Check(nested.Count > 0 && nested.IsSubsetOf(opened), "a nested tile keeps its key when its folder opens");
        var trail = Tree.Follow(home, [".cargo", "targets"]);
        h.Check(Treemap.KeyOf(trail) == Treemap.KeyOf(Treemap.KeyOf(1, ".cargo"), "targets"), "a trail's key matches its tiles' keys");
        h.Check(outer.NamesOf(outer.Tiles.ToList().FindIndex(t => t.Depth == 1 && outer.Tiles[t.Parent].Item.Name == ".cargo")).SequenceEqual([".cargo", "targets"]),
            "a nested tile names its path from the open folder");
    }

    static void Metrics(Harness h)
    {
        var folder = Item.Folder("m", 0, 0,
        [
            Item.File("big-sparse", 4096, 9_000_000_000, 0),
            Item.Folder("many", 0, 0, Enumerable.Range(0, 50).Select(i => Item.File($"f{i}", 8192, 100, 0))),
            Item.File("dense", 2_000_000, 2_000_000, 0)
        ]);
        h.Check(folder.Ranked(Metric.Disk)[0].Name == "dense" && folder.Ranked(Metric.Apparent)[0].Name == "big-sparse" && folder.Ranked(Metric.Files)[0].Name == "many",
            "each metric orders items by its own measure");
        var layout = Treemap.Build(folder, 1, Metric.Apparent, 800, 600);
        var sparse = layout.Tiles.Single(t => t.Item.Name == "big-sparse");
        h.Check(sparse.W * sparse.H > 800 * 600 * .9, "apparent size gives a sparse file its logical area");
        var model = DiskMapApp.Update(DiskMapApp.Show("/m", folder, 800, 600), new Msg.SetMetric(Metric.Files)).Item1;
        h.Check(model.Items[0].Name == "many" && model.Layout.Tiles[0].Item.Name == "many" && model.Glide, "switching the metric reorders the list and glides the map");
    }
}
