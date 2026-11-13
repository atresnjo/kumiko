using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiskMap;

/// <summary>
/// The treemap. Every tile is an absolute child of one clipped area, keyed by its path, so a tile keeps its
/// identity when it moves between depths: opening a folder glides its contents from their nested places.
/// </summary>
internal sealed partial class MapScreen
{
    void Map(Palette p)
    {
        using var well = C.Div(key: "well").Flex().FillWidth().MinHeight(0).Padding(new Edges(0, Space.S4, Space.S4, Space.S4));
        using var map = C.Div(key: "map").SizeFull().Clip().Semantics(Role.Group, "Treemap");
        if (map.PreviousRect is { } rect)
        {
            if (MathF.Abs(rect.Width - Model.Width) >= .5f || MathF.Abs(rect.Height - Model.Height) >= .5f)
                Ui.Dispatch(new Msg.Resized(rect.Width, rect.Height));
        }
        else
            C.FrameIn(TimeSpan.Zero);
        var layout = Model.Layout;
        if (layout.Tiles.IsEmpty)
        {
            using var empty = C.Div(key: "empty").SizeFull().Center().Gap(Space.S2);
            C.Icon(Hugeicons.FolderOpen, 28, p.Faint, key: "icon");
            C.Text(Model.Folder is { Opens: true } ? "Measuring…" : "This folder is empty", new(13, Weight.Medium, p.Muted), key: "text");
            return;
        }
        var motion = Model.Glide ? Motion.Standard : Motion.None;
        ulong selected = Model.Selected is { } pick ? KeyOf(pick.Names) : 0;
        Span<ulong> marked = stackalloc ulong[Math.Min(64, Model.Marked.Count)];
        int count = 0;
        foreach (var path in Model.Marked)
        {
            if (count == marked.Length)
                break;
            if (DiskMap.Core.Tree.Names(Model.Root, path) is { } names)
                marked[count++] = Rooted(names);
        }
        var parent = Model.Folder!.Value.Value(Model.Metric);
        for (int i = 0; i < layout.Tiles.Length; i++)
        {
            var tile = layout.Tiles[i];
            bool isMarked = marked[..count].Contains(tile.Key);
            DrawTile(p, layout, i, motion, tile.Key == selected, isMarked, parent);
        }
    }

    /// <summary>The tile key of an item below the open folder.</summary>
    ulong KeyOf(ImmutableArray<string> names)
    {
        ulong key = Model.FolderKey;
        foreach (var name in names)
            key = Treemap.KeyOf(key, name);
        return key;
    }

    /// <summary>The tile key of an item below the scanned root.</summary>
    static ulong Rooted(ImmutableArray<string> names)
    {
        ulong key = 1;
        foreach (var name in names)
            key = Treemap.KeyOf(key, name);
        return key;
    }

    void DrawTile(Palette p, TreemapLayout layout, int index, Motion motion, bool selected, bool marked, long parent)
    {
        var tile = layout.Tiles[index];
        var node = tile.Item;
        bool frame = tile.Shape == TileShape.Frame;
        bool file = node.Kind != ItemKind.Folder;
        bool dimmed = Model.Filter.Length > 0 && !DiskMapApp.Matches(node, Model.Filter);
        float radius = tile.Depth == 0 ? 7 : tile.Depth == 1 ? 5 : 4;
        using var div = C.Div(key: tile.Key).Absolute().Offset(tile.X, tile.Y, motion).Width(tile.W, motion).Height(tile.H, motion)
            .Rounded(MathF.Min(radius, MathF.Min(tile.W, tile.H) / 3)).Clip()
            .Enter(Transition.Fade with
            {
                Motion = Motion.Quick
            })
            .Exit(Transition.Fade with
            {
                Motion = Motion.Quick
            });
        var hue = node.Kind == ItemKind.Rest ? p.RestHue : Palette.Hue(tile.Hue);
        Interaction hit;
        if (frame)
        {
            // A frame's children cover its body, so only its header takes the pointer; targets never overlap.
            using var header = C.Div(key: "header").Row().FillWidth().Height(Treemap.Header).ItemsCenter().Gap(6).Px(7);
            hit = Bind(layout, index, header, selected);
            Header(tile, p.Tile(hue, tile.Depth, true, file, hit.Hovered, dimmed), hit.Hovered);
        }
        else
            hit = Bind(layout, index, div, selected);
        var colors = p.Tile(hue, tile.Depth, frame, file, hit.Hovered, dimmed);
        div.Background(colors.Fill);
        if (selected)
            div.Border(p.Accent, 2);
        else if (marked)
            div.Border(p.Mark, 2);
        else
            div.Border(colors.Edge, 1);
        if (!frame && !dimmed)
            Label(tile, colors, parent);
        if (marked && tile.W >= 26 && tile.H >= 26)
            Badge(p, tile.W);
    }

    /// <summary>Makes a tile's target a button: a click selects, a second click or double click opens, right-click shows the menu.</summary>
    Interaction Bind(TreemapLayout layout, int index, Div target, bool selected)
    {
        var tile = layout.Tiles[index];
        target.TabStop(false).Semantics(Role.Button, $"{tile.Item.Name}, {Format.Value(tile.Value, Model.Metric, C.Culture)}");
        var hit = target.Control();
        if (hit.Clicked || hit.DoubleClicked)
        {
            var names = layout.NamesOf(index);
            Ui.Dispatch(hit.DoubleClicked || selected && tile.Item.Opens ? new Msg.Open(names) : new Msg.Select(names));
        }
        if (hit.Hovered)
        {
            var names = layout.NamesOf(index);
            Ui.OnContextMenu(hit, at => new Msg.OpenMenu(names, at));
        }
        return hit;
    }

    /// <summary>A folder's header strip: name, size and an open arrow when hovered.</summary>
    void Header(Tile tile, TileColors colors, bool hovered)
    {
        using (C.Div(key: "name").Flex().MinWidth(0))
            C.Text(tile.Item.Name, new(11.5f, Weight.Semibold, colors.Text)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "text");
        if (tile.W >= 130)
            C.Text(Format.Value(tile.Value, Model.Metric, C.Culture), Mono(10.5f, colors.Detail), key: "size");
        if (hovered && tile.W >= 96)
            C.Icon(Hugeicons.ArrowUpRight01, 12, colors.Text, key: "open");
    }

    /// <summary>
    /// A leaf's text, sized to its tile: a large figure on big tiles, name and size on medium ones, a name alone on
    /// small ones and nothing on the smallest.
    /// </summary>
    void Label(Tile tile, TileColors colors, long parent)
    {
        float w = tile.W;
        float h = tile.H;
        if (w < 34 || h < 17)
            return;
        var name = new TextStyle(w >= 170 && h >= 110 ? 12.5f : 11.5f, Weight.Semibold, colors.Text)
        {
            Overflow = TextOverflow.Ellipsis
        };
        using var labels = C.Div(key: "labels").Column().FillWidth().Px(h >= 30 ? 8 : 5).Py(h >= 30 ? 6 : 2).Gap(h >= 110 ? 4 : 1);
        C.Text(tile.Item.Name, name, key: "name");
        if (h < 40 || w < 52)
            return;
        bool big = w >= 170 && h >= 110;
        var value = Format.Value(tile.Value, Model.Metric, C.Culture);
        C.Text(value, Mono(big ? MathF.Min(30, 14 + MathF.Min(w, h) / 22) : 11, big ? colors.Text : colors.Detail, big ? Weight.Medium : Weight.Regular), key: "size");
        if (big && parent > 0)
            C.Text(Format.Share((double)tile.Value / parent, C.Culture) + " of folder", Mono(11, colors.Detail), key: "share");
    }

    void Badge(Palette p, float width)
    {
        using var badge = C.Div(key: "badge").Absolute().Offset(width - 20, 4).Width(16).Height(16).Rounded(8).Center().Background(p.Mark);
        C.Icon(Hugeicons.Tick02, 11, p.Canvas, key: "tick");
    }
}
