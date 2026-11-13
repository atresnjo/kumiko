using DiffViewer.Core;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>
/// Text selection: characters selected in one line select every other place that text shows, and the selection
/// steps between them.
/// </summary>
internal sealed partial class DiffApp
{
    static Model Press(Model model, Msg.TextPress press)
    {
        if (!Shows(model, press.Row, press.Line))
            return model;
        var text = Text(model, press.Row, press.Line);
        int column = Math.Clamp(press.Column, 0, text.Length);
        var pointed = model with
        {
            Cursor = press.Row
        };
        if (press.Extend && model.Selection is { } extended && extended.Row == press.Row && extended.Line == press.Line && extended.Left == press.Left)
            return Drag(pointed, extended with
            {
                Dragging = true
            }, column);
        if (press.Clicks >= 2)
        {
            var (start, end) = TextMatches.Around(text, column, press.Clicks);
            return Release(pointed, new()
            {
                Row = press.Row,
                Line = press.Line,
                Left = press.Left,
                Anchor = start,
                Head = end,
                Text = text[start..end]
            });
        }
        return pointed with
        {
            Selection = new()
            {
                Row = press.Row,
                Line = press.Line,
                Left = press.Left,
                Anchor = column,
                Head = column,
                Dragging = true
            }
        };
    }

    static Model Drag(Model model, Selection selection, int column)
    {
        var text = Text(model, selection.Row, selection.Line);
        var dragged = selection with
        {
            Head = Math.Clamp(column, 0, text.Length)
        };
        return model with
        {
            Selection = dragged with
            {
                Text = text[dragged.Start..dragged.End]
            }
        };
    }

    /// <summary>Finds every occurrence and makes the selection's own current; an empty selection clears.</summary>
    static Model Release(Model model, Selection selection)
    {
        if (selection.Text.Length == 0)
            return model with
            {
                Selection = null
            };
        var found = TextMatches.Find(model.Document.Files, model.Layout, selection.Text);
        var own = new Occurrence(selection.Row, selection.Line, selection.Start);
        int current = 0;
        while (current < found.Length - 1 && Before(found[current], own))
            current++;
        return model with
        {
            Selection = selection with
            {
                Dragging = false,
                Found = found,
                Current = current
            }
        };
    }

    /// <summary>Moves the selection to the next or previous occurrence, wrapping, and the cursor with it.</summary>
    static Model Step(Model model, Selection selection, int direction)
    {
        var found = selection.Found;
        int current = ((selection.Current + direction) % found.Length + found.Length) % found.Length;
        return Cursor(model with
        {
            Selection = On(model.Layout, selection, current)
        }, found[current].Row, VirtualRevealMode.Top);
    }

    /// <summary>
    /// After the rows change, finds the selected text again and moves the selection to the first occurrence at or
    /// after the cursor. Text no shown line holds any more clears it.
    /// </summary>
    static Model Reselect(Model model)
    {
        if (model.Selection is not { Dragging: false } selection)
            return model.Selection is null ? model : model with
            {
                Selection = null
            };
        var found = TextMatches.Find(model.Document.Files, model.Layout, selection.Text);
        if (found.IsEmpty)
            return model with
            {
                Selection = null
            };
        int current = 0;
        while (current < found.Length - 1 && found[current].Row < model.Cursor)
            current++;
        return model with
        {
            Selection = On(model.Layout, selection with
            {
                Found = found
            }, current)
        };
    }

    /// <summary>The selection moved onto occurrence <paramref name="current"/>; an old line of a split row is its left side.</summary>
    static Selection On(RowLayout layout, Selection selection, int current)
    {
        var at = selection.Found[current];
        var row = layout.Rows[at.Row];
        return selection with
        {
            Current = current,
            Row = at.Row,
            Line = at.Line,
            Left = row.Kind == RowKind.Pair && row.A == at.Line && row.A != row.B,
            Anchor = at.Start,
            Head = at.Start + selection.Text.Length
        };
    }

    static bool Before(Occurrence a, Occurrence b) => a.Row != b.Row ? a.Row < b.Row : a.Line != b.Line ? a.Line < b.Line : a.Start < b.Start;

    /// <summary>Whether a row of the layout shows the line, so a press from an older frame still applies.</summary>
    static bool Shows(Model model, int row, int line)
    {
        if (row < 0 || row >= model.Layout.Count)
            return false;
        var shown = model.Layout.Rows[row];
        return shown.Kind is RowKind.Line or RowKind.Pair && (shown.A == line || shown.B == line) && line >= 0;
    }

    static string Text(Model model, int row, int line)
    {
        var file = model.Document.Files[model.Layout.Rows[row].File];
        return file.Text(file.Lines[line]);
    }
}
