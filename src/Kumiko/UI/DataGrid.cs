using System.Collections.Immutable;
using System.Globalization;

namespace Kumiko.UI;

public sealed record DataGridColumn<T>(
    string Id,
    string Header,
    float Width,
    Func<T, string> Value,
    Func<T, string, T>? Edit = null,
    Func<string, string?>? Validate = null,
    Comparison<T>? Compare = null
)
{
    /// <summary>The narrowest width a user can resize the column to.</summary>
    public float MinWidth { get; init; } = 48;
}

/// <summary>
/// Fixed-height virtual data grid with stable selection, transactional cell editing, multi-row selection,
/// clipboard copy, CSV export, resizable and reorderable columns, a quick filter and single-level grouping.
/// </summary>
public sealed partial class DataGrid<T> where T : class
{
    readonly Func<T, ulong> key;
    readonly IReadOnlyList<DataGridColumn<T>> columns;
    T[] source = [], rows = [];
    readonly TextBuffer draft = new();
    readonly ContextMenu menu = new();
    int selectedColumn;
    int editingColumn = -1;
    ulong? editingId;
    ulong gridId;
    bool focusEditor, focusCell, reveal;
    string? error;
    string filter = "";
    string? groupBy;
    UiStrings strings = UiStrings.English;
    public int SortColumn { get; private set; } = -1;
    public bool Descending
    {
        get; private set;
    }
    /// <summary>The focused record: the target of keyboard navigation, editing and row actions.</summary>
    public ulong? SelectedId
    {
        get; private set;
    }
    public int SelectedIndex { get; private set; } = -1;
    public T? SelectedRow => SelectedIndex >= 0 && SelectedIndex < rows.Length ? rows[SelectedIndex] : null;
    /// <summary>Rows after the quick filter.</summary>
    public int Count => rows.Length;
    /// <summary>Rows before the quick filter.</summary>
    public int TotalCount => source.Length;
    /// <summary>Accessible grid name; empty uses <see cref="UiStrings.DataGrid"/>.</summary>
    public string Name { get; set; } = "";
    /// <summary>Empty-state hint; empty uses <see cref="UiStrings.NoRecordsHint"/>.</summary>
    public string EmptyDescription { get; set; } = "";
    public string? EditError => error;
    public bool IsEditing => editingId.HasValue;
    /// <summary>Allows Primary-click, Shift-click, Shift+arrows and Primary+A to select several rows.</summary>
    public bool MultiSelect
    {
        get; set;
    }
    public Func<T, IReadOnlyList<MenuCommand>>? RowActions
    {
        get; set;
    }
    /// <summary>
    /// Context-menu commands for one cell. A right-clicked or focused cell shows these when the list is not empty,
    /// otherwise the row's <see cref="RowActions"/>.
    /// </summary>
    public Func<T, DataGridColumn<T>, IReadOnlyList<MenuCommand>>? CellActions
    {
        get; set;
    }
    public event Action<DataGridState>? StateChanged;
    public event Action<T>? RowEdited;
    public event Action<T>? SelectionChanged;

    public DataGrid(Func<T, ulong> key, params DataGridColumn<T>[] columns)
    {
        if (columns.Length == 0 || columns.Any(c => !float.IsFinite(c.Width) || c.Width <= 0) || columns.Select(c => c.Id).Distinct().Count() != columns.Length)
            throw new ArgumentException("Grid columns require unique IDs and positive finite widths.", nameof(columns));
        this.key = key;
        this.columns = columns;
        order = [.. columns.Select(c => c.Id)];
    }

    /// <summary>Case-insensitive text matched against every column's displayed value; empty shows all rows.</summary>
    public string Filter
    {
        get => filter;
        set
        {
            if (filter == (value ?? ""))
                return;
            filter = value ?? "";
            SortRows();
        }
    }

    /// <summary>Column ID whose displayed value groups rows under collapsible headers; null disables grouping.</summary>
    public string? GroupBy
    {
        get => groupBy;
        set
        {
            if (groupBy == value)
                return;
            groupBy = value;
            SortRows();
        }
    }

    public void SetRows(IEnumerable<T> items)
    {
        var next = items.ToArray();
        var keys = new HashSet<ulong>();
        if (next.Any(item => !keys.Add(key(item))))
            throw new ArgumentException("Grid records require unique stable IDs.", nameof(items));
        source = next;
        SortRows();
        if (editingId is { } editId && !keys.Contains(editId))
            CancelEdit();
    }

    void SortRows()
    {
        var compare = culture.CompareInfo;
        rows = filter.Length == 0 ? (T[])source.Clone() : source.Where(row => columns.Any(c =>
            compare.IndexOf(c.Value(row), filter, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)).ToArray();
        if (SortColumn >= 0)
        {
            var column = columns[SortColumn];
            Array.Sort(rows, (a, b) =>
            {
                int result = column.Compare?.Invoke(a, b) ?? StringComparer.Create(culture, false).Compare(column.Value(a), column.Value(b));
                if (result == 0)
                    result = key(a).CompareTo(key(b));
                return Descending ? -Math.Sign(result) : Math.Sign(result);
            });
        }
        BuildEntries();
        SelectedIndex = SelectedId is { } id ? Array.FindIndex(rows, row => key(row) == id) : -1;
        if (SelectedIndex < 0)
            SelectedId = null;
        PruneSelection();
    }

    void Select(int row, int column)
    {
        if (rows.Length == 0)
            return;
        SelectedIndex = Math.Clamp(row, 0, rows.Length - 1);
        selectedColumn = Math.Clamp(column, 0, columns.Count - 1);
        SelectedId = key(rows[SelectedIndex]);
        reveal = true;
        SelectionChanged?.Invoke(rows[SelectedIndex]);
    }

    public void BeginEdit()
    {
        if (SelectedRow is not { } row || columns[selectedColumn].Edit is null)
            return;
        editingId = key(row);
        editingColumn = selectedColumn;
        draft.Text = columns[editingColumn].Value(row);
        error = null;
        focusEditor = true;
        reveal = true;
    }

    void CancelEdit()
    {
        editingId = null;
        editingColumn = -1;
        error = null;
        focusCell = true;
    }

    bool CommitEdit()
    {
        var item = rows.FirstOrDefault(row => key(row) == editingId);
        if (item is null || editingColumn < 0)
            return false;
        var column = columns[editingColumn];
        error = column.Validate?.Invoke(draft.Text);
        if (error is not null)
            return false;
        try
        {
            var next = column.Edit!(item, draft.Text);
            if (key(next) != key(item))
                throw new InvalidOperationException("Editing a cell cannot change its record ID.");
            source = source.Select(row => key(row) == key(item) ? next : row).ToArray();
            CancelEdit();
            SortRows();
            reveal = true;
            RowEdited?.Invoke(next);
            return true;
        }
        catch (FormatException)
        {
            error = strings.InvalidValue;
            return false;
        }
    }

    void OpenMenu(Context c, Point point, int column)
    {
        if (SelectedRow is not { } row)
            return;
        var commands = CellActions is { } cell && column >= 0 && column < columns.Count ? cell(row, columns[column]) : [];
        if (commands.Count == 0 && RowActions is { } actions)
            commands = actions(row);
        if (commands.Count == 0)
            return;
        menu.Open(point, c.Focus, commands);
        c.Changed = true;
    }
}
