using System.Runtime.CompilerServices;
using Kumiko.Runtime;

namespace Kumiko.UI;

public sealed partial class Context
{
    ApplicationIconCache applicationIcons = new(bundle => Rendering.Image.ApplicationIcon(bundle));
    public ApplicationIconCache ApplicationIcons
    {
        get => ReadUI(applicationIcons);
        internal set => applicationIcons = value;
    }

    public void ApplicationIcon(string bundle, float size)
    {
        CheckThread();
        ApplicationIcons.BeginFrame(this, FrameId);
        if (ApplicationIcons.Get(bundle) is { } icon)
            Image(icon, size, size);
        if (ApplicationIcons.Pending)
            FrameIn(TimeSpan.FromSeconds(1 / 60d));
    }

    internal MessageSource? SourceOfInput()
    {
        if (accessibilitySource is not null)
            return accessibilitySource;
        var id = Input.Key != UI.Key.None || Input.Text.Length > 0 ? focus : active != 0 ? active : hot;
        for (var i = 0; i < Count; i++)
        {
            var node = Tree[i];
            if (node.Id != id)
                continue;
            var name = node.Name ?? ControlName(i);
            if (name.Length == 0)
                name = accessibility.FirstOrDefault(n => n.Id == id)?.Name ?? "";
            return new(node.Role, name, node.Key, PathOf(i));
        }
        return null;
    }

    internal long ProgramViewVersion
    {
        get; private set;
    }

    internal void ResetProgramView()
    {
        ProgramViewVersion++;
        Edits.Clear();
        Scrolls.Clear();
        nodeStates.Clear();
        retentionOwners.Clear();
        retentionChildren.Clear();
        retentionChildrenPass = -1;
        returningFocus = 0;
        animations.Clear();
        focus = active = 0;
        modalRoot = inputModalRoot = 0;
        navigationStacks.Clear();
        Input.Clear();
        accessibleInvocations.Clear();
        accessibleActivation = 0;
        accessibilitySource = null;
    }

    internal void FreezeProgramFrame()
    {
        Animating = false;
        NextFrame = null;
    }

    /// <summary>Dispatches a message when model-controlled text changes.</summary>
    public bool TextField<TMessage>(string value, Func<string, TMessage> message, Dispatch<TMessage> send,
        TextFieldOptions options = default, NodeKey key = default,
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0) =>
        TextField(value, text => send(message(text)), options, key, source, line);

    public bool Checkbox<TMessage>(string label, bool value, Func<bool, TMessage> message, Dispatch<TMessage> send,
        NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var next = value;
        if (!Checkbox(label, ref next, key, source, line))
            return false;
        send(message(next));
        return true;
    }

    public bool Toggle<TMessage>(string label, bool value, Func<bool, TMessage> message, Dispatch<TMessage> send,
        NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var next = value;
        if (!Toggle(label, ref next, key, source, line))
            return false;
        send(message(next));
        return true;
    }

    public bool Slider<TMessage>(float value, Func<float, TMessage> message, Dispatch<TMessage> send,
        SliderOptions options = default, NodeKey key = default, string name = "",
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        var next = value;
        if (!Slider(ref next, options, key, name, source, line))
            return false;
        send(message(next));
        return true;
    }

    /// <summary>Model-controlled text. Editing state belongs to the keyed node; contents belong to the model.</summary>
    public bool TextField(string value, Action<string> changed, TextFieldOptions options = default,
        NodeKey key = default, [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        CheckThread();
        if (ControlRenderer is not null)
            return NativeTextField(value, changed, options, key, source, line);
        var id = NextNodeId(key);
        var buffer = Edits.TryGetValue(id, out var edit) && edit.Buffer is { } existing ? existing : new TextBuffer(value);
        if (buffer.Text != value)
        {
            buffer.Text = value;
            edit?.Undo.Clear();
            edit?.Redo.Clear();
        }
        var edited = TextField(buffer, options, key, source, line);
        Edits[id].ModelChanged = changed;
        if (edited)
            changed(buffer.Text);
        return edited;
    }
}
