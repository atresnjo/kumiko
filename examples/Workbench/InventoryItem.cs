namespace Kumiko.Examples.Workbench;

public sealed record InventoryItem(ulong Id, string Name, string Sku, int Stock, string Supplier, string Location);
