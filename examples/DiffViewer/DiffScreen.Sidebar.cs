using System.Collections;
using DiffViewer.Core;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffScreen
{
    const float FileRowHeight = 28;

    void Sidebar(Palette p)
    {
        var files = Model.Document.Files;
        using (C.Div(key: "sidebar").Width(SidebarWidth).FillHeight().Background(p.Surface).Semantics(Role.Group, "Changed files"))
        {
            using (C.Div(key: "heading").Row().FillWidth().Height(32).ItemsCenter().Px(Space.S3))
            {
                C.Text("Changed files", new(11, Weight.Semibold, p.Muted), key: "title");
                C.Spacer(key: "space");
                C.Text(files.Length.ToString(C.Culture), new(11, Color: p.Muted), key: "count");
            }
            using (C.Div(key: "files").FillWidth().Flex().MinHeight(0))
            {
                int current = Model.CursorFileIndex;
                C.VirtualList("list", new Uniform(files.Length, FileRowHeight), static i => (ulong)i + 1,
                    (_, row) => FileItem(p, files[row.Index], row.Index, row.Index == current),
                    new()
                    {
                        Overscan = 6,
                        Reveal = current >= 0 ? new(current, Model.Reveal) : null
                    });
            }
        }
        Rule(p, "sidebar-rule", vertical: true);
    }

    void FileItem(Palette p, FileDiff file, int index, bool selected)
    {
        using var item = C.Div(key: "file").Row().SizeFull().ItemsCenter().Gap(Space.S2).Px(Space.S3).TabStop(false)
            .Semantics(Role.Button, file.Path);
        var hit = item.Control();
        if (hit.Clicked)
            Ui.Dispatch(new Msg.SelectFile(index));
        item.Background(selected ? p.AccentSoft : hit.Hovered ? p.Hover : p.Surface);
        Badge(p, file.Status, "status");
        using (C.Div(key: "names").Row().ItemsCenter().Gap(6).Flex().MinWidth(0))
        {
            using (C.Div(key: "name").Shrink(0).MaxWidth(SidebarWidth - 110))
                C.Text(file.Name, new(12.5f, selected ? Weight.Semibold : Weight.Medium, p.Text)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "text");
            if (file.Directory.Length > 0)
                C.Text(file.Directory, new(11, Color: p.Muted)
                {
                    Overflow = TextOverflow.Ellipsis
                }, key: "directory");
        }
        if (file.Additions > 0)
            C.Text($"+{file.Additions.ToString(C.Culture)}", new(11, Color: p.AddedText), key: "added");
        if (file.Deletions > 0)
            C.Text($"−{file.Deletions.ToString(C.Culture)}", new(11, Color: p.RemovedText), key: "removed");
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
