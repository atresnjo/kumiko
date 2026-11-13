using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite.Components;

/// <summary>Controlled sign-in form: any host can supply its own state and message bindings.</summary>
public sealed record AuthFormProps(AuthState Auth, bool Dark, Accent Accent);

public sealed class AuthForm(AuthFormProps props) : View<AuthFormProps, AuthMsg>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        var auth = Model.Auth;
        var p = new Palette(Model.Dark, Model.Accent);
        var c = ui.Context;
        using var panel = c.Div(key: "panel").RaisedSurface(p).Width(420).MaxWidth(c.Size.Width - 48).Padding(Space.S6).Gap(Space.S4);
        c.Text("Harbor", new(Size: TextSize.Metric, Weight: Weight.Semibold, Color: p.Text), key: "brand");
        c.Text("Sign in to your workspace", new(Size: TextSize.Dialog, Color: p.Text), key: "title");
        c.Text("A demo workspace with fictional accounts. No password needed.",
            new(Size: TextSize.Body, Color: p.Secondary, Wrap: true), key: "hint");
        using (var form = ui.Form(key: "form", onSubmit: new AuthMsg.SignIn(), label: "Workspace sign in"))
        {
            form.Layout.FillWidth().Gap(Space.S3);
            ui.TextField("email", "Work email", auth.Email, static email => new AuthMsg.SetAuthEmail(email), new("name@harbor.app", 356, Disabled: auth.Busy), showLabel: true);
            if (auth.Error is { } error)
                c.Text(error, new(Size: TextSize.Body, Color: p.Danger, Wrap: true), key: "error");
            ui.Submit("sign-in", auth.Busy ? "Signing in…" : "Sign in", new(Disabled: auth.Busy));
        }
        c.Text("Demo accounts", new(Size: TextSize.Body, Color: p.Text, Weight: Weight.Medium), key: "accounts-title");
        foreach (var member in Data.Team.Where(m => m.Id is "maya" or "daniel" or "nina"))
        {
            using var row = c.Div(key: member.Id).FillWidth().Column().Gap(2);
            ui.Button(member.Email, new AuthMsg.SetAuthEmail(member.Email), new(Disabled: auth.Busy), key: "choose");
            c.Text(member.Id == "nina" ? "Viewer · read-only" : "Team member · can edit", new(Size: TextSize.Caption, Color: p.Secondary), key: "role");
        }
    }
}
