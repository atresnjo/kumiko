using System.Collections.Immutable;

namespace Kumiko.UI;

/// <summary>Serializable interaction state. Commands are supplied afresh by the current view.</summary>
public sealed record ContextMenuState(bool IsOpen = false, Point Position = default, ulong ReturnFocus = 0, int Active = 0, bool FocusFirst = false);
public sealed record DropdownState(string? SelectedId = null, string Query = "", int Active = 0, bool FocusSearch = false,
    ulong TriggerId = 0, Rect Anchor = default, bool IsOpen = false)
{
    /// <summary>The chosen IDs of a multiple-selection dropdown, in the order they were chosen.</summary>
    public ImmutableArray<string> SelectedIds { get; init; } = [];
    /// <summary>
    /// The chosen choices as last seen. The trigger labels selections absent from the current choices with them,
    /// such as a remote result after the next search; set it to label an initial selection before results load.
    /// </summary>
    public ImmutableArray<Choice> Selection { get; init; } = [];
}
public sealed record DataGridState(int SortColumn = -1, bool Descending = false, ulong? SelectedId = null, int SelectedColumn = 0,
    int EditingColumn = -1, ulong? EditingId = null, string Draft = "", ulong GridId = 0, bool FocusEditor = false,
    bool FocusCell = false, bool Reveal = false, string? Error = null, ContextMenuState? Menu = null)
{
    /// <summary>All selected record IDs when multi-selection is enabled.</summary>
    public ImmutableArray<ulong> SelectedIds { get; init; } = [];
    /// <summary>The record a Shift range extends from.</summary>
    public ulong? AnchorId
    {
        get; init;
    }
    /// <summary>User column order; empty means declaration order.</summary>
    public ImmutableArray<string> ColumnOrder { get; init; } = [];
    /// <summary>User column widths by column ID.</summary>
    public ImmutableDictionary<string, float> ColumnWidths { get; init; } = ImmutableDictionary<string, float>.Empty;
    /// <summary>Collapsed group values when grouping is enabled.</summary>
    public ImmutableArray<string> CollapsedGroups { get; init; } = [];
    /// <summary>An in-progress header resize or reorder.</summary>
    public DataGridDrag? Drag
    {
        get; init;
    }
}

/// <summary>A header gesture: resizing <paramref name="Column"/> or dragging it to a new position.</summary>
public sealed record DataGridDrag(string Column, bool Resize, float Origin, float StartWidth, bool Moved = false);

public sealed partial class ContextMenu
{
    public ContextMenuState Capture() => new(IsOpen, position, returnFocus, active, focusFirst);
    public void Restore(ContextMenuState state, IReadOnlyList<MenuCommand> items)
    {
        IsOpen = state.IsOpen && items.Count > 0;
        position = state.Position;
        returnFocus = state.ReturnFocus;
        active = state.Active;
        focusFirst = state.FocusFirst;
        commands = items;
    }
}

public sealed partial class SearchDropdown
{
    public DropdownState Capture() => new(SelectedId, query.Text, active, focusSearch, triggerId, anchor, IsOpen)
    {
        SelectedIds = selectedIds,
        Selection = selection
    };
    public void Restore(DropdownState state)
    {
        SelectedIds = state.SelectedIds;
        selection = state.Selection.IsDefault ? [] : state.Selection;
        SelectedId = state.SelectedId;
        query.Text = state.Query;
        active = state.Active;
        focusSearch = state.FocusSearch;
        triggerId = state.TriggerId;
        anchor = state.Anchor;
        IsOpen = state.IsOpen;
    }
}

public sealed partial class DataGrid<T>
{
    public DataGridState Capture() => new(SortColumn, Descending, SelectedId, selectedColumn, editingColumn, editingId,
        draft.Text, gridId, focusEditor, focusCell, reveal, error, menu.Capture())
    {
        SelectedIds = selection,
        AnchorId = anchorId,
        ColumnOrder = order.SequenceEqual(columns.Select(c => c.Id)) ? [] : order,
        ColumnWidths = widths,
        CollapsedGroups = collapsed,
        Drag = drag
    };

    public void Restore(DataGridState state)
    {
        SortColumn = state.SortColumn;
        Descending = state.Descending;
        SelectedId = state.SelectedId;
        selectedColumn = state.SelectedColumn;
        editingColumn = state.EditingColumn;
        editingId = state.EditingId;
        draft.Text = state.Draft;
        gridId = state.GridId;
        focusEditor = state.FocusEditor;
        focusCell = state.FocusCell;
        reveal = state.Reveal;
        error = state.Error;
        selection = state.SelectedIds.IsDefault ? [] : state.SelectedIds;
        selectionLookup = null;
        anchorId = state.AnchorId;
        var ids = columns.Select(c => c.Id).ToHashSet();
        if (!state.ColumnOrder.IsDefaultOrEmpty && state.ColumnOrder.Length == ids.Count && ids.SetEquals(state.ColumnOrder))
            order = state.ColumnOrder;
        widths = state.ColumnWidths ?? ImmutableDictionary<string, float>.Empty;
        if (widths.Keys.Any(id => !ids.Contains(id)))
            widths = widths.RemoveRange(widths.Keys.Where(id => !ids.Contains(id)));
        collapsed = state.CollapsedGroups.IsDefault ? [] : state.CollapsedGroups;
        drag = state.Drag;
        SortRows();
        menu.Restore(state.Menu ?? new(), SelectedRow is { } row ? RowActions?.Invoke(row) ?? [] : []);
    }
}
