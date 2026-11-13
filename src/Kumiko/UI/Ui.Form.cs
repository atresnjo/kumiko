namespace Kumiko.UI;

public sealed partial class Ui<TMessage>
{
    readonly Stack<FormScope> forms = new();

    /// <summary>Begins a named form. Enter in a single-line editor and Primary+Enter submit once.</summary>
    public FormScope Form(NodeKey key, TMessage onSubmit, string? label = null)
    {
        Check();
#pragma warning disable KUM001 // FormScope owns and disposes the layout scope.
        var layout = context.Div(key).FillWidth().Semantics(Role.Form, label);
#pragma warning restore KUM001
        var form = new FormScope(this, layout, onSubmit, context.TakeFormSubmit(layout.Id));
        forms.Push(form);
        return form;
    }
    public void Submit(NodeKey key, string label, ButtonOptions options = default)
    {
        var form = CurrentForm();
        form.Enabled |= !options.Disabled;
        OnPress(context.Button(label, options, key), form.Message);
    }
    /// <summary>Binds a custom styled submit button to its enclosing form.</summary>
    public Interaction Submit(Div surface, string label, bool enabled)
    {
        var form = CurrentForm();
        form.Enabled |= enabled;
        return Button(surface, label, form.Message, enabled);
    }
    FormScope CurrentForm()
    {
        Check();
        return forms.TryPeek(out var form) ? form : throw new InvalidOperationException("Submit requires an enclosing ui.Form scope.");
    }

    public sealed class FormScope : IDisposable
    {
        readonly Ui<TMessage> ui;
        readonly bool submit;
        bool disposed;
        internal TMessage Message
        {
            get;
        }
        internal bool Enabled;
        public Div Layout
        {
            get;
        }
        internal FormScope(Ui<TMessage> ui, Div layout, TMessage message, bool submit)
        {
            this.ui = ui;
            Layout = layout;
            Message = message;
            this.submit = submit;
        }
        public void Dispose()
        {
            if (disposed)
                return;
            ui.Check();
            if (ui.forms.Peek() != this)
                throw new InvalidOperationException("Dispose form scopes in reverse order.");
            ui.forms.Pop();
            if (submit && Enabled)
                ui.context.WithSource(Layout.Id, () => ui.send(Message));
            Layout.Dispose();
            disposed = true;
        }
    }
}

public sealed partial class Context
{
    ulong submittingForm;
    void PrepareFormSubmit()
    {
        submittingForm = 0;
        if (ControlRenderer?.TakeSubmit() is { } field && field != 0)
        {
            SubmitFrom(field);
            return;
        }
        if (Input.Key != UI.Key.Enter || Input.Composition is not null || Input.CommitComposition ||
            Edits.TryGetValue(focus, out var edit) && edit.Composition.Length > 0)
            return;
        var focused = accessibility.FirstOrDefault(n => n.Id == focus);
        if (focused is null || !focused.Enabled || focused.Inert)
            return;
        var modifiers = Input.Modifiers;
        var primary = OperatingSystem.IsMacOS() ? Modifiers.Meta : Modifiers.Control;
        if ((modifiers & Modifiers.Primary) != 0)
            modifiers = (modifiers & ~Modifiers.Primary) | primary;
        if (modifiers != primary && (modifiers != Modifiers.None || focused.Role != Role.TextField || focused.Properties?.Multiline == true))
            return;
        SubmitFrom(focus);
        if (submittingForm != 0)
            Input.Key = UI.Key.None;
    }
    /// <summary>Selects the nearest form enclosing <paramref name="field"/> in the previous frame.</summary>
    void SubmitFrom(ulong field)
    {
        for (var index = Array.FindIndex(Tree, 0, Count, n => n.Id == field); index >= 0; index = Tree[index].Parent)
            if (Tree[index].Role == Role.Form)
            {
                submittingForm = Tree[index].Id;
                break;
            }
    }
    internal bool TakeFormSubmit(ulong id)
    {
        if (submittingForm != id)
            return false;
        submittingForm = 0;
        return true;
    }
}
