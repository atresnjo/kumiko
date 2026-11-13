using CRMLite.Components;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void SignInPage()
    {
        using var page = C.Div(key: "signed-out").SizeFull().Center().Background(p.Sidebar);
        Ui.Child(new AuthForm(new(Model.Auth, Model.Dark, Model.Preferences.Accent)), "auth", static message => new CrmMsg.Authentication(message));
    }

    void BackendStatus()
    {
        var state = Model.Backend;
        using var bar = C.Div(key: "backend-status").FillWidth().Column().Gap(Space.S2).Padding(Space.S2)
            .Background(p.Sidebar).Border(p.Border).Semantics(Role.Group, "Workspace status");
        var status = state.Loading ? "Loading workspace…" : state.Error ?? (state.Saving ? "Saving changes…" : "All changes saved");
        C.Text(status, new(Size: TextSize.Caption, Color: state.Error is null ? p.Secondary : p.Danger, Wrap: true), key: "status");
        if (Model.Auth.Session is { CanEdit: false })
            C.Text("Read-only access", new(Size: TextSize.Caption, Color: p.Secondary), key: "access");
        if (state.Dirty && !state.Saving)
            Ui.Button("Retry save", new CrmMsg.Authentication(new AuthMsg.RetrySave()), key: "retry-save");
        else
            Ui.Button(state.Loaded ? "Refresh data" : "Retry loading", new CrmMsg.Authentication(new AuthMsg.RefreshWorkspace()),
                new(Disabled: state.Loading || state.Dirty), key: "refresh");
        Ui.Button("Sign out", new CrmMsg.Authentication(new AuthMsg.SignOut()), new(Disabled: state.Dirty), key: "sign-out");
    }

    void AccountSession()
    {
        if (Model.Auth.Session is not { } session)
            return;
        using var card = C.Div(key: "session").FillWidth().Column().Gap(Space.S3).Padding(Space.S3).OutlinedCard(p);
        using (C.Div(key: "identity").FillWidth())
            AccountIdentity.View(C, p, session.User, session.Workspace, session.CanEdit, key: "account");
        Ui.Button("Sign out", new CrmMsg.Authentication(new AuthMsg.SignOut()), new(Disabled: Model.Backend.Dirty), key: "sign-out");
    }
}
