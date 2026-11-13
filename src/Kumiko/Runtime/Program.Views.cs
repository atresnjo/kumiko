using System.Text.Json.Serialization.Metadata;
using Kumiko.UI;

namespace Kumiko.Runtime;

public sealed partial class Program<TModel, TMessage>
{
    /// <summary>Creates a program with an explicitly selected, freshly constructed typed root.</summary>
    public Program(TModel initial, Func<TModel, TMessage, (TModel Model, Cmd<TMessage> Command)> update,
        Func<TModel, NavigationRoot<TMessage>> root, JsonTypeInfo<TModel> modelType, JsonTypeInfo<TMessage> messageType,
        Func<TModel, IEnumerable<Sub<TMessage>>>? subscriptions = null)
        : this(initial, update, BindRoot(root), modelType, messageType, subscriptions)
    {
    }

    /// <summary>Supplies effect dependencies while retaining typed root construction.</summary>
    public Program(TModel initial, Func<TModel, TMessage, (TModel Model, Cmd<TMessage> Command)> update,
        Func<TModel, NavigationRoot<TMessage>> root, JsonTypeInfo<TModel> modelType, JsonTypeInfo<TMessage> messageType,
        Func<TModel, IEnumerable<Sub<TMessage>>>? subscriptions, KumikoServices services)
        : this(initial, update, BindRoot(root), modelType, messageType, subscriptions, services)
    {
    }

    static Action<Context, TModel, Dispatch<TMessage>> BindRoot(Func<TModel, NavigationRoot<TMessage>> root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return (context, model, send) => RenderRoot(context, root(model), send);
    }

    static void RenderRoot(Context context, NavigationRoot<TMessage> root, Dispatch<TMessage> send)
    {
        ArgumentNullException.ThrowIfNull(root);
        root.Invoke(context, send);
    }
}
