namespace Kumiko.Runtime;

/// <summary>
/// A reusable update component whose model a parent stores and whose messages the parent wraps. Its view uses <c>View&lt;TProps, TMessage&gt;</c>; the parent composes it with <c>ui.Child(view, key, mapping)</c>. Members are checked where the
/// component is compiled, so applications can use components from other assemblies.
/// </summary>
public interface IKumikoComponent<TModel, TMessage> where TModel : notnull where TMessage : notnull
{
    static abstract (TModel, Cmd<TMessage>) Update(TModel model, TMessage message);
}

/// <summary>A reusable component that can also report an out-message, such as "deleted", to its parent.</summary>
public interface IKumikoComponent<TModel, TMessage, TOutMessage> where TModel : notnull where TMessage : notnull where TOutMessage : class
{
    static abstract Outcome<TModel, TMessage, TOutMessage> Update(TModel model, TMessage message);
}

/// <summary>A component update: its next model, its commands and an optional out-message for its parent.</summary>
public readonly record struct Outcome<TModel, TMessage, TOutMessage> where TOutMessage : class
{
    readonly Cmd<TMessage>? cmd;

    public Outcome(TModel model, Cmd<TMessage>? cmd = null, TOutMessage? outMessage = null)
    {
        Model = model;
        this.cmd = cmd;
        OutMessage = outMessage;
    }

    public TModel Model
    {
        get;
    }
    public Cmd<TMessage> Cmd => cmd ?? Cmd<TMessage>.None;
    public TOutMessage? OutMessage
    {
        get;
    }

    /// <summary>
    /// Writes the child model into its parent and wraps the child's commands. When the child reported an
    /// out-message, <paramref name="onOutMessage"/> handles it; its commands start after the child's.
    /// </summary>
    public (TParent, Cmd<TParentMessage>) Fold<TParent, TParentMessage>(Func<TModel, TParent> write,
        Func<TMessage, TParentMessage> toParent, Func<TParent, TOutMessage, (TParent, Cmd<TParentMessage>)> onOutMessage)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(toParent);
        ArgumentNullException.ThrowIfNull(onOutMessage);
        var parent = write(Model);
        var lifted = Cmd.Map(toParent);
        if (OutMessage is null)
            return (parent, lifted);
        var (next, cmd) = onOutMessage(parent, OutMessage);
        return (next, Cmd<TParentMessage>.Batch(lifted, cmd));
    }
}

public static class Outcome
{
    /// <summary>Writes a child update into its parent and wraps the child's commands as parent messages.</summary>
    public static (TParent, Cmd<TParentMessage>) Fold<TModel, TMessage, TParent, TParentMessage>(this (TModel Model, Cmd<TMessage> Cmd) update,
        Func<TModel, TParent> write, Func<TMessage, TParentMessage> toParent)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(toParent);
        return (write(update.Model), (update.Cmd ?? Cmd<TMessage>.None).Map(toParent));
    }
}
