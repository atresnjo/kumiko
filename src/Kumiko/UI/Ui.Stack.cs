using Kumiko.Runtime;

namespace Kumiko.UI;

// Platform adapters own native navigation. Core never references an operating-system assembly.
internal interface INavigationStackScope : IDisposable
{
    Div Page(int id, string title, bool guarded = false);
}
internal interface INavigationStackRenderer
{
    INavigationStackScope BeginStack<TMessage>(Ui<TMessage> ui, string stack, long revision,
        Func<BackRequest, TMessage> onBack, NodeKey key);
}

public sealed partial class Ui<TMessage>
{
    /// <summary>
    /// Renders identified visits synchronously. Desktop scopes use the existing parent and entry keys;
    /// native hosts use key for their container and require onBack. On desktop, supplying onBack enables
    /// focused/deepest child Back routing before screen history; Update still decides admission.
    /// </summary>
    public void Stack<TRoute, TEntry, TProps>(NavigationPath<TRoute, TEntry> path, TProps props,
        Action<Ui<TMessage>, TProps, TEntry> renderPage, Func<BackRequest, TMessage>? onBack = null,
        Func<TProps, TEntry, string>? title = null, Func<TEntry, bool>? guarded = null,
        string stack = "main", NodeKey key = default, Transition? enter = null)
        where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
    {
        Check();
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(renderPage);
        ArgumentException.ThrowIfNullOrWhiteSpace(stack);
        if (context.ControlRenderer is INavigationStackRenderer native)
        {
            ArgumentNullException.ThrowIfNull(onBack);
            using var host = native.BeginStack(this, stack, path.Revision, onBack, key);
            foreach (var entry in path.Entries)
            {
                using var page = host.Page(entry.Id, title?.Invoke(props, entry) ?? entry.Route.ToString() ?? "Page", guarded?.Invoke(entry) ?? false);
                renderPage(this, props, entry);
            }
            return;
        }
        foreach (var entry in path.Entries)
        {
            using var retained = context.RetainedScope(key: entry.Id, visible: entry.Id == path.Top.Id);
            if (!retained.Visible)
                continue;
            using var page = context.Div(key: "page").FillWidth().Flex().Column().Enter(enter ?? Transition.Rise with
            {
                Y = 6
            });
            renderPage(this, props, entry);
            if (onBack is not null)
            {
                var dispatch = send;
                var request = path.Entries.Length > 1 ? path.RequestTo(path.Entries[^2].Id, stack) : null;
                context.RegisterNavigationStack(page.Id, stack, request is null ? null : () => dispatch(onBack(request)));
            }
        }
    }
}
