using System.Collections;

namespace Kumiko.Runtime;

internal interface IHeldCommandOwner<TMessage>
{
    void CheckOwnerThread();
    void Settle(HeldCommand<TMessage> command, TMessage message);
    void Release(HeldCommand<TMessage> command);
}

/// <summary>
/// The commands a program holds instead of running, in the order their updates started them. Read and settle them
/// on the program's UI thread.
/// </summary>
public sealed class HeldCommands<TMessage> : IReadOnlyList<HeldCommand<TMessage>>
{
    readonly List<HeldCommand<TMessage>> items;
    readonly IHeldCommandOwner<TMessage> owner;

    internal HeldCommands(List<HeldCommand<TMessage>> items, IHeldCommandOwner<TMessage> owner)
    {
        this.items = items;
        this.owner = owner;
    }

    public int Count
    {
        get
        {
            owner.CheckOwnerThread();
            return items.Count;
        }
    }

    public HeldCommand<TMessage> this[int index]
    {
        get
        {
            owner.CheckOwnerThread();
            return items[index];
        }
    }

    /// <summary>The held command names in start order.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            owner.CheckOwnerThread();
            return [.. items.Select(c => c.Name)];
        }
    }

    /// <summary>Finds exactly one held command named <paramref name="name"/>.</summary>
    public HeldCommand<TMessage> Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        owner.CheckOwnerThread();
        var matches = items.Where(c => c.Name == name).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Expected one held command named \"{name}\"; found {matches.Length}. Held: {Describe()}.");
        return matches[0];
    }

    /// <summary>Requires the held commands to be exactly <paramref name="names"/>, in the order they started.</summary>
    public void Expect(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (!Names.SequenceEqual(names, StringComparer.Ordinal))
            throw new InvalidOperationException($"Expected held commands {Describe(names)}; held: {Describe()}.");
    }

    /// <summary>Requires that nothing is held.</summary>
    public void ExpectNone() => Expect();

    public IEnumerator<HeldCommand<TMessage>> GetEnumerator()
    {
        owner.CheckOwnerThread();
        return items.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    string Describe() => Describe(Names);

    static string Describe(IReadOnlyList<string> names) => names.Count == 0 ? "none" : "[" + string.Join(", ", names) + "]";
}

/// <summary>One started command waiting for the test to answer it, fail it or let it run.</summary>
public sealed class HeldCommand<TMessage>
{
    internal enum State
    {
        Held,
        Settled,
        Released,
        Replaced,
        Discarded
    }

    readonly IHeldCommandOwner<TMessage> owner;
    State state;

    internal HeldCommand(Cmd<TMessage>.Effect effect, CancellationTokenSource? source, long epoch, IHeldCommandOwner<TMessage> owner)
    {
        Effect = effect;
        Source = source;
        Epoch = epoch;
        this.owner = owner;
    }

    internal Cmd<TMessage>.Effect Effect
    {
        get;
    }

    /// <summary>The keyed registration that lets a newer command with the same key replace this one.</summary>
    internal CancellationTokenSource? Source
    {
        get;
    }

    internal long Epoch
    {
        get;
    }

    public string Name => Effect.Name;

    /// <summary>True until the command is settled, released, replaced or discarded.</summary>
    public bool IsHeld => state == State.Held;

    /// <summary>Queues <paramref name="message"/> as this command's result. The next frame applies it.</summary>
    public void Resolve(TMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        RequireHeld();
        owner.Settle(this, message);
    }

    /// <summary>Queues the command's own error mapping of <paramref name="error"/> as its result.</summary>
    public void Fail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        RequireHeld();
        TMessage message;
        using (KumikoExecution.Enter("command"))
            message = Effect.OnError(error);
        owner.Settle(this, message);
    }

    /// <summary>Runs the real work on a worker with the program's services, as if it had never been held.</summary>
    public void Run()
    {
        RequireHeld();
        owner.Release(this);
    }

    internal void Close(State next) => state = next;

    void RequireHeld()
    {
        owner.CheckOwnerThread();
        if (state == State.Held)
            return;
        throw new InvalidOperationException(state switch
        {
            State.Settled => $"Held command \"{Name}\" was already settled.",
            State.Released => $"Held command \"{Name}\" was already released to run.",
            State.Replaced => $"Held command \"{Name}\" was replaced by a newer command with the same key.",
            _ => $"Held command \"{Name}\" was discarded by history navigation, replay or disposal."
        });
    }
}
