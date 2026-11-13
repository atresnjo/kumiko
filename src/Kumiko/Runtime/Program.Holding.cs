namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage> : IHeldCommandOwner<TMessage>
{
    readonly List<HeldCommand<TMessage>> held = [];
    HeldCommands<TMessage>? heldView;
    bool holdCommands;

    /// <summary>
    /// When true, commands started by updates wait in <see cref="Held"/> instead of running, so a test decides how
    /// and when each one answers. Turning it off while commands are held fails; settle or run them first.
    /// </summary>
    public bool HoldCommands
    {
        get => holdCommands;
        set
        {
            CheckThread();
            if (!value && held.Count > 0)
                throw new InvalidOperationException("Settle or run the held commands before turning holding off: " +
                    string.Join(", ", held.Select(c => c.Name)) + ".");
            holdCommands = value;
        }
    }

    /// <summary>The commands waiting while <see cref="HoldCommands"/> is on, in start order.</summary>
    public HeldCommands<TMessage> Held => heldView ??= new(held, this);

    void Start(Cmd<TMessage>.Effect effect)
    {
        if (!holdCommands)
        {
            Execute(effect);
            return;
        }
        CancellationTokenSource? source = null;
        if (effect.Keyed)
        {
            // Registering the key cancels a running command with the same key, exactly as an unheld start would.
            source = Replace(effect.Name, cancellation.Token);
            foreach (var older in held.Where(c => c.Source is not null && c.Name == effect.Name).ToArray())
            {
                held.Remove(older);
                older.Close(HeldCommand<TMessage>.State.Replaced);
                older.Source!.Dispose();
            }
        }
        held.Add(new(effect, source, generation, this));
    }

    void DiscardHeld()
    {
        foreach (var command in held)
        {
            command.Close(HeldCommand<TMessage>.State.Discarded);
            if (command.Source is { } source)
                Release(command.Name, source);
        }
        held.Clear();
    }

    void IHeldCommandOwner<TMessage>.CheckOwnerThread() => CheckThread();

    void IHeldCommandOwner<TMessage>.Settle(HeldCommand<TMessage> command, TMessage message)
    {
        held.Remove(command);
        command.Close(HeldCommand<TMessage>.State.Settled);
        if (command.Source is not { } source)
        {
            Enqueue(message, null, command.Epoch, null);
            return;
        }
        Deliver(command.Name, source, () => Enqueue(message, null, command.Epoch, null));
        Release(command.Name, source);
    }

    void IHeldCommandOwner<TMessage>.Release(HeldCommand<TMessage> command)
    {
        held.Remove(command);
        command.Close(HeldCommand<TMessage>.State.Released);
        Execute(command.Effect, command.Source);
    }
}
