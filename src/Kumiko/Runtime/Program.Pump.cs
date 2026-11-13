using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    /// <summary>Drains bridge/service messages on the owning thread without rendering a view.</summary>
    public void ProcessMessages()
    {
        CheckThread();
        if (building is not null)
            throw new InvalidOperationException("Do not process messages inside a view; dispatch and let the runtime rebuild.");
        if (lastContext is { } context)
        {
            Drain(context);
            if (!subscriptionsStarted)
                Reconcile();
        }
        else
        {
            using var temporary = new Context { DevMode = devMode };
            Drain(temporary);
            if (!subscriptionsStarted)
                Reconcile();
        }
    }
}
