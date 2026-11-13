namespace Kumiko.UI;

public sealed partial class DataGrid<T>
{
    string? pendingGroup;

    public void Draw(Context c, NodeKey key = default)
    {
        var t = c.Theme;
        strings = c.Strings;
        if (!Equals(culture, c.Culture))
        {
            culture = c.Culture;
            SortRows();
        }
        var displayed = Displayed;
        using var grid = c.Div(key.Value is null ? "grid" : key).SizeFull().Gap(0)
            .Border(t.Border).Padding(1).Rounded(t.Radius.Md).Clip().Semantics(Role.Grid, Name.Length > 0 ? Name : strings.DataGrid)
            .Accessibility(new(RowCount: rows.Length, ColumnCount: columns.Count));
        gridId = grid.Id;
        grid.Control();
        c.PreviewTab(grid.Id, context =>
        {
            var handled = Tab(context);
            if (handled)
                StateChanged?.Invoke(Capture());
            return handled;
        });
        var tableWidth = displayed.Sum(i => ColumnWidth(columns[i].Id));
        using (c.Div(key: "horizontal").FillWidth().Grow().ScrollX())
        using (c.Div(key: "table").Width(Math.Max(tableWidth, grid.PreviousRect?.Width ?? 0)).FillHeight())
        {
            var headerFocused = Headers(c, displayed);
            if (!headerFocused)
                Keyboard(c, (grid.PreviousRect?.Height ?? c.Size.Height) - t.Controls.HeaderHeight);
            c.Divider(key: "divider");
            using var viewport = c.Div(key: "viewport").FillWidth().Grow();
            if (rows.Length == 0)
            {
                using var empty = c.Div(key: "empty").SizeFull().Center().Gap(t.Space.S2);
                c.Text(strings.NoRecords, t.Typography.Label, key: "title");
                c.Text(EmptyDescription.Length > 0 ? EmptyDescription : strings.NoRecordsHint, t.Typography.Body with
                {
                    Color = t.Muted
                }, key: "hint");
            }
            else
            {
                var revealEntry = reveal && SelectedIndex >= 0 ? entries.FindIndex(e => e.Group is null && e.Row == SelectedIndex) : -1;
                c.UniformList("rows", entries.Count, t.Controls.RowHeight, (context, i) => DrawEntry(context, i, displayed),
                    revealRow: revealEntry >= 0 ? revealEntry : null, rowKey: EntryKey);
                if (pendingGroup is { } group)
                    ToggleGroup(group);
                pendingGroup = null;
            }
        }
        if (error is not null)
        {
            using var message = c.Div(key: "error").FillWidth().Padding(t.Space.S3).Background(t.ErrorSurface).Semantics(Role.Alert, error);
            c.Text(error, t.Typography.Body with
            {
                Color = t.Danger
            }, key: "text");
        }
        reveal = false;
        focusCell = false;
        menu.Draw(c, $"grid-menu:{key.Value}");
    }

    ulong EntryKey(int index) => entries[index] is { Group: { } group } ? Context.Hash("\u0001group:" + group) : key(rows[entries[index].Row]);

    /// <summary>Builds sortable, resizable, reorderable headers; returns true when one has keyboard focus.</summary>
    bool Headers(Context c, IReadOnlyList<int> displayed)
    {
        var t = c.Theme;
        var focused = false;
        var rects = new List<(string Id, Rect? Rect)>(displayed.Count);
        using (c.Div(key: "header").Row().FillWidth().Height(t.Controls.HeaderHeight).Background(t.Surface))
        {
            foreach (var index in displayed)
            {
                var column = columns[index];
                var width = ColumnWidth(column.Id);
                using var header = c.Div(key: column.Id).Row().Width(width).FillHeight().ItemsCenter()
                    .Px(t.Space.S3).Semantics(Role.ColumnHeader, column.Header);
                var hit = header.Control();
                rects.Add((column.Id, header.PreviousRect));
                if (hit.Hovered || hit.Focused || drag?.Column == column.Id && drag.Moved)
                    header.Background(t.SurfaceHover);
                if (hit.Focused)
                {
                    HeaderKeys(c, column.Id);
                    focused = true;
                }
                c.Text(column.Header, t.Typography.Label with
                {
                    Color = t.Muted
                }, key: "label");
                if (SortColumn == index)
                {
                    c.Spacer(key: "space");
                    c.Icon(Descending ? "chevron.down" : "chevron.up", 12, t.Muted, key: "sort");
                }
                if (hit.Pressed && c.Input.Pressed && drag is null)
                    drag = new(column.Id, false, c.Input.Pointer.X, width);
                if (hit.Clicked && drag is not { Moved: true })
                {
                    Descending = SortColumn == index && !Descending;
                    SortColumn = index;
                    SortRows();
                    reveal = true;
                    c.Changed = true;
                }
                ResizeZone(c, column, width);
            }
        }
        TrackDrag(c, rects);
        return focused;
    }

    void ResizeZone(Context c, DataGridColumn<T> column, float width)
    {
        var t = c.Theme;
        using var zone = c.Div(key: "resize").Absolute().Offset(width - t.Space.S3 - 6, 0).Width(6).FillHeight().TabStop(false)
            .Semantics(Role.Separator, column.Header)
            .Accessibility(new(Minimum: column.MinWidth, Number: width, Step: ResizeStep), request =>
            {
                if (request.Action != AccessibilityAction.SetRange)
                    return false;
                SetWidth(column.Id, (float)request.Number);
                StateChanged?.Invoke(Capture());
                return true;
            });
        var hit = zone.Control();
        if (hit.Hovered || hit.Pressed)
        {
            c.Cursor = Cursor.ResizeHorizontal;
            using (c.Div(key: "rule").Absolute().Offset(5, 0).Width(1).FillHeight().Background(t.FocusRing))
            {
            }
        }
        if (hit.Pressed && c.Input.Pressed)
            drag = new(column.Id, true, c.Input.Pointer.X, width);
    }

    void DrawEntry(Context c, int index, IReadOnlyList<int> displayed)
    {
        var entry = entries[index];
        if (entry.Group is { } group)
            GroupRow(c, group, entry.Count);
        else
            Row(c, entry.Row, displayed);
    }

    void GroupRow(Context c, string group, int count)
    {
        var t = c.Theme;
        var column = columns.First(col => col.Id == groupBy);
        var open = !collapsed.Contains(group);
        var label = c.Format(strings.Group, column.Header, group, count);
        using var line = c.Div(key: "group").Row().SizeFull().ItemsCenter().Gap(t.Space.S2).Px(t.Space.S3).Background(t.Surface)
            .Semantics(Role.Row, label).Accessibility(new(Expanded: open), request =>
            {
                if (request.Action is not (AccessibilityAction.Expand or AccessibilityAction.Collapse) ||
                    request.Action == AccessibilityAction.Expand == open)
                    return false;
                ToggleGroup(group);
                StateChanged?.Invoke(Capture());
                return true;
            });
        var hit = line.Control();
        var pressed = c.Logical(c.Input.Key);
        if (hit.Clicked || hit.Focused && (pressed == Key.Left && open || pressed == Key.Right && !open))
        {
            if (!hit.Clicked)
                c.Input.Key = Key.None;
            // Applied after the list so its entries stay stable while rows are built.
            pendingGroup = group;
            c.Changed = true;
        }
        c.Icon(open ? "chevron.down" : "chevron.right", 12, t.Muted, key: "chevron");
        c.Text(label, t.Typography.Label, key: "label");
    }

    void Row(Context c, int rowIndex, IReadOnlyList<int> displayed)
    {
        var t = c.Theme;
        var row = rows[rowIndex];
        ulong rowId = key(row);
        var selected = IsSelected(rowId) || SelectedId == rowId && selection.IsEmpty;
        using var line = c.Div(key: rowId).Row().SizeFull().Semantics(Role.Row, c.Format(strings.Record, rowId))
            .Accessibility(new(Selected: selected, Row: rowIndex));
        if (selected)
            line.Background(t.Selection);
        foreach (var i in displayed)
        {
            int columnIndex = i;
            var column = columns[i];
            var value = column.Value(row);
            using var cell = c.Div(key: column.Id).Width(ColumnWidth(column.Id)).FillHeight().Items(Align.Stretch).Justify(Justify.Center)
                .Px(t.Space.S3).Clip().TabStop(SelectedId == rowId && selectedColumn == i || SelectedId is null && rowIndex == 0 && i == displayed[0])
                .Semantics(Role.Cell, $"{column.Header}: {value}", value)
                .Accessibility(new(ReadOnly: column.Edit is null, Selected: SelectedId == rowId && selectedColumn == i, Row: rowIndex, Column: i), request =>
                {
                    if (request.Action != AccessibilityAction.Select)
                        return false;
                    SelectRow(rowIndex, columnIndex, false, false);
                    return true;
                });
            if (focusCell && SelectedId == rowId && selectedColumn == i)
                c.FocusNextControl();
            var hit = cell.Control();
            if (hit.Focused && !IsEditing)
                cell.Border(t.FocusRing, t.Controls.FocusWidth);
            if (hit.Clicked)
            {
                var shift = (c.Input.Modifiers & Modifiers.Shift) != 0;
                SelectRow(rowIndex, i, c.Input.Primary, shift);
                if (hit.DoubleClicked)
                    BeginEdit();
            }
            if (hit.Pressed && c.Input.Button == PointerButton.Secondary && c.Input.Pressed)
            {
                if (!IsSelected(rowId))
                    SelectRow(rowIndex, i, false, false);
                OpenMenu(c, c.Input.Pointer, i);
            }
            if (editingId == rowId && editingColumn == i)
            {
                if (focusEditor)
                {
                    c.FocusNextControl();
                    focusEditor = false;
                }
                c.TextField(draft.Text, text =>
                {
                    draft.Text = text;
                    StateChanged?.Invoke(Capture());
                }, new(Width: Math.Max(1, ColumnWidth(column.Id) - 2 * t.Space.S3), Name: $"Edit {column.Header}", Error: error), key: "editor");
            }
            else
                c.Text(value, t.Typography.Body with
                {
                    Color = selected ? t.SelectionText : t.Text
                }, key: "value");
        }
        using var rule = c.Div(key: "rule").Absolute().Offset(0, t.Controls.RowHeight - 1).FillWidth().Height(1).Background(t.Border);
    }
}
