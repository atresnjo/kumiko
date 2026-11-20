using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Highlight;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffScreen
{
    /// <summary>
    /// Per-frame measurements shared by every row: the monospace advance and gutter widths. Press is set on the frame
    /// the pointer goes down on the diff itself, not on a control floating over it.
    /// </summary>
    readonly record struct Metrics(float Char, int Digits, float Gutter, float SplitGutter, float Half, bool Press);

    VirtualListView Pane(Palette p)
    {
        using var pane = C.Div(key: "pane").Flex().FillHeight().MinWidth(0).Background(p.Canvas).TabStop(false)
            .Semantics(Role.Group, "Diff");
        var hit = pane.Control();
        var layout = Model.Layout;
        if (layout.Count == 0)
        {
            Empty(p);
            return default;
        }
        var style = Code(p);
        float advance = C.TextSystem.Measure("0000000000", style).Width / 10;
        int digits = Math.Max(3, Model.Document.Files.Max(f => Math.Max(f.OldLines.Length, f.NewLines.Length)).ToString(C.Culture).Length);
        float width = (pane.PreviousRect?.Width ?? C.Size.Width - (SidebarShown ? SidebarWidth + 1 : 0));
        bool press = hit.Pressed && C.Input.Pressed && C.Input.Button == PointerButton.Primary;
        var metrics = new Metrics(advance, digits, (2 * digits + 3) * advance + 16, (digits + 2) * advance + 16, (width - 1) / 2, press);
        // Pointer moves drive the text cursor over code and a selection drag.
        C.WantsMoves = hit.Hovered || Model.Selection is { Dragging: true };
        float code = layout.MaxColumns * advance + 48;
        float content = Model.Mode == ViewMode.Unified ? metrics.Gutter + code : metrics.SplitGutter + code + metrics.Half + 1;
        var options = new VirtualListOptions
        {
            Overscan = 8,
            StickyHeader = layout.StickyHeader,
            ContentWidth = content,
            Reveal = new(Model.Cursor, Model.Reveal, Model.RevealMode, Model.RevealMode == VirtualRevealMode.Top ? 2 * RowLayout.LineHeight : RowLayout.LineHeight)
        };
        var view = C.VirtualList("rows", layout.Offsets, RowKey, (_, row) => RowView(p, metrics, row), options);
        // A drag whose line scrolled away still ends; rows dispatch their last drag before this.
        if (Model.Selection is { Dragging: true } && !C.Input.Down)
            Ui.Dispatch(new Msg.TextRelease());
        if (hit.Clicked && pane.PreviousRect is { } rect)
        {
            float y = C.Input.Pointer.Y - rect.Y + view.Offset;
            int row = layout.Offsets.BinarySearch(y);
            Ui.Dispatch(new Msg.Point(row >= 0 ? row : ~row - 1));
        }
        if (Model.Selection is { Dragging: false } selection)
            SelectionBar(p, selection);
        return view;
    }

    void Empty(Palette p)
    {
        using var empty = C.Div(key: "empty").SizeFull().Center().Gap(Space.S2);
        var title = Model.Error is not null ? "Could not load the diff" : Model.Loading ? "Loading…" : "No changes";
        C.Text(title, new(15, Weight.Semibold, p.Text), key: "title");
        var detail = Model.Error ?? (Model.Loading ? "" : "The compared sides are identical.");
        if (detail.Length > 0)
            C.Text(detail, new(12, Color: p.Muted, Wrap: true), key: "detail");
    }

    ulong RowKey(int index)
    {
        var row = Model.Layout.Rows[index];
        ulong value = ((ulong)(uint)row.File << 32) ^ ((ulong)row.Kind << 28) ^ ((ulong)(uint)(row.A + 1) * 0x9E3779B97F4A7C15UL) ^ (uint)(row.B + 1);
        value ^= value >> 31;
        value *= 0xBF58476D1CE4E5B9UL;
        return value ^ (value >> 29);
    }

    void RowView(Palette p, Metrics m, VirtualRow virtualRow)
    {
        var row = Model.Layout.Rows[virtualRow.Index];
        var file = Model.Document.Files[row.File];
        bool cursor = virtualRow.Index == Model.Cursor;
        switch (row.Kind)
        {
            case RowKind.File:
                FileHeader(p, file, row.File, virtualRow.Pinned, cursor);
                break;
            case RowKind.Hunk:
                HunkHeader(p, m, file.Hunks[row.A], cursor);
                break;
            case RowKind.Line:
                Unified(p, m, file, row.A, virtualRow, cursor);
                break;
            case RowKind.Pair:
                Split(p, m, file, row, virtualRow, cursor);
                break;
            case RowKind.Note:
                Note(p, file, cursor);
                break;
            default:
                using (C.Div(key: "end").SizeFull())
                {
                }
                break;
        }
    }

    void FileHeader(Palette p, FileDiff file, int index, bool pinned, bool cursor)
    {
        // Zen mode's only file never folds; its header says where the file sits among the others instead.
        bool zen = Model.Zen is not null;
        bool collapsed = Model.Collapsed.Contains(file.Path);
        using var header = C.Div(key: "file").Row().SizeFull().ItemsCenter().Gap(Space.S2).Px(Space.S3)
            .Background(cursor ? p.AccentSoft : p.Surface).TabStop(false);
        if (!zen)
        {
            header.Semantics(Role.Button, (collapsed ? "Expand " : "Collapse ") + file.Path);
            if (header.Control().Clicked)
                Ui.Dispatch(new Msg.ToggleFile(index));
        }
        if (pinned)
            header.Shadow(6, 2, Color.Black.Alpha(C.Theme.Dark ? .5f : .12f));
        if (!zen)
            C.Icon(collapsed ? Hugeicons.ArrowRight01 : Hugeicons.ArrowDown01, 14, p.Muted, key: "chevron");
        Badge(p, file.Status, "status");
        var directory = file.Directory;
        var path = directory.Length > 0 ? directory + "/" + file.Name : file.Name;
        C.RichText(path, [new(path.Length - file.Name.Length, path.Length, p.Text, Weight.Semibold)],
            new(12.5f, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "path");
        if (file.OldPath is { } old && file.Status == FileStatus.Renamed)
            C.Text("from " + old, new(12, Color: p.Muted)
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "renamed");
        C.Spacer(key: "space");
        if (zen)
            C.Text($"{(index + 1).ToString(C.Culture)} of {Model.Document.Files.Length.ToString(C.Culture)}   n p change   ← → file", new(12, Color: p.Muted), key: "position");
        C.Text($"+{file.Additions.ToString(C.Culture)}", new(12, Weight.Semibold, p.AddedText), key: "added");
        C.Text($"−{file.Deletions.ToString(C.Culture)}", new(12, Weight.Semibold, p.RemovedText), key: "removed");
        using (C.Div(key: "rule").Absolute().Offset(0, RowLayout.FileHeight - 1).FillWidth().Height(1).Background(p.Border))
        {
        }
    }

    void Badge(Palette p, FileStatus status, string key)
    {
        var (letter, color) = status switch
        {
            FileStatus.Added => ("A", p.AddedText),
            FileStatus.Deleted => ("D", p.RemovedText),
            FileStatus.Renamed => ("R", p.Accent),
            _ => ("M", p.HunkText)
        };
        using var badge = C.Div(key: key).Width(18).Height(18).Center().Rounded(4).Background(color);
        C.Text(letter, new(11, Weight.Bold, C.Theme.Dark ? p.Canvas : Color.White), key: "letter");
    }

    void HunkHeader(Palette p, Metrics m, Hunk hunk, bool cursor)
    {
        using var row = C.Div(key: "hunk").Row().SizeFull().ItemsCenter().Background(cursor ? p.AccentSoft : p.HunkLine);
        Cursor(p, cursor);
        using (C.Div(key: "gutter").Width(Model.Mode == ViewMode.Unified ? m.Gutter : m.SplitGutter).FillHeight())
        {
        }
        var text = $"@@ -{hunk.OldStart + (hunk.OldCount > 0 ? 1 : 0)},{hunk.OldCount} +{hunk.NewStart + (hunk.NewCount > 0 ? 1 : 0)},{hunk.NewCount} @@";
        C.Text(text, Code(p) with
        {
            Color = p.HunkText
        }, key: "range");
    }

    void Note(Palette p, FileDiff file, bool cursor)
    {
        using var row = C.Div(key: "note").Row().SizeFull().ItemsCenter().Px(Space.S5).Background(cursor ? p.AccentSoft : p.Canvas);
        var text = file.Omitted > 0 ? $"Too large to show over SSH ({(file.Omitted / 1048576.0).ToString("0.0", C.Culture)} MB); a local clone shows it"
            : file.Binary ? "Binary file not shown" : file.OldLines.IsEmpty && file.NewLines.IsEmpty ? "Empty file" : "No content changes";
        C.Text(text, new(12, Color: p.Muted), key: "text");
    }

    void Cursor(Palette p, bool cursor)
    {
        if (!cursor)
            return;
        using (C.Div(key: "cursor").Absolute().Offset(0, 0).Width(3).FillHeight().Background(p.Accent))
        {
        }
    }

    void Unified(Palette p, Metrics m, FileDiff file, int index, VirtualRow virtualRow, bool cursor)
    {
        var line = file.Lines[index];
        var (background, gutter, sign) = Tones(p, line.Kind);
        using var row = C.Div(key: "line").Row().SizeFull().Background(background);
        var oldNumber = line.Old >= 0 ? (line.Old + 1).ToString(C.Culture) : "";
        var newNumber = line.New >= 0 ? (line.New + 1).ToString(C.Culture) : "";
        var numbers = $"{oldNumber.PadLeft(m.Digits)} {newNumber.PadLeft(m.Digits)} {Sign(line.Kind)}";
        GutterText(p, m.Gutter, cursor ? p.AccentSoft : gutter, numbers, sign);
        Cursor(p, cursor);
        CodeText(p, m, file, index, line, virtualRow, cursor);
    }

    void Split(Palette p, Metrics m, FileDiff file, Row row, VirtualRow virtualRow, bool cursor)
    {
        using var split = C.Div(key: "pair").Row().SizeFull();
        Side(p, m, file, row.A, virtualRow, cursor, "old");
        using (C.Div(key: "divider").Width(1).FillHeight().Background(p.Border))
        {
        }
        Side(p, m, file, row.B, virtualRow, cursor, "new");
        Cursor(p, cursor);
    }

    void Side(Palette p, Metrics m, FileDiff file, int index, VirtualRow virtualRow, bool cursor, string key)
    {
        if (index < 0)
        {
            using (C.Div(key: key).Width(m.Half).FillHeight().Background(p.Empty))
            {
            }
            return;
        }
        var line = file.Lines[index];
        bool left = key == "old";
        var kind = line.Kind == LineKind.Context ? LineKind.Context : line.Kind;
        var (background, gutter, sign) = Tones(p, kind);
        using var side = C.Div(key: key).Row().Width(m.Half).FillHeight().Background(background).Clip();
        int number = (left ? line.Old : line.New) + 1;
        var text = $"{number.ToString(C.Culture).PadLeft(m.Digits)} {Sign(kind)}";
        GutterText(p, m.SplitGutter, cursor ? p.AccentSoft : gutter, text, sign);
        CodeText(p, m, file, index, line with
        {
            Kind = left && line.Kind == LineKind.Context ? LineKind.Removed : line.Kind
        }, virtualRow,
            cursor, left, left && line.Kind == LineKind.Context);
    }

    void GutterText(Palette p, float width, Color background, string text, Color sign)
    {
        using var gutter = C.Div(key: "gutter").Width(width).FillHeight().Row().ItemsCenter().Px(8).Background(background);
        var style = Code(p) with
        {
            Color = p.Muted
        };
        if (text[^1] == ' ')
            C.Text(text, style, key: "numbers");
        else
            C.RichText(text, [new(text.Length - 1, text.Length, sign)], style, key: "numbers");
    }

    /// <summary>
    /// The code cell clips and shifts by the shared horizontal offset, so the gutter stays in place. A context line
    /// shown on the left of a split row reads its old-side tokens. Selected text and its other occurrences are tinted.
    /// </summary>
    void CodeText(Palette p, Metrics m, FileDiff file, int index, DiffLine line, VirtualRow virtualRow, bool cursor, bool left = false, bool contextOnLeft = false)
    {
        bool old = line.Kind == LineKind.Removed;
        var text = old ? file.OldLines[line.Old] : file.NewLines[line.New];
        var tokens = old ? file.OldTokens : file.NewTokens;
        int number = old ? line.Old : line.New;
        var emphasis = contextOnLeft ? ImmutableArray<Emphasis>.Empty : file.Emphasis[index];
        var emphasisColor = old ? p.RemovedWord : p.AddedWord;
        var search = Model.Search is { Query.Length: > 0 } s ? s : null;
        bool current = cursor && search is { Found.Length: > 0 } && search.Found[search.Current] == Model.Cursor;
        var selection = Model.Selection is { Text.Length: > 0 } chosen ? chosen : null;
        bool mine = selection is not null && selection.Row == virtualRow.Index && selection.Line == index;
        var marks = new Marks(search?.Query, current, selection?.Text, mine ? selection!.Start : 0, mine ? selection!.End : 0);
        var spans = LineSpans.Build(text, tokens, number, emphasis, emphasisColor, marks, p);
        using var cell = C.Div(key: "code").Flex().FillHeight().MinWidth(0).Clip();
        using var shifted = C.Div(key: "shift").Absolute().Offset(10 - virtualRow.ScrollX, 0).Height(RowLayout.LineHeight).Row().ItemsCenter();
        TextPointer(p, m, cell, shifted, text, virtualRow.Index, index, left);
        if (spans is null)
            C.Text(text, Code(p), key: "text");
        else
            C.RichText(text, spans, Code(p), key: "text");
    }

    static (Color Background, Color Gutter, Color Sign) Tones(Palette p, LineKind kind) => kind switch
    {
        LineKind.Added => (p.AddedLine, p.AddedGutter, p.AddedText),
        LineKind.Removed => (p.RemovedLine, p.RemovedGutter, p.RemovedText),
        _ => (p.Canvas, p.Canvas, p.Faint)
    };

    static char Sign(LineKind kind) => kind switch
    {
        LineKind.Added => '+',
        LineKind.Removed => '-',
        _ => ' '
    };
}
