using System.Collections.Immutable;
using Kumiko.Runtime;

namespace Kumiko.Examples.Workbench;

public sealed partial class WorkbenchApp : IKumiko<WorkbenchModel, WorkbenchMsg>
{
    public static NavigationRoot<WorkbenchMsg> Root(WorkbenchModel model) => NavigationRoot<WorkbenchMsg>.Create(model.Navigation, "workbench", _ => new WorkbenchScreen(model).Map<WorkbenchMsg>(static message => message), static _ => throw new InvalidOperationException("The inventory has one screen."));
    public static WorkbenchModel Init => new(Fixture(10_000));

    public static (WorkbenchModel, Cmd<WorkbenchMsg>) Update(WorkbenchModel m, WorkbenchMsg msg)
    {
        var changes = m.LocalChanges ?? ImmutableDictionary<ulong, InventoryItem?>.Empty;
        return (msg switch
        {
            WorkbenchMsg.Search s => m with { Search = s.Text },
            WorkbenchMsg.ShellStatus s => m with { Notice = s.Text },
            WorkbenchMsg.Theme t => m with { Theme = t.Value },
            WorkbenchMsg.Grid g => m with { Grid = g.Value },
            WorkbenchMsg.Supplier s => m with { Supplier = s.Value },
            WorkbenchMsg.Edit e => m with { Inventory = m.Inventory.Select(i => i.Id == e.Item.Id ? e.Item : i).ToImmutableArray(), LocalChanges = changes.SetItem(e.Item.Id, e.Item), Notice = (e.Restock ? "Updated stock for " : "Saved ") + e.Item.Name },
            WorkbenchMsg.Archive a => m with { Inventory = m.Inventory.Where(i => i.Id != a.Id).ToImmutableArray(), LocalChanges = changes.SetItem(a.Id, null), Notice = "Archived record" },
            WorkbenchMsg.Fixture f => m with { Inventory = Fixture(f.Count), Grid = null },
            WorkbenchMsg.Load => m with { IsLoading = true, Request = m.Request + 1, LoadError = null },
            WorkbenchMsg.Loaded l when m.IsLoading && m.Request == l.Request => m with { Inventory = l.Items.Where(i => !changes.TryGetValue(i.Id, out var change) || change is not null).Select(i => changes.GetValueOrDefault(i.Id) ?? i).ToImmutableArray(), IsLoading = false, Notice = "Inventory loaded · illustrative records" },
            WorkbenchMsg.Loaded => m,
            WorkbenchMsg.Failed f when m.IsLoading && m.Request == f.Request => m with { IsLoading = false, LoadError = "Could not load inventory: " + f.Error },
            WorkbenchMsg.Failed => m,
            WorkbenchMsg.Cancel => m with { IsLoading = false, Request = m.Request + 1, Notice = "Loading cancelled · existing inventory retained" },
            _ => throw new ArgumentOutOfRangeException(nameof(msg))
        }, Cmd<WorkbenchMsg>.None);
    }

    public static ImmutableArray<InventoryItem> Fixture(int count) => Enumerable.Range(0, count).Select(i => new InventoryItem((ulong)i + 1, i == 0 ? "Precision bearing" : $"Warehouse part {i:D6}", $"PT-{i + 1:D6}", i + 24, InventoryWorkbench.Suppliers[i % InventoryWorkbench.Suppliers.Count].Id, $"Aisle {i % 24 + 1:D2} / Bay {i % 8 + 1:D2}")).ToImmutableArray();
}
