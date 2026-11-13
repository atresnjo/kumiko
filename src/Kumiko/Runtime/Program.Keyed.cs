namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    /// <summary>The latest running command for each debounce key.</summary>
    readonly Dictionary<string, CancellationTokenSource> keyed = new(StringComparer.Ordinal);

    /// <summary>Registers the latest command for <paramref name="key"/> and cancels the one it replaces.</summary>
    CancellationTokenSource Replace(string key, CancellationToken parent)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(parent);
        CancellationTokenSource? previous;
        lock (keyed)
        {
            keyed.Remove(key, out previous);
            keyed[key] = source;
        }
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The replaced command already finished and released its source.
        }
        return source;
    }

    /// <summary>Delivers a keyed command's message only while it is still the latest command for its key.</summary>
    void Deliver(string key, CancellationTokenSource source, Action deliver)
    {
        lock (keyed)
        {
            if (keyed.TryGetValue(key, out var current) && current == source)
                deliver();
        }
    }

    /// <summary>Forgets a finished keyed command and disposes its source.</summary>
    void Release(string key, CancellationTokenSource source)
    {
        lock (keyed)
        {
            if (keyed.TryGetValue(key, out var current) && current == source)
                keyed.Remove(key);
        }
        source.Dispose();
    }
}
