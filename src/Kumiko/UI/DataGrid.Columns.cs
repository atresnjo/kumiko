using System.Collections.Immutable;
using System.Globalization;

namespace Kumiko.UI;

public sealed partial class DataGrid<T>
{
    const float ResizeStep = 16;
    const float DragThreshold = 6;
    ImmutableArray<string> order;
    ImmutableDictionary<string, float> widths = ImmutableDictionary<string, float>.Empty;
    DataGridDrag? drag;
    CultureInfo culture = CultureInfo.CurrentCulture;

    /// <summary>Column IDs in displayed order; users reorder them by dragging headers or Primary+Shift+arrows.</summary>
    public IReadOnlyList<string> ColumnOrder => order;

    /// <summary>The displayed width of a column, including user resizing.</summary>
    public float ColumnWidth(string id) => widths.TryGetValue(id, out var width) ? width : Column(id).Width;

    DataGridColumn<T> Column(string id) => columns.FirstOrDefault(c => c.Id == id) ??
        throw new ArgumentException($"Unknown grid column '{id}'.", nameof(id));

    int IndexOf(string id)
    {
        for (var i = 0; i < columns.Count; i++)
            if (columns[i].Id == id)
                return i;
        return -1;
    }

    /// <summary>Column indices (into the constructor's columns) in displayed order.</summary>
    IReadOnlyList<int> Displayed => order.Select(IndexOf).ToArray();

    void SetWidth(string id, float width)
    {
        var column = Column(id);
        var next = MathF.Round(Math.Max(column.MinWidth, float.IsFinite(width) ? width : column.Width));
        if (ColumnWidth(id) != next)
            widths = widths.SetItem(id, next);
    }

    void MoveColumn(string id, int position)
    {
        var from = order.IndexOf(id);
        position = Math.Clamp(position, 0, order.Length - 1);
        if (from < 0 || from == position)
            return;
        order = order.RemoveAt(from).Insert(position, id);
    }

    /// <summary>Header keyboard commands: Primary+Shift+arrows move, Alt+arrows resize.</summary>
    bool HeaderKeys(Context c, string id)
    {
        var step = c.Logical(c.Input.Key) switch
        {
            Key.Left => -1,
            Key.Right => 1,
            _ => 0
        };
        if (step == 0)
            return false;
        var modifiers = c.Input.Modifiers;
        if (c.Input.Primary && (modifiers & Modifiers.Shift) != 0)
            MoveColumn(id, order.IndexOf(id) + step);
        else if ((modifiers & Modifiers.Alt) != 0)
            SetWidth(id, ColumnWidth(id) + step * ResizeStep);
        else
            return false;
        c.Input.Key = Key.None;
        c.Changed = true;
        return true;
    }

    /// <summary>Continues or finishes a header resize or reorder gesture after the headers are built.</summary>
    void TrackDrag(Context c, IReadOnlyList<(string Id, Rect? Rect)> headers)
    {
        if (drag is not { } d)
            return;
        var x = c.Input.Pointer.X;
        var rtl = c.IsRightToLeft();
        if (d.Resize)
        {
            // Right-to-left headers grow toward the left.
            SetWidth(d.Column, d.StartWidth + (rtl ? d.Origin - x : x - d.Origin));
            c.Cursor = Cursor.ResizeHorizontal;
        }
        else if (!d.Moved && Math.Abs(x - d.Origin) > DragThreshold)
            drag = d = d with
            {
                Moved = true
            };
        if (c.Input.Down && !c.Input.Released)
            return;
        if (!d.Resize && d.Moved)
        {
            var target = headers.Count;
            for (var i = 0; i < headers.Count; i++)
                if (headers[i].Rect is { } rect && (rtl ? x > rect.X + rect.Width / 2 : x < rect.X + rect.Width / 2))
                {
                    target = i;
                    break;
                }
            var from = order.IndexOf(d.Column);
            MoveColumn(d.Column, target > from ? target - 1 : target);
        }
        drag = null;
        c.Changed = true;
    }
}
