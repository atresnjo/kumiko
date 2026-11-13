using Kumiko.Runtime;
using Kumiko.UI;

namespace TypedViews;

public sealed record AuthProps(string Name);
public abstract partial record AuthMsg
{
    public sealed record NameChanged(string Name) : AuthMsg;
    public sealed record SignIn : AuthMsg;
}

public sealed class AuthScreen(AuthProps props) : Screen<AuthProps, AuthMsg>(props)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Center().Padding(Space.S5).Background(C.Theme.Background);
        using var panel = C.Div(key: "panel").Width(360).MaxWidth(C.Size.Width - Space.S6).Gap(Space.S4);
        C.Text("Welcome", Typography.Heading, key: "title");
        C.Text("A local demo. Pick a display name to continue.", Typography.Body with
        {
            Wrap = true
        }, key: "intro");
        using var form = Ui.Form("sign-in", new AuthMsg.SignIn(), "Demo sign in");
        form.Layout.Gap(Space.S3);
        Ui.TextField("name", "Display name", Model.Name, static name => new AuthMsg.NameChanged(name),
            new(Width: 360), showLabel: true);
        Ui.Submit("submit", "Sign in", new(Disabled: string.IsNullOrWhiteSpace(Model.Name)));
    }
}
