using System.Collections;
using DiskMap.Core;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiskMap;

/// <summary>Details of the selection (or the open folder), the folder's items as a list, and the volume's space.</summary>
internal sealed partial class MapScreen
{
    const float RowHeight = 34;

    void Sidebar(Palette p)
    {
        using var side = C.Div(key: "sidebar").Column().Width(SidebarWidth).FillHeight().Background(p.Surface).Semantics(Role.Group, "Details");
        Details(p);
        Rule(p, "details-rule");
        Items(p);
        if (Model.Volume is { Total: > 0 } volume)
        {
            Rule(p, "volume-rule");
            DiskSpace(p, volume);
        }
    }

    void Details(Palette p)
    {
        var folder = Model.Folder!.Value;
        var node = Model.Selected?.Item ?? folder;
        bool own = Model.Selected is null;
        var path = own ? Model.FolderPath : node.Kind == ItemKind.Rest ? Model.FolderPath : Model.PathOf(Model.Selected!);
        long value = node.Value(Model.Metric);
        long whole = (own ? Model.Tree!.Value : folder).Value(Model.Metric);
        double share = whole > 0 ? (double)value / whole : 0;
        using var card = C.Div(key: "details").Column().FillWidth().Padding(Space.S4).Gap(Space.S3);
        using (C.Div(key: "title").Row().FillWidth().ItemsCenter().Gap(Space.S2))
        {
            var icon = node.Kind switch
            {
                ItemKind.Folder => Hugeicons.Folder01,
                ItemKind.Rest => Hugeicons.Layers01,
                ItemKind.Volume => Hugeicons.HardDrive,
                ItemKind.Link => Hugeicons.Link01,
                _ => Hugeicons.File01
            };
            C.Icon(icon, 16, p.Muted, key: "icon");
            using (C.Div(key: "name").Flex().MinWidth(0))
                C.Text(node.Name, new(14, Weight.Semibold, p.Text)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "text");
        }
        C.Text(Abbreviate(path), Mono(11, p.Muted) with
        {
            Overflow = TextOverflow.Ellipsis
        }, key: "path");
        C.Text(Format.Value(value, Model.Metric, C.Culture), Mono(30, p.Text, Weight.Medium), key: "size");
        Bar(p, "share", share, p.Accent);
        using (C.Div(key: "facts").Row().FillWidth().Gap(Space.S3))
        {
            using (C.Div(key: "left").Column().Flex().Gap(Space.S3))
            {
                Fact(p, own ? "Of scan" : "Of folder", Format.Share(share, C.Culture), "share");
                Fact(p, "Modified", Format.Date(node.Modified, C.Culture), "modified");
            }
            using (C.Div(key: "right").Column().Flex().Gap(Space.S3))
            {
                Fact(p, "Files", Format.Count(node.Files, C.Culture), "files");
                if (node.Kind == ItemKind.Folder)
                    Fact(p, "Largest", node.Largest?.Name ?? "—", "largest");
                else
                    Fact(p, Model.Metric == Metric.Apparent ? "On disk" : "Apparent",
                        Format.Bytes(Model.Metric == Metric.Apparent ? node.Size : node.Apparent, C.Culture), "other");
            }
        }
    }

    void Fact(Palette p, string label, string value, string key)
    {
        using var fact = C.Div(key: key).Column().FillWidth().Gap(3);
        C.Text(label.ToUpperInvariant(), new(10, Weight.Semibold, p.Faint), key: "label");
        C.Text(value, Mono(13, p.Text) with
        {
            Overflow = TextOverflow.Ellipsis
        }, key: "value");
    }

    void Bar(Palette p, string key, double share, Color color, float height = 4)
    {
        using var track = C.Div(key: key).FillWidth().Height(height).Rounded(height / 2).Background(p.Well).Clip();
        var width = track.PreviousRect?.Width ?? SidebarWidth - 32;
        using (C.Div(key: "fill").Height(height).Width((float)Math.Clamp(share, 0, 1) * width, Motion.Standard).Rounded(height / 2).Background(color))
        {
        }
    }

    /// <summary>The open folder's items, largest first by the metric, virtual so huge folders stay cheap.</summary>
    void Items(Palette p)
    {
        var items = Model.Items;
        using (C.Div(key: "heading").Row().FillWidth().Height(30).ItemsCenter().Px(Space.S4))
        {
            C.Text(Model.Filter.Length > 0 ? $"{Format.Count(items.Length, C.Culture)} MATCHING" : $"{Format.Count(items.Length, C.Culture)} ITEMS",
                new(10, Weight.Semibold, p.Faint), key: "count");
            C.Spacer(key: "space");
            C.Text(Model.Metric switch
            {
                Metric.Disk => "DISK SIZE",
                Metric.Apparent => "APPARENT",
                _ => "FILES"
            }, new(10, Weight.Semibold, p.Faint), key: "metric");
        }
        using var list = C.Div(key: "items").FillWidth().Flex().MinHeight(0);
        int selected = Model.Selected is { Names.Length: 1 } pick ? items.IndexOf(pick.Item) : -1;
        long largest = items.IsEmpty ? 1 : Math.Max(1, items[0].Value(Model.Metric));
        C.VirtualList("rows", new Uniform(items.Length, RowHeight), static i => (ulong)i + 1,
            (_, row) => Row(p, items[row.Index], row.Index, row.Index == selected, largest),
            new()
            {
                Overscan = 6,
                Reveal = selected >= 0 ? new(selected, Model.Reveal) : null
            });
    }

    void Row(Palette p, Item node, int index, bool selected, long largest)
    {
        using var row = C.Div(key: "row").Row().SizeFull().ItemsCenter().Gap(Space.S2).Px(Space.S4).TabStop(false)
            .Semantics(Role.ListItem, node.Name);
        var hit = row.Control();
        if (hit.DoubleClicked)
            Ui.Dispatch(new Msg.Open([node.Name]));
        else if (hit.Clicked)
            Ui.Dispatch(new Msg.Select([node.Name]));
        if (hit.Hovered)
        {
            var name = node.Name;
            Ui.OnContextMenu(hit, at => new Msg.OpenMenu([name], at));
        }
        row.Background(selected ? p.AccentSoft : hit.Hovered ? p.Hover : p.Surface);
        int hue = Model.Layout.Tiles.Length > 0 ? HueOf(node) : index;
        using (C.Div(key: "swatch").Width(3).Height(18).Rounded(2).Background(hue < 0 ? p.Faint : Palette.Hue(hue)))
        {
        }
        using (C.Div(key: "names").Column().Flex().MinWidth(0).Gap(4))
        {
            using (C.Div(key: "line").Row().FillWidth().ItemsCenter().Gap(6))
            {
                if (node.Opens)
                    C.Icon(Hugeicons.ArrowRight01, 11, p.Faint, key: "chevron");
                using (C.Div(key: "name").Flex().MinWidth(0))
                    C.Text(node.Name, new(12.5f, selected ? Weight.Semibold : Weight.Medium, p.Text)
                    {
                        Overflow = TextOverflow.Ellipsis
                    }, key: "text");
            }
            using (C.Div(key: "track").FillWidth().Height(2).Rounded(1).Background(p.Well))
            using (C.Div(key: "fill").Height(2).Rounded(1).Width(Length.Percent((float)(100.0 * node.Value(Model.Metric) / largest))).Background(selected ? p.Accent : p.Faint.Alpha(.6f)))
            {
            }
        }
        if (Model.Marked.Count > 0 && Model.Marked.Contains(Model.PathOf(new([node.Name], node))))
            C.Icon(Hugeicons.Tick02, 12, p.Mark, key: "marked");
        C.Text(Format.Value(node.Value(Model.Metric), Model.Metric, C.Culture), Mono(11.5f, p.Muted), key: "value");
    }

    /// <summary>The hue a top-level tile of this item has, or -1 when it was merged into small items.</summary>
    int HueOf(Item node)
    {
        foreach (var tile in Model.Layout.Tiles)
        {
            if (tile.Depth > 0)
                break;
            if (tile.Item == node)
                return tile.Hue;
        }
        return -1;
    }

    void DiskSpace(Palette p, Volume volume)
    {
        using var card = C.Div(key: "volume").Column().FillWidth().Padding(Space.S4).Gap(Space.S2);
        C.Text("DISK SPACE", new(10, Weight.Semibold, p.Faint), key: "label");
        C.Text(Format.Bytes(volume.Free, C.Culture) + " free", Mono(20, p.Text, Weight.Medium), key: "free");
        Bar(p, "used", (double)volume.Used / volume.Total, p.Accent, 6);
        using (C.Div(key: "numbers").Row().FillWidth())
        {
            C.Text(Format.Bytes(volume.Used, C.Culture) + " used", Mono(11, p.Muted), key: "used");
            C.Spacer(key: "space");
            C.Text(Format.Bytes(volume.Total, C.Culture) + " total", Mono(11, p.Muted), key: "total");
        }
    }

    /// <summary>Offsets of equally tall rows, computed on demand instead of stored.</summary>
    sealed class Uniform(int count, float height) : IReadOnlyList<float>
    {
        public float this[int index] => index * height;
        public int Count => count + 1;

        public IEnumerator<float> GetEnumerator()
        {
            for (int i = 0; i <= count; i++)
                yield return i * height;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
