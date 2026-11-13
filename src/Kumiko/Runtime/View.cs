using Kumiko.UI;

namespace Kumiko.Runtime;

/// <summary>A fresh, render-local view. Never retain a view or its Ui beyond the current render.</summary>
public abstract class View<TProps, TMessage>(TProps model) where TProps : notnull where TMessage : notnull
{
    Ui<TMessage>? invocation;
    bool invoked;
    protected Ui<TMessage> Ui => invocation ?? throw new InvalidOperationException("A view is only active during its render invocation.");
    protected TProps Model { get; } = model;
    protected Context C => Ui.Context;
    protected abstract void Render();

    internal void Invoke(Ui<TMessage> ui)
    {
        if (invoked)
            throw new InvalidOperationException("Construct a fresh view for each invocation; a view cannot be reused.");
        invoked = true;
        invocation = ui;
        try
        {
            Render();
        }
        finally
        {
            invocation = null;
        }
    }
}
