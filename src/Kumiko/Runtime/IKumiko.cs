namespace Kumiko.Runtime;

/// <summary>Factory emitted by the Kumiko build generator for a Kumiko application.</summary>
public interface IKumiko
{
    static abstract IProgram Create();
}

/// <summary>The single application contract. State and messages must be deeply immutable.</summary>
public interface IKumiko<TModel, TMessage> : IKumiko where TModel : notnull where TMessage : notnull
{
    static abstract TModel Init
    {
        get;
    }
    static abstract (TModel, Cmd<TMessage>) Update(TModel model, TMessage message);
    static abstract NavigationRoot<TMessage> Root(TModel model);
    static virtual IEnumerable<Sub<TMessage>> Subscriptions(TModel model) => [];
}
