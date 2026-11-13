namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    // Only the UI thread adds work. Completed successful tasks can be forgotten;
    // failed cleanup stays observable by DisposeAsync, even after reconciliation.
    readonly List<Task> lifetimeWork = [];

    void Track(Task task)
    {
        lifetimeWork.RemoveAll(work => work.IsCompletedSuccessfully);
        lifetimeWork.Add(task);
    }

    void RetireCancellation(CancellationTokenSource source)
    {
        var pending = Task.WhenAll(lifetimeWork);
        Track(ReleaseCancellation(source, pending));
    }

    static async Task ReleaseCancellation(CancellationTokenSource source, Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        finally
        {
            source.Dispose();
        }
    }

    void StopSubscription(Subscription subscription)
    {
        Task stopped;
        try
        {
            // Initiate stopping on the UI thread. Prefer async disposal so a
            // timer/source can join callbacks before releasing scoped services.
            if (subscription.Handle is IAsyncDisposable asynchronous)
                stopped = asynchronous.DisposeAsync().AsTask();
            else
            {
                subscription.Handle?.Dispose();
                stopped = Task.CompletedTask;
            }
        }
        catch (Exception error)
        {
            stopped = Task.FromException(error);
        }
        Track(FinishSubscription(subscription, stopped));
    }

    static async Task FinishSubscription(Subscription subscription, Task stopped)
    {
        try
        {
            await stopped.ConfigureAwait(false);
        }
        finally
        {
            if (subscription.Scope is { } scope)
                await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Call on the UI thread to cancel and detach, then await all command and subscription cleanup
    /// before disposing the host/provider. Synchronous Dispose initiates shutdown without waiting.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(Task.WhenAll(lifetimeWork));
    }
}
