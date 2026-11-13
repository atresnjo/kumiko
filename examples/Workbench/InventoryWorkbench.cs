using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.Workbench;
/// <summary>Program owner and injectable inventory service. Application state lives in Program.Model.</summary>
public sealed class InventoryWorkbench : IDisposable
{
    Func<CancellationToken, Task<IReadOnlyList<InventoryItem>>>? loader;
    TaskCompletionSource? completion;
    public Program<WorkbenchModel, WorkbenchMsg> Program
    {
        get;
    }
    public DataGrid<InventoryItem> Grid => WorkbenchApp.Grid(Program.Model, Program.Send);

    public SearchDropdown SupplierPicker
    {
        get
        {
            var picker = new SearchDropdown();
            picker.Restore(Program.Model.Supplier ?? new());
            return picker;
        }
    }

    public Theme Theme
    {
        get => WorkbenchApp.ThemeFor(Program.Model.Theme); set => Program.Send(new WorkbenchMsg.Theme(value == Theme.ShadcnDark ? WorkbenchTheme.Dark : value == Theme.ShadcnLight ? WorkbenchTheme.Light : WorkbenchTheme.Violet));
    }
    public bool IsLoading => Program.Model.IsLoading;
    public string? LoadError => Program.Model.LoadError;
    public string? Supplier
    {
        get => Program.Model.Supplier?.SelectedId; set => Program.Send(new WorkbenchMsg.Supplier((Program.Model.Supplier ?? new()) with { SelectedId = value }));
    }
    public static IReadOnlyList<Choice> Suppliers { get; } = ImmutableArray.Create<Choice>(new("northstar", "Northstar Components"), new("atlas", "Atlas Industrial"), new("meridian", "Meridian Supply"), new("pacific", "Pacific Parts"));

    public InventoryWorkbench()
    {
        Program = new(WorkbenchApp.Init, WorkbenchApp.Update, WorkbenchApp.Root, WorkbenchAppKumikoJson.Default.Model, WorkbenchAppKumikoJson.Default.Message, Subscriptions);
    }

    IEnumerable<Sub<WorkbenchMsg>> Subscriptions(WorkbenchModel m)
    {
        if (!m.IsLoading)
            yield break;
        if (loader is not { } current)
        {
            foreach (var sub in WorkbenchApp.Subscriptions(m))
                yield return sub;
            yield break;
        }

        var done = completion;
        yield return WorkbenchApp.Load(m.Request, async token => (await current(token).ConfigureAwait(false)).ToImmutableArray(), () => done?.TrySetResult());
    }

    public void LoadFixture(int count) => Program.Send(new WorkbenchMsg.Fixture(count));
    public void CancelLoad()
    {
        Program.Send(new WorkbenchMsg.Cancel());
        Program.ProcessMessages();
    }

    public Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<InventoryItem>>>? load = null)
    {
        loader = load;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Program.Send(new WorkbenchMsg.Load());
        Program.ProcessMessages();
        return completion.Task;
    }

    public void Dispose() => Program.Dispose();
}
