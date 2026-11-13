using System.Globalization;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.Workbench;

public sealed partial class WorkbenchApp
{
    internal static DataGrid<InventoryItem> Grid(WorkbenchModel m, Dispatch<WorkbenchMsg> send)
    {
        var grid = new DataGrid<InventoryItem>(item => item.Id, new("name", "Name", 270, item => item.Name, (item, value) => item with { Name = value.Trim() }, value => string.IsNullOrWhiteSpace(value) ? "Name is required." : value.Length > 120 ? "Name must be at most 120 characters." : null), new("sku", "SKU", 150, item => item.Sku), new("stock", "Stock", 130, item => item.Stock.ToString(CultureInfo.InvariantCulture), (item, value) => item with { Stock = int.Parse(value, CultureInfo.InvariantCulture) }, value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stock) && stock >= 0 ? null : "Stock must be a whole number of zero or more.", (a, b) => a.Stock.CompareTo(b.Stock)), new("supplier", "Supplier", 260, item => InventoryWorkbench.Suppliers.First(s => s.Id == item.Supplier).Label), new("location", "Location", 180, item => item.Location, (item, value) => item with { Location = value.Trim() }, value => string.IsNullOrWhiteSpace(value) ? "Location is required." : null));
        grid.Name = "Inventory";
        grid.StateChanged += state => send(new WorkbenchMsg.Grid(state));
        grid.RowEdited += item => send(new WorkbenchMsg.Edit(item));
        grid.RowActions = item => [new("edit", "Edit selected cell", grid.BeginEdit, Shortcut: "F2"), new("restock", "Add 10 to stock", () => send(new WorkbenchMsg.Edit(item with { Stock = checked(item.Stock + 10) }, true))), new("archive", "Archive record", () => send(new WorkbenchMsg.Archive(item.Id)), Destructive: true)];
        grid.SetRows(m.Inventory.Where(item => (m.Supplier?.SelectedId is null || item.Supplier == m.Supplier.SelectedId) && (item.Name.Contains(m.Search, StringComparison.OrdinalIgnoreCase) || item.Sku.Contains(m.Search, StringComparison.OrdinalIgnoreCase))));
        grid.Restore(m.Grid ?? new());
        return grid;
    }

    public static Theme ThemeFor(WorkbenchTheme theme) => theme switch
    {
        WorkbenchTheme.Dark => Theme.ShadcnDark,
        WorkbenchTheme.Violet => Theme.ShadcnLight with
        {
            Accent = Color.Hex(0x6d28d9),
            AccentHover = Color.Hex(0x5b21b6),
            AccentText = Color.White,
            FocusRing = Color.Hex(0x8b5cf6),
            Selection = Color.Hex(0xede9fe),
            SelectionText = Color.Hex(0x4c1d95),
            Radius = new(Sm: 2, Md: 4, Lg: 6),
            Controls = new(RowHeight: 34, Height: 32)
        },
        _ => Theme.ShadcnLight
    };
}
