using Kumiko.UI;

namespace Kumiko.Runtime;

/// <summary>The only public program root: identified navigation history, typed screens and typed navigation output.</summary>
public abstract class NavigationRoot<TMessage> where TMessage : notnull
{
    private protected NavigationRoot()
    {
    }
    internal abstract void Invoke(Context context, Dispatch<TMessage> send);

    /// <summary>Declares one host. Only the selected visit constructs/renders a screen; other visits retain UI state.</summary>
    public static NavigationRoot<TMessage> Create<TRoute>(NavigationHistory<TRoute> history, NodeKey key,
        Func<TRoute, Screen<TMessage>> resolve, Func<HistoryRequest, TMessage> onNavigate) where TRoute : notnull
    {
        Validate(history, key, resolve, onNavigate);
        return new Host<TRoute>(history, key, resolve, onNavigate, null);
    }

    /// <summary>
    /// Declares one host whose <paramref name="shell"/> stays mounted across visits. The shell places the selected
    /// visit with its outlet; other visits retain UI state.
    /// </summary>
    public static NavigationRoot<TMessage> Create<TRoute>(NavigationHistory<TRoute> history, NodeKey key,
        Func<TRoute, Screen<TMessage>> resolve, Func<HistoryRequest, TMessage> onNavigate, NavigationShell<TMessage> shell) where TRoute : notnull
    {
        Validate(history, key, resolve, onNavigate);
        ArgumentNullException.ThrowIfNull(shell);
        return new Host<TRoute>(history, key, resolve, onNavigate, shell);
    }

    static void Validate<TRoute>(NavigationHistory<TRoute> history, NodeKey key, Func<TRoute, Screen<TMessage>> resolve,
        Func<HistoryRequest, TMessage> onNavigate) where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(onNavigate);
        if (string.IsNullOrEmpty(key.Value))
            throw new ArgumentException("Navigation requires a stable nonempty host key.", nameof(key));
    }

    sealed class Host<TRoute>(NavigationHistory<TRoute> history, NodeKey key, Func<TRoute, Screen<TMessage>> resolve,
        Func<HistoryRequest, TMessage> onNavigate, NavigationShell<TMessage>? shell) : NavigationRoot<TMessage> where TRoute : notnull
    {
        bool invoked;
        internal override void Invoke(Context context, Dispatch<TMessage> send)
        {
            if (invoked)
                throw new InvalidOperationException("Construct a fresh navigation root for each render.");
            invoked = true;
            using var host = context.BeginChild(key);
            // Shell nodes sit under the host key, outside every entry identity, so they persist across visits.
            if (shell is null)
                Visits(context, send);
            else
                shell.Invoke(context, send, () => Visits(context, send));
            var direction = context.TakeNavigation();
            var request = direction switch
            {
                NavigationDirection.Back => history.BackRequest(key.Value!),
                NavigationDirection.Forward => history.ForwardRequest(key.Value!),
                _ => null
            };
            if (request is not null)
                context.WithNavigationSource(key.Value!, direction, () => send(onNavigate(request)));
        }

        void Visits(Context context, Dispatch<TMessage> send)
        {
            foreach (var entry in history.Path.Entries)
            {
                var visible = entry.Id == history.Current.Id;
                using var identity = context.BeginChild($"entry:{entry.Id}", visible);
                if (visible)
                {
                    var screen = resolve(entry.Route) ?? throw new InvalidOperationException("Every active route must resolve to a screen.");
                    screen.Invoke(context, send);
                }
            }
        }
    }
}
