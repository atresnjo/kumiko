using Kumiko.Runtime;
using Kumiko.UI;

namespace TypedViews;

public sealed record EditNameProps(string Name);
public abstract partial record EditNameMsg
{
    public sealed record NameChanged(string Name) : EditNameMsg;
    public sealed record Save : EditNameMsg;
    public sealed record Cancel : EditNameMsg;
}

public sealed class EditNameView(EditNameProps props) : View<EditNameProps, EditNameMsg>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        // Kit invokes this close callback synchronously inside Dialog().
        using var dialog = C.Kit.Dialog("Edit name", () => ui.Dispatch(new EditNameMsg.Cancel()),
            new(Width: 400, ShowClose: false), key: "dialog");
        using var form = Ui.Form("edit", new EditNameMsg.Save(), "Edit display name");
        form.Layout.Gap(Space.S4);
        Ui.TextField("name", "New display name", Model.Name, static name => new EditNameMsg.NameChanged(name),
            new(Width: 352), showLabel: true);
        using var actions = C.Div(key: "actions").Row().Gap(Space.S3);
        Ui.Button("Cancel", new EditNameMsg.Cancel(), key: "cancel");
        Ui.Submit("save", "Save", new(Disabled: string.IsNullOrWhiteSpace(Model.Name)));
    }
}
