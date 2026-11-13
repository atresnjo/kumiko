using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.Workbench;

public enum WorkbenchTheme
{
    Light, Dark, Violet
}
public sealed record WorkbenchModel(ImmutableArray<InventoryItem> Inventory, string Search = "", WorkbenchTheme Theme = WorkbenchTheme.Light,
    DataGridState? Grid = null, DropdownState? Supplier = null, bool IsLoading = false, int Request = 0,
    string? LoadError = null, string Notice = "Illustrative inventory · edits are kept for this session",
    ImmutableDictionary<ulong, InventoryItem?>? LocalChanges = null)
{
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("inventory");
}
