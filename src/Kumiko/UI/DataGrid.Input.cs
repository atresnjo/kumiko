namespace Kumiko.UI;

public sealed partial class DataGrid<T>
{
    bool Tab(Context c)
    {
        if (!IsEditing || menu.IsOpen)
            return false;
        int direction = (c.Input.Modifiers & Modifiers.Shift) != 0 ? -1 : 1;
        var display = DisplayRows();
        var displayed = Displayed;
        int row = SelectedIndex, column = selectedColumn;
        if (!CommitEdit())
            return true;
        // Sort may move the edited record; continue from its stable identity.
        row = SelectedIndex >= 0 ? SelectedIndex : row;
        var position = display.IndexOf(row);
        int cell = position * displayed.Count + Math.Max(0, IndexOfDisplayed(displayed, column));
        for (cell += direction; position >= 0 && cell >= 0 && cell < display.Count * displayed.Count; cell += direction)
        {
            var target = displayed[cell % displayed.Count];
            if (columns[target].Edit is null)
                continue;
            Select(display[cell / displayed.Count], target);
            BeginEdit();
            return true;
        }
        return false;
    }

    static int IndexOfDisplayed(IReadOnlyList<int> displayed, int column)
    {
        for (var i = 0; i < displayed.Count; i++)
            if (displayed[i] == column)
                return i;
        return -1;
    }

    void Keyboard(Context c, float viewportHeight)
    {
        if (menu.IsOpen || !c.FocusWithin(gridId))
            return;
        if (IsEditing)
        {
            if (c.Shortcut(Key.Escape, Modifiers.None))
            {
                CancelEdit();
                c.Changed = true;
            }
            else if (c.Shortcut(Key.Enter, Modifiers.None))
            {
                CommitEdit();
                c.Changed = true;
            }
            return;
        }
        if (c.Shortcut(Key.A))
        {
            SelectAll();
            c.Changed = true;
            return;
        }
        if (c.Shortcut(Key.C))
        {
            Copy(c);
            return;
        }
        var display = DisplayRows();
        if (display.Count == 0)
            return;
        var displayed = Displayed;
        var pressed = c.Logical(c.Input.Key);
        var shift = (c.Input.Modifiers & Modifiers.Shift) != 0;
        int position = Math.Max(0, display.IndexOf(SelectedIndex));
        int column = Math.Max(0, IndexOfDisplayed(displayed, selectedColumn));
        int page = Math.Max(1, (int)(viewportHeight / c.Theme.Controls.RowHeight) - 1);
        switch (pressed)
        {
            case Key.Up:
                position--;
                break;
            case Key.Down:
                position++;
                break;
            case Key.Left:
                column--;
                break;
            case Key.Right:
                column++;
                break;
            case Key.Home:
                position = c.Input.Primary ? 0 : position;
                column = 0;
                break;
            case Key.End:
                position = c.Input.Primary ? display.Count - 1 : position;
                column = displayed.Count - 1;
                break;
            case Key.PageUp:
                position -= page;
                break;
            case Key.PageDown:
                position += page;
                break;
            case Key.Enter:
            case Key.F2:
                BeginEdit();
                c.Input.Key = Key.None;
                c.Changed = true;
                return;
            case Key.F10 when shift:
                OpenMenu(c, new(40, 160), selectedColumn);
                c.Input.Key = Key.None;
                return;
            default:
                return;
        }
        position = Math.Clamp(position, 0, display.Count - 1);
        column = Math.Clamp(column, 0, displayed.Count - 1);
        var vertical = pressed is Key.Up or Key.Down or Key.PageUp or Key.PageDown || pressed is Key.Home or Key.End && c.Input.Primary;
        SelectRow(display[position], displayed[column], false, shift && vertical);
        focusCell = true;
        c.Input.Key = Key.None;
        c.Changed = true;
    }

    /// <summary>Copies the selected rows, or the focused row, as tab-separated text in displayed order.</summary>
    void Copy(Context c)
    {
        var copied = selection.IsEmpty ? SelectedRow is { } row ? [row] : [] : SelectedRows;
        if (copied.Count == 0)
            return;
        var text = Delimited(copied, '\t', false, false, "\r\n").TrimEnd('\r', '\n');
        c.Clipboard = text;
        c.WriteClipboard?.Invoke(text);
    }
}
