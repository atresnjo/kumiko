using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    public static CrmModel SignedOut(bool dark = false) => Data.Initial(dark) with
    {
        Companies = [],
        Deals = [],
        Contacts = [],
        Activities = [],
        Inbox = [],
        Favorites = [],
        Auth = new(),
        Backend = new(Loaded: false)
    };

    public static (CrmModel, Cmd<AuthMsg>) Update(CrmModel m, AuthMsg msg)
    {
        switch (msg)
        {
            case AuthMsg.SetAuthEmail e:
                return (m.Auth.Busy ? m : m with
                {
                    Auth = m.Auth with
                    {
                        Email = e.Email,
                        Error = null
                    }
                }, Cmd<AuthMsg>.None);
            case AuthMsg.SignIn:
                if (m.Auth.Busy || m.Auth.Session is not null)
                    return (m, Cmd<AuthMsg>.None);
                var request = m.Auth.Request + 1;
                var email = m.Auth.Email;
                return (m with
                {
                    Auth = m.Auth with
                    {
                        Busy = true,
                        Error = null,
                        Request = request
                    }
                },
                    Cmd<AuthMsg>.Run<IAuthService>("sign-in", async (auth, token) => new AuthMsg.SignedIn(request, await auth.SignInAsync(email, token)),
                        error => new AuthMsg.AuthFailed(request, error.Message)));
            case AuthMsg.SignedIn s:
                return s.Request == m.Auth.Request && m.Auth.Busy && m.Auth.Session is null
                    ? LoadWorkspace(m with
                    {
                        Auth = m.Auth with
                        {
                            Session = s.Session,
                            Busy = false,
                            Error = null
                        }
                    })
                    : (m, Cmd<AuthMsg>.None);
            case AuthMsg.AuthFailed f:
                return (f.Request == m.Auth.Request && m.Auth.Busy ? m with
                {
                    Auth = m.Auth with
                    {
                        Busy = false,
                        Error = f.Error,
                        ReturnTo = null
                    }
                } : m, Cmd<AuthMsg>.None);
            case AuthMsg.SignOut:
                if (m.Backend.Dirty)
                    return (m with
                    {
                        Backend = m.Backend with
                        {
                            Error = "Save your changes before signing out."
                        }
                    }, Cmd<AuthMsg>.None);
                var session = m.Auth.Session;
                var logout = m.Auth.Request + 1;
                var cleared = SignedOut(m.Dark) with
                {
                    Auth = new(Email: m.Auth.Email, Busy: session is not null, Request: logout),
                    Backend = new(Loaded: false, Request: m.Backend.Request + 1, SearchRequest: m.Backend.SearchRequest)
                };
                return (cleared, session is null ? Cmd<AuthMsg>.None : Cmd<AuthMsg>.Run<IAuthService>("sign-out", async (auth, token) =>
                {
                    await auth.SignOutAsync(session, token);
                    return new AuthMsg.SignedOut(logout);
                }, error => new AuthMsg.AuthFailed(logout, error.Message)));
            case AuthMsg.SignedOut s:
                return (s.Request == m.Auth.Request ? m with
                {
                    Auth = m.Auth with
                    {
                        Busy = false
                    }
                } : m, Cmd<AuthMsg>.None);
            case AuthMsg.RefreshWorkspace:
                return m.Auth.Session is null || m.Backend.Loading || m.Backend.Dirty ? (m, Cmd<AuthMsg>.None) : LoadWorkspace(m);
            case AuthMsg.WorkspaceLoaded l:
                return (WorkspaceLoaded(m, l.Request, l.Data), Cmd<AuthMsg>.None);
            case AuthMsg.WorkspaceFailed f:
                return (m.Auth.Session is not null && f.Request == m.Backend.Request
                    ? m with
                    {
                        Backend = m.Backend with
                        {
                            Loading = false,
                            Error = f.Error
                        }
                    } : m, Cmd<AuthMsg>.None);
            case AuthMsg.WorkspaceSaved s:
                return (m.Auth.Session is not null && s.Request == m.Backend.Request && s.Revision == m.Backend.Revision
                    ? m with
                    {
                        Backend = m.Backend with
                        {
                            Saving = false,
                            Dirty = false,
                            Error = null
                        }
                    } : m, Cmd<AuthMsg>.None);
            case AuthMsg.WorkspaceSaveFailed f:
                return (m.Auth.Session is not null && f.Request == m.Backend.Request && f.Revision == m.Backend.Revision
                    ? m with
                    {
                        Backend = m.Backend with
                        {
                            Saving = false,
                            Error = f.Error
                        }
                    } : m, Cmd<AuthMsg>.None);
            case AuthMsg.RetrySave:
                if (!m.CanEdit || !m.Backend.Dirty || m.Backend.Saving)
                    return (m, Cmd<AuthMsg>.None);
                var retry = m with
                {
                    Backend = m.Backend with
                    {
                        Saving = true,
                        Error = null
                    }
                };
                return (retry, SaveWorkspace(retry));
            case AuthMsg.SessionExpired e:
                return (m.Auth.Session is not null && e.Request == m.Backend.Request ? SignedOut(m.Dark) with
                {
                    Auth = new(Email: m.Auth.Email, Request: m.Auth.Request + 1, Error: "Your session ended. Sign in again."),
                    Backend = new(Loaded: false, Request: m.Backend.Request + 1, SearchRequest: m.Backend.SearchRequest)
                } : m, Cmd<AuthMsg>.None);
            default:
                throw new ArgumentOutOfRangeException(nameof(msg));
        }
    }

    static (CrmModel, Cmd<AuthMsg>) LoadWorkspace(CrmModel m)
    {
        var session = m.Auth.Session!;
        var request = m.Backend.Request + 1;
        return (m with
        {
            Backend = m.Backend with
            {
                Loading = true,
                Error = null,
                Request = request
            },
            Overlay = new Overlay.None()
        },
            Cmd<AuthMsg>.Run<ICrmApi>("load-workspace", async (api, token) => new AuthMsg.WorkspaceLoaded(request, await api.LoadAsync(session, token)),
                error => error is UnauthorizedAccessException ? new AuthMsg.SessionExpired(request) : new AuthMsg.WorkspaceFailed(request, error.Message)));
    }

    static Cmd<AuthMsg> SaveWorkspace(CrmModel m)
    {
        var session = m.Auth.Session!;
        var request = m.Backend.Request;
        var data = WorkspaceData.From(m);
        return Cmd<AuthMsg>.Debounce<ICrmApi>("save-workspace", TimeSpan.FromMilliseconds(100), async (api, token) =>
        {
            await api.SaveAsync(session, data, token);
            return new AuthMsg.WorkspaceSaved(request, data.Revision);
        }, error => error is UnauthorizedAccessException ? new AuthMsg.SessionExpired(request) : new AuthMsg.WorkspaceSaveFailed(request, data.Revision, error.Message));
    }

    static bool RequiresEditor(CrmMsg msg) => msg is CrmMsg.OpenCompanyDialog or CrmMsg.SubmitCompany or CrmMsg.OpenDealDialog or CrmMsg.SubmitDeal
        or CrmMsg.AddNote or CrmMsg.SetDealStage or CrmMsg.MoveDealTo or CrmMsg.SetOwner or CrmMsg.SetHot or CrmMsg.ToggleFavorite
        or CrmMsg.DeleteCompany or CrmMsg.OpenPlan or CrmMsg.ApprovePlan or CrmMsg.ResumePlan or CrmMsg.RetryStep or CrmMsg.SkipStep or CrmMsg.AgentTick;
}
