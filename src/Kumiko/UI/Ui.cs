using System.Runtime.CompilerServices;
using Kumiko.Runtime;

namespace Kumiko.UI;
/// <summary>A message-bound invocation. Created and disposed by the runtime for a root or child render.</summary>
public sealed partial class Ui<TMessage> : IDisposable
{
    readonly Context context;
    readonly Dispatch<TMessage> send;
    readonly long frame;
    bool disposed;
    internal Ui(Context context, Dispatch<TMessage> send)
    {
        context.CheckThread();
        this.context = context;
        this.send = send;
        frame = context.FrameId;
    }

    /// <summary>Layout and drawing primitives for custom styling. State changes use bound messages.</summary>
    public Context Context
    {
        get
        {
            Check();
            return context;
        }
    }

    void Check()
    {
        context.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (frame != context.FrameId)
            throw new InvalidOperationException("A Ui belongs to one view invocation; do not retain it.");
    }

    /// <summary>
    /// Renders one child with identity local to its parent and a typed output mapping.
    /// Adds no layout node. Deferred control bindings retain the mapping, never the child Ui.
    /// </summary>
    public void Child<TProps, TChild>(View<TProps, TChild> view, NodeKey key, Func<TChild, TMessage> toParent)
        where TProps : notnull where TChild : notnull
    {
        Check();
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(toParent);
        var parent = send;
        using var identity = context.BeginChild(key);
        using var child = new Ui<TChild>(context, message => parent(toParent(message)));
        view.Invoke(child);
    }

    /// <summary>Binds an external control callback to this program without retaining the frame-scoped Ui or view.</summary>
    public Dispatch<TMessage> BindDispatcher()
    {
        Check();
        return send;
    }

    /// <summary>Dispatches non-control messages, such as shortcuts or drag results.</summary>
    public void Dispatch(TMessage message)
    {
        Check();
        send(message);
    }

    /// <summary>Binds an existing custom control's interaction to one message.</summary>
    public void OnPress(Interaction interaction, TMessage message)
    {
        Check();
        if (interaction.Clicked)
            context.WithSource(interaction.Id, () => send(message));
    }

    /// <summary>
    /// Binds a control's context-menu request, a secondary press or Shift+F10, to a message carrying the window
    /// point where the menu opens.
    /// </summary>
    public void OnContextMenu(Interaction interaction, Func<Point, TMessage> message)
    {
        Check();
        if (context.MenuRequested(interaction) is { } at)
            context.WithSource(interaction.Id, () => send(message(at)));
    }

    public void Button(string label, TMessage onPress, ButtonOptions options = default, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) => OnPress(Context.Button(label, options, key, source, line), onPress);
    /// <summary>Binds a styled surface as a named button. The caller owns its layout scope.</summary>
    public Interaction Button(Div surface, string label, TMessage onPress, bool enabled)
    {
        Check();
        var interaction = surface.Semantics(Role.Button, label).Control(enabled);
        OnPress(interaction, onPress);
        return interaction;
    }

    /// <summary>A controlled field; label names the native control. Set showLabel for a visible label.</summary>
    public void TextField(NodeKey key, string label, string value, Func<string, TMessage> onChange, TextFieldOptions options = default, bool showLabel = false, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        Check();
        if (showLabel)
            context.Text(label, key: $"{key.Value}-label", source: source, line: line);
        context.TextField(value, onChange, send, options with
        {
            Name = label
        }, key, source, line);
    }

    public void Checkbox(string label, bool value, Func<bool, TMessage> onChange, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) => Context.Checkbox(label, value, onChange, send, key, source, line);
    public void Toggle(string label, bool value, Func<bool, TMessage> onChange, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) => Context.Toggle(label, value, onChange, send, key, source, line);
    public void Slider(string label, float value, Func<float, TMessage> onChange, SliderOptions options = default, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) => Context.Slider(value, onChange, send, options, key, label, source, line);
    public void ResizeHandle(string label, float value, Func<float, TMessage> onChange, Components.ResizeHandleProps options = default, NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        Check();
        var dispatch = send;
        context.Kit.ResizeHandle(label, value, next => dispatch(onChange(next)), options, key, source, line);
    }

    public void Dispose()
    {
        context.CheckThread();
        disposed = true;
    }
}

public sealed partial class Context
{
    internal void WithSource(ulong id, Action action)
    {
        var previous = accessibilitySource;
        var index = Array.FindIndex(Tree, 0, Count, n => n.Id == id);
        if (index >= 0)
            accessibilitySource ??= new(Tree[index].Role, Tree[index].Name ?? ControlName(index), Tree[index].Key, PathOf(index));
        try
        {
            action();
        }
        finally
        {
            accessibilitySource = previous;
        }
    }
}
