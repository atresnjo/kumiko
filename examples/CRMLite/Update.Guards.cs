using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    static RouteDecision<Screen> BeforeEnter(CrmModel m, Screen route)
    {
        // A session alone is not enough: existence checks need the loaded workspace.
        if (m.Auth.Session is null || !m.Backend.Loaded || m.Backend.Loading)
            return new RouteDecision<Screen>.Defer();
        return route is Screen.CompanyDetails company && Crm.Company(m, company.Id) is null
            ? new RouteDecision<Screen>.Redirect(new Screen.Companies()) : new RouteDecision<Screen>.Allow();
    }

    static CrmModel RememberRoute(CrmModel m, Screen route) => m with
    {
        Auth = m.Auth with
        {
            ReturnTo = route is Screen.Companies ? null : route
        },
        PendingNavigation = null,
        Overlay = new Overlay.None()
    };

    static CrmModel WorkspaceLoaded(CrmModel m, int request, WorkspaceData data)
    {
        if (m.Auth.Session is null || !m.Backend.Loading || request != m.Backend.Request)
            return m;
        var loaded = data.Apply(m);
        if (loaded.Auth.ReturnTo is not { } route)
            return loaded;
        loaded = loaded with
        {
            Auth = loaded.Auth with
            {
                ReturnTo = null
            }
        };
        return route is Screen.CompanyDetails company ? Open(loaded, company.Id)
            : NavigateTo(loaded, loaded.Navigation.Reset(route));
    }

    static bool IsNavigation(CrmMsg msg) => msg is CrmMsg.Navigate or CrmMsg.Go or CrmMsg.ShowList or CrmMsg.OpenCompany or CrmMsg.OpenThread
        or CrmMsg.StepCompany or CrmMsg.Jump or CrmMsg.Back or CrmMsg.ConfirmNavigation;
}
