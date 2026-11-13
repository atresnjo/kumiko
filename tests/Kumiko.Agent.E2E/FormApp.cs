using Kumiko.Runtime;
using Kumiko.UI;

internal sealed record FormModel(string Name = "", string Notes = "", bool Enabled = true, int Saves = 0, int InnerSaves = 0, string Status = "Ready");
internal abstract partial record FormMsg
{
    public sealed record NameChanged(string Value) : FormMsg;
    public sealed record NotesChanged(string Value) : FormMsg;
    public sealed record EnabledChanged(bool Value) : FormMsg;
    public sealed record Save : FormMsg;
    public sealed record SaveInner : FormMsg;
    public sealed record Load : FormMsg;
    public sealed record Loaded : FormMsg;
    public sealed record LoadFailed(string Reason) : FormMsg;
}
internal sealed partial class FormApp : IKumiko<FormModel, FormMsg>
{
    public static Kumiko.Runtime.NavigationRoot<FormMsg> Root(FormModel model) => Kumiko.Runtime.NavigationRoot<FormMsg>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new FormView(model).Map<FormMsg>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static FormModel Init => new();
    public static (FormModel, Cmd<FormMsg>) Update(FormModel m, FormMsg msg) => msg switch
    {
        FormMsg.NameChanged change => (m with { Name = change.Value }, Cmd<FormMsg>.None),
        FormMsg.NotesChanged change => (m with { Notes = change.Value }, Cmd<FormMsg>.None),
        FormMsg.EnabledChanged change => (m with { Enabled = change.Value }, Cmd<FormMsg>.None),
        FormMsg.Save => (m with { Saves = m.Saves + 1 }, Cmd<FormMsg>.None),
        FormMsg.SaveInner => (m with { InnerSaves = m.InnerSaves + 1 }, Cmd<FormMsg>.None),
        FormMsg.Load => (m with
        {
            Status = "Loading"
        }, Cmd<FormMsg>.Run("load", async token =>
        {
            await Task.Delay(150, token);
            return new FormMsg.Loaded();
        }, error => new FormMsg.LoadFailed(error.Message))),
        FormMsg.LoadFailed failure => (m with { Status = failure.Reason }, Cmd<FormMsg>.None),
        FormMsg.Loaded => (m with { Status = "Loaded" }, Cmd<FormMsg>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    };
}

internal sealed class FormView(FormModel model) : Kumiko.Runtime.Screen<FormModel, FormMsg>(model)
{
    protected override void Render()
    {
        var c = Ui.Context;
        using var page = c.Div("page").SizeFull().Padding(Space.S4).Gap(Space.S2);
        c.Text($"Saved {Model.Saves}; inner {Model.InnerSaves}");
        c.Text(Model.Status);
        Ui.Button("Load", new FormMsg.Load(), key: "load");
        Ui.Checkbox("Enable saving", Model.Enabled, value => new FormMsg.EnabledChanged(value), key: "enabled");
        using var form = Ui.Form("account", new FormMsg.Save(), "Account");
        Ui.TextField("name", "Name", Model.Name, value => new FormMsg.NameChanged(value));
        Ui.TextField("notes", "Notes", Model.Notes, value => new FormMsg.NotesChanged(value), new(Multiline: true));
        Ui.Submit("save", "Save", new(Disabled: !Model.Enabled));
        using var inner = Ui.Form("inner", new FormMsg.SaveInner(), "Nested");
        Ui.TextField("name", "Name", Model.Name, value => new FormMsg.NameChanged(value));
        Ui.Submit("save", "Save");
    }
}
