using Kumiko.Runtime;

namespace Kumiko.Examples.Workbench;

public sealed partial class WorkbenchApp
{
    public static IEnumerable<Sub<WorkbenchMsg>> Subscriptions(WorkbenchModel m)
    {
        if (m.IsLoading)
            yield return Load(m.Request, async token =>
            {
                await Task.Delay(200, token).ConfigureAwait(false);
                return Fixture(100_000);
            });
    }

    internal static Sub<WorkbenchMsg> Load(int request, Func<CancellationToken, Task<System.Collections.Immutable.ImmutableArray<InventoryItem>>> load,
        Action? completed = null) => new("inventory:" + request, send =>
    {
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var items = await load(token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                    send(new WorkbenchMsg.Loaded(request, items));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (!token.IsCancellationRequested)
                    send(new WorkbenchMsg.Failed(request, error.Message));
            }
            finally { completed?.Invoke(); }
        });
        return new Loading(cancellation);
    });

    sealed class Loading(CancellationTokenSource cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
