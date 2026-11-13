using System.Collections.Immutable;

namespace Kumiko.UI;

public sealed partial class DataGrid<T>
{
    ImmutableArray<ulong> selection = [];
    HashSet<ulong>? selectionLookup;
    ulong? anchorId;
    ImmutableArray<string> collapsed = [];
    List<Entry> entries = [];

    /// <summary>A displayed line: a data row index, or a group header covering <see cref="Count"/> rows.</summary>
    readonly record struct Entry(int Row, string? Group, int Count);

    /// <summary>Every selected record ID; contains <see cref="SelectedId"/> when a row is focused.</summary>
    public IReadOnlyList<ulong> SelectedIds => selection;

    /// <summary>Selected records in displayed order.</summary>
    public IReadOnlyList<T> SelectedRows => rows.Where(row => IsSelected(key(row))).ToArray();

    bool IsSelected(ulong id)
    {
        selectionLookup ??= [.. selection];
        return selectionLookup.Contains(id);
    }

    void SetSelection(IEnumerable<ulong> ids)
    {
        var next = ids.Distinct().ToImmutableArray();
        if (next.SequenceEqual(selection))
            return;
        selection = next;
        selectionLookup = null;
    }

    /// <summary>Applies a pointer or keyboard selection gesture to the row at <paramref name="row"/>.</summary>
    void SelectRow(int row, int column, bool toggle, bool extend)
    {
        if (row < 0 || row >= rows.Length)
            return;
        var id = key(rows[row]);
        if (!MultiSelect || !toggle && !extend)
        {
            SetSelection([id]);
            anchorId = id;
        }
        else if (toggle)
        {
            SetSelection(IsSelected(id) ? selection.Remove(id) : selection.Add(id));
            anchorId = id;
        }
        else
        {
            var display = DisplayRows();
            var from = anchorId is { } anchor ? display.FindIndex(i => key(rows[i]) == anchor) : -1;
            var to = display.IndexOf(row);
            if (from < 0)
                from = to;
            var (low, high) = from <= to ? (from, to) : (to, from);
            SetSelection(display.Skip(low).Take(high - low + 1).Select(i => key(rows[i])));
        }
        Select(row, column);
    }

    void SelectAll()
    {
        if (!MultiSelect)
            return;
        SetSelection(rows.Select(key));
    }

    void PruneSelection()
    {
        if (selection.IsEmpty)
            return;
        var visible = rows.Select(key).ToHashSet();
        SetSelection(selection.Where(visible.Contains));
        if (anchorId is { } anchor && !visible.Contains(anchor))
            anchorId = null;
    }

    /// <summary>Data row indices in display order, excluding rows inside collapsed groups.</summary>
    List<int> DisplayRows() => entries.Where(e => e.Group is null).Select(e => e.Row).ToList();

    void BuildEntries()
    {
        entries = new(rows.Length);
        var column = groupBy is null ? null : columns.FirstOrDefault(c => c.Id == groupBy);
        if (column is null)
        {
            for (var i = 0; i < rows.Length; i++)
                entries.Add(new(i, null, 1));
            return;
        }
        var compare = StringComparer.Create(culture, false);
        var groups = Enumerable.Range(0, rows.Length).GroupBy(i => column.Value(rows[i])).OrderBy(g => g.Key, compare);
        foreach (var group in groups)
        {
            var members = group.ToArray();
            entries.Add(new(-1, group.Key, members.Length));
            if (collapsed.Contains(group.Key))
                continue;
            foreach (var row in members)
                entries.Add(new(row, null, 1));
        }
    }

    /// <summary>Data rows in export order: grouped like the display, including collapsed groups.</summary>
    IEnumerable<T> ExportRows()
    {
        var column = groupBy is null ? null : columns.FirstOrDefault(c => c.Id == groupBy);
        if (column is null)
            return rows;
        var compare = StringComparer.Create(culture, false);
        return rows.GroupBy(column.Value).OrderBy(g => g.Key, compare).SelectMany(g => g);
    }

    void ToggleGroup(string group)
    {
        collapsed = collapsed.Contains(group) ? collapsed.Remove(group) : collapsed.Add(group);
        BuildEntries();
    }
}
