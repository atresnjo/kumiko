using System.Collections.Immutable;
using Kumiko.UI;

namespace Kumiko.Examples.Workbench;

public abstract partial record WorkbenchMsg
{
    public sealed record Search(string Text) : WorkbenchMsg;
    public sealed record Theme(WorkbenchTheme Value) : WorkbenchMsg;
    public sealed record Grid(DataGridState Value) : WorkbenchMsg;
    public sealed record Supplier(DropdownState Value) : WorkbenchMsg;
    public sealed record Edit(InventoryItem Item, bool Restock = false) : WorkbenchMsg;
    public sealed record Archive(ulong Id) : WorkbenchMsg;
    public sealed record Fixture(int Count) : WorkbenchMsg;
    public sealed record Load : WorkbenchMsg;
    public sealed record Loaded(int Request, ImmutableArray<InventoryItem> Items) : WorkbenchMsg;
    public sealed record Failed(int Request, string Error) : WorkbenchMsg;
    public sealed record Cancel : WorkbenchMsg;
    public sealed record ShellStatus(string Text) : WorkbenchMsg;
}
