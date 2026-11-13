using Kumiko.Runtime;
using Kumiko.UI;

namespace TypedViews;

public sealed record MainProps(string Name, EditNameProps? Dialog);
public abstract partial record MainMsg
{
    public sealed record EditName : MainMsg;
    public sealed record SignOut : MainMsg;
    public sealed record Edit(EditNameMsg Message) : MainMsg;
}

public sealed class MainScreen(MainProps props) : Screen<MainProps, MainMsg>(props)
{
    protected override void Render()
    {
        using (C.Div(key: "page").SizeFull().Center().Padding(Space.S5).Background(C.Theme.Background))
            Content();
        if (Model.Dialog is { } draft)
            Ui.Child(new EditNameView(draft), "edit-name", static message => new MainMsg.Edit(message));
    }

    void Content()
    {
        using var panel = C.Div(key: "panel").Width(360).MaxWidth(C.Size.Width - Space.S6).Gap(Space.S4);
        C.Text($"Welcome, {Model.Name}", Typography.Heading with
        {
            Wrap = true
        }, key: "title");
        C.Text("Your name lives only in this demo session.", Typography.Body with
        {
            Wrap = true
        }, key: "intro");
        using var actions = C.Div(key: "actions").Row().Gap(Space.S3);
        Ui.Button("Edit name", new MainMsg.EditName(), key: "edit");
        Ui.Button("Sign out", new MainMsg.SignOut(), key: "sign-out");
    }
}
