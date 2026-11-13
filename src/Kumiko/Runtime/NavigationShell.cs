using Kumiko.UI;

namespace Kumiko.Runtime;

/// <summary>
/// Navigation-owned chrome around the selected visit, such as a tab bar, sidebar or header. Its nodes keep one
/// identity, focus and native view while Push, Replace, Back, Forward and Reset change the visit rendered by
/// <see cref="Outlet"/>. Shells cannot be composed with Ui.Child or invoked by application code.
/// </summary>
public abstract class NavigationShell<TProps, TMessage>(TProps model) where TProps : notnull where TMessage : notnull
{
    Ui<TMessage>? invocation;
    Action? outlet;
    bool invoked;
    int outlets;
    protected Ui<TMessage> Ui => invocation ?? throw new InvalidOperationException("A shell is only active during its navigation-owned render.");
    protected TProps Model { get; } = model;
    protected Context C => Ui.Context;
    protected abstract void Render();

    /// <summary>Renders the selected visit at this point in the shell's layout. Call it exactly once per render.</summary>
    protected void Outlet()
    {
        var render = outlet ?? throw new InvalidOperationException("Outlet is only available while the shell renders.");
        if (++outlets > 1)
            throw new InvalidOperationException("A navigation shell must call Outlet exactly once; it was called again.");
        render();
    }

    /// <summary>Declares typed shell output routing. This does not render or navigate.</summary>
    public NavigationShell<TParent> Map<TParent>(Func<TMessage, TParent> toParent) where TParent : notnull
    {
        ArgumentNullException.ThrowIfNull(toParent);
        return new Mapped<TParent>(this, toParent);
    }

    sealed class Mapped<TParent>(NavigationShell<TProps, TMessage> shell, Func<TMessage, TParent> mapping) : NavigationShell<TParent> where TParent : notnull
    {
        internal override void Invoke(Context context, Dispatch<TParent> send, Action visit)
        {
            if (shell.invoked)
                throw new InvalidOperationException("Construct a fresh shell for each navigation-owned invocation.");
            shell.invoked = true;
            using var ui = new Ui<TMessage>(context, message => send(mapping(message)));
            shell.invocation = ui;
            shell.outlet = visit;
            try
            {
                shell.Render();
            }
            finally
            {
                shell.invocation = null;
                shell.outlet = null;
            }
            if (shell.outlets != 1)
                throw new InvalidOperationException("A navigation shell must call Outlet exactly once; it never rendered the visit.");
        }
    }
}

/// <summary>A framework-owned shell declaration with outputs mapped to its application.</summary>
public abstract class NavigationShell<TMessage> where TMessage : notnull
{
    private protected NavigationShell()
    {
    }
    internal abstract void Invoke(Context context, Dispatch<TMessage> send, Action visit);
}
