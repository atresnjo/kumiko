using Kumiko.UI;

namespace Kumiko.Runtime;

/// <summary>A navigation-owned destination. Screens cannot be composed with Ui.Child or invoked by application code.</summary>
public abstract class Screen<TProps, TMessage>(TProps model) where TProps : notnull where TMessage : notnull
{
    Ui<TMessage>? invocation;
    bool invoked;
    protected Ui<TMessage> Ui => invocation ?? throw new InvalidOperationException("A screen is only active during its navigation-owned render.");
    protected TProps Model { get; } = model;
    protected Context C => Ui.Context;
    protected abstract void Render();

    /// <summary>Declares typed screen output routing. This does not render or navigate.</summary>
    public Screen<TParent> Map<TParent>(Func<TMessage, TParent> toParent) where TParent : notnull
    {
        ArgumentNullException.ThrowIfNull(toParent);
        return new Mapped<TParent>(this, toParent);
    }

    sealed class Mapped<TParent>(Screen<TProps, TMessage> screen, Func<TMessage, TParent> mapping) : Screen<TParent> where TParent : notnull
    {
        internal override void Invoke(Context context, Dispatch<TParent> send)
        {
            if (screen.invoked)
                throw new InvalidOperationException("Construct a fresh screen for each navigation-owned invocation.");
            screen.invoked = true;
            using var ui = new Ui<TMessage>(context, message => send(mapping(message)));
            screen.invocation = ui;
            try
            {
                screen.Render();
            }
            finally
            {
                screen.invocation = null;
            }
        }
    }
}

/// <summary>A framework-owned screen declaration with outputs mapped to its application.</summary>
public abstract class Screen<TMessage> where TMessage : notnull
{
    private protected Screen()
    {
    }
    internal abstract void Invoke(Context context, Dispatch<TMessage> send);
}
