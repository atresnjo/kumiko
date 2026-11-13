namespace Kumiko.Runtime;

public delegate void Dispatch<in TMessage>(TMessage message);

public static class Dispatch
{
    public static Dispatch<TChild> Map<TParent, TChild>(this Dispatch<TParent> send, Func<TChild, TParent> map) => message => send(map(message));
}

/// <summary>A named effect description. Only the program runtime executes it.</summary>
public sealed class Cmd<TMessage>
{
    /// <summary>A keyed effect replaces the pending or running effect with the same key, cancelling its token.</summary>
    internal sealed record Effect(string Name, Func<KumikoServices, CancellationToken, Task<TMessage>> Work, Func<Exception, TMessage> OnError,
        bool Keyed = false);
    internal IReadOnlyList<Effect> Effects
    {
        get;
    }
    Cmd(IReadOnlyList<Effect> effects) => Effects = effects;
    public static Cmd<TMessage> None { get; } = new([]);

    /// <summary>Runs work on a worker; both success and failure return through the message queue.</summary>
    public static Cmd<TMessage> Run(string name, Func<CancellationToken, Task<TMessage>> work, Func<Exception, TMessage> onError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onError);
        return new([new(name, (_, token) => work(token), onError)]);
    }

    /// <summary>Resolves a service only when the worker executes the command.</summary>
    public static Cmd<TMessage> Run<TService>(string name, Func<TService, CancellationToken, Task<TMessage>> work,
        Func<Exception, TMessage> onError) where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onError);
        return new([new(name, (services, token) => work(services.Resolve<TService>(), token), onError)]);
    }

    /// <summary>
    /// Runs work on a worker after <paramref name="delay"/> of quiet. A later command with the same
    /// <paramref name="key"/> cancels this one first, whether it is still waiting or already running, so only the
    /// latest answer returns; a cancelled command sends no message. Use it for search-as-you-type and autosave.
    /// </summary>
    public static Cmd<TMessage> Debounce(string key, TimeSpan delay, Func<CancellationToken, Task<TMessage>> work,
        Func<Exception, TMessage> onError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onError);
        return new([new(key, async (_, token) =>
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token).ConfigureAwait(false);
            return await work(token).ConfigureAwait(false);
        }, onError, Keyed: true)]);
    }

    /// <summary>A service-aware keyed command; cancelled work never delivers a result.</summary>
    public static Cmd<TMessage> Debounce<TService>(string key, TimeSpan delay, Func<TService, CancellationToken, Task<TMessage>> work,
        Func<Exception, TMessage> onError) where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onError);
        return new([new(key, async (services, token) =>
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, token).ConfigureAwait(false);
            return await work(services.Resolve<TService>(), token).ConfigureAwait(false);
        }, onError, Keyed: true)]);
    }

    public static Cmd<TMessage> Batch(params Cmd<TMessage>[] commands) => new(commands.SelectMany(c => c.Effects).ToArray());
    public Cmd<TParent> Map<TParent>(Func<TMessage, TParent> map) => new([.. Effects.Select(effect =>
        new Cmd<TParent>.Effect(effect.Name, async (services, token) => map(await effect.Work(services, token).ConfigureAwait(false)), error => map(effect.OnError(error)),
            effect.Keyed))]);
}

/// <summary>A model-selected event source. Keys identify subscriptions across updates.</summary>
public sealed partial record Sub<TMessage>
{
    public string Key
    {
        get;
    }
    internal Func<Dispatch<TMessage>, IDisposable> Start
    {
        get;
    }
    internal Func<Dispatch<TMessage>, Action<Exception>, IDisposable>? CheckedStart
    {
        get; init;
    }
    internal Func<KumikoServices, Dispatch<TMessage>, IDisposable>? ServiceStart
    {
        get; init;
    }

    /// <summary>Starts a source in its own service scope. When the key disappears, the runtime stops the handle
    /// (preferring IAsyncDisposable) and keeps the scope until teardown finishes. IDisposable-only handles must
    /// synchronously quiesce any work using the service.</summary>
    public static Sub<TMessage> From<TService>(string key, Func<TService, Dispatch<TMessage>, IDisposable> start) where TService : class
    {
        ArgumentNullException.ThrowIfNull(start);
        return new(key, _ => throw new InvalidOperationException("The Kumiko runtime owns service subscriptions."))
        {
            ServiceStart = (services, send) => start(services.Resolve<TService>(), send)
        };
    }
    public Sub(string key, Func<Dispatch<TMessage>, IDisposable> start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(start);
        Key = key;
        Start = start;
    }

    /// <summary>Wraps a child's subscription messages while preserving cancellation and runtime error reporting.</summary>
    public Sub<TParent> Map<TParent>(Func<TMessage, TParent> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new(Key, send => Start(send.Map(map)))
        {
            CheckedStart = CheckedStart is { } start ? (send, report) => start(send.Map(map), report) : null,
            ServiceStart = ServiceStart is { } serviceStart ? (services, send) => serviceStart(services, send.Map(map)) : null
        };
    }

    public static Sub<TMessage> Every(string key, TimeSpan interval, Func<TMessage> tick)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        return new(key, _ => throw new InvalidOperationException("The Kumiko runtime owns subscription execution."))
        {
            CheckedStart = (send, report) => new Timer(_ =>
            {
                try
                {
                    send(tick());
                }
                catch (Exception error) { report(error); }
            }, null, interval, interval)
        };
    }
}

/// <summary>Analyzer contract: application state changes only through dispatch.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class KumikoViewAttribute : Attribute;

/// <summary>Analyzer contract: deterministic model/message transition without effects.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class KumikoUpdateAttribute : Attribute;
