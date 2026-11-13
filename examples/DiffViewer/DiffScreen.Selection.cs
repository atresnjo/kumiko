using DiffViewer.Core;
using Kumiko.Icons;
using Kumiko.UI;

namespace DiffViewer;

internal sealed partial class DiffScreen
{
    const int Preview = 40;

    /// <summary>
    /// Starts, drags or extends a selection in one line from the pointer. Characters are found from the shifted text
    /// box's last-frame position; a drag follows only the side of a split row it started on.
    /// </summary>
    void TextPointer(Palette p, Metrics m, Div cell, Div shifted, string text, int row, int line, bool left)
    {
        if (cell.PreviousRect is not { } box || shifted.PreviousRect is not { } origin)
            return;
        var pointer = C.Input.Pointer;
        bool over = box.Contains(pointer);
        if (over)
            C.Cursor = Kumiko.UI.Cursor.Text;
        if (m.Press && over)
            Ui.Dispatch(new Msg.TextPress(row, line, Column(p, text, pointer.X - origin.X), C.Input.ClickCount, left,
                (C.Input.Modifiers & Modifiers.Shift) != 0));
        else if (!m.Press && Model.Selection is { Dragging: true } selection && selection.Row == row && selection.Line == line && selection.Left == left)
        {
            int column = Column(p, text, pointer.X - origin.X);
            if (column != selection.Head)
                Ui.Dispatch(new Msg.TextDrag(column));
        }
    }

    /// <summary>The character boundary nearest <paramref name="x"/> points from the text's left edge.</summary>
    int Column(Palette p, string text, float x)
    {
        var style = Code(p);
        int low = 0, high = text.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (C.TextSystem.Measure(text[..mid], style).Width <= x)
                low = mid;
            else
                high = mid - 1;
        }
        if (low < text.Length)
        {
            float before = C.TextSystem.Measure(text[..low], style).Width, after = C.TextSystem.Measure(text[..(low + 1)], style).Width;
            if (x - before > after - x)
                low++;
        }
        if (low > 0 && low < text.Length && char.IsLowSurrogate(text[low]))
            low--;
        return low;
    }

    /// <summary>Floats over the diff's bottom-right corner, so it never moves the rows under the pointer.</summary>
    void SelectionBar(Palette p, Selection selection)
    {
        using var bar = C.Div(key: "selection").Absolute().Inset(LayoutEdge.Right, Space.S4).Inset(LayoutEdge.Bottom, Space.S4).Row()
            .Height(38).MaxWidth(520).ItemsCenter().Gap(Space.S2).Padding(new Edges(0, 4, 0, Space.S3)).Rounded(8).Background(p.Raised)
            .Border(p.Border).Shadow(16, 6, Color.Black.Alpha(C.Theme.Dark ? .4f : .14f)).Semantics(Role.Group, "Selection");
        if (bar.PreviousRect is { } rect && rect.Contains(C.Input.Pointer))
            C.Cursor = Kumiko.UI.Cursor.Arrow;
        var text = selection.Text.Length > Preview ? selection.Text[..Preview] + "…" : selection.Text;
        C.Text(text, new(12, Mono: true, Color: p.Text)
        {
            Overflow = TextOverflow.Ellipsis
        }, key: "text");
        var found = selection.Found;
        var more = found.Length >= TextMatches.Limit ? "+" : "";
        var matches = found.Length < 2 ? "No other matches" : $"{(selection.Current + 1).ToString(C.Culture)} of {found.Length.ToString(C.Culture)}{more}";
        C.Text(matches, new(12, Color: p.Muted), key: "count");
        if (found.Length > 1)
        {
            IconButton(p, Hugeicons.ArrowUp01, "Previous occurrence", "previous", new Msg.SelectionStep(-1));
            IconButton(p, Hugeicons.ArrowDown01, "Next occurrence", "next", new Msg.SelectionStep(1));
        }
        IconButton(p, Hugeicons.Cancel01, "Clear selection", "clear", new Msg.ClearSelection());
    }
}
