using Kumiko.Runtime;

namespace CRMLite;

/// <summary>Desktop shell messages, and routing a notice click through the existing navigation and plan updates.</summary>
public sealed partial class CrmApp
{
    const string SaveNotice = "save";

    static string PlanNotice(string dealId) => "plan:" + dealId;

    static CrmModel WithDesktop(CrmModel m, Func<DesktopState, DesktopState> change) => m with
    {
        DesktopReport = change(m.Desktop)
    };

    static PostedNotice? Posted(CrmModel m, string id) => m.Desktop.Posted.FirstOrDefault(n => n.Id == id);

    public static (CrmModel, Cmd<DesktopMsg>) Update(CrmModel m, DesktopMsg msg)
    {
        switch (msg)
        {
            case DesktopMsg.AccessChanged a:
                return (WithDesktop(m, d => d with { Access = a.Access, Detail = a.Detail }), Cmd<DesktopMsg>.None);
            case DesktopMsg.Activated a:
                var active = WithDesktop(m, d => d with { Active = a.Active });
                // Coming forward onto the plan a notice is about: its dialog already says it.
                if (a.Active && m.Overlay is Overlay.Plan open && Posted(m, PlanNotice(open.DealId)) is { } seen)
                    return (WithDesktop(active, d => d with { Posted = d.Posted.Remove(seen) }), Withdraw(seen.Id));
                return (active, Cmd<DesktopMsg>.None);
            case DesktopMsg.Clicked c:
                return Clicked(m, c.Id);
            case DesktopMsg.PostFailed f:
                return (WithDesktop(m, d => d with
                {
                    Access = f.Access ?? d.Access,
                    Detail = f.Error,
                    Posted = d.Posted.RemoveAll(n => n.Id == f.Id)
                }), Cmd<DesktopMsg>.None);
            case DesktopMsg.Done d:
                return (d.Error is { } error ? WithDesktop(m, s => s with { Detail = error }) : m, Cmd<DesktopMsg>.None);
            default:
                throw new ArgumentOutOfRangeException(nameof(msg));
        }
    }

    /// <summary>
    /// Only a posted notice routes, once: a click on a withdrawn, cleared, earlier-session or duplicate notice does
    /// nothing. Routing reuses Open and OpenPlan, so navigation guards and plan rules still apply.
    /// </summary>
    static (CrmModel, Cmd<DesktopMsg>) Clicked(CrmModel m, string id)
    {
        if (Posted(m, id) is not { } notice)
            return (m, Cmd<DesktopMsg>.None);
        var next = WithDesktop(m, d => d with { Posted = d.Posted.Remove(notice) });
        return (Route(next, notice), BringForward());
    }

    /// <summary>A plan notice opens its deal's company and plan. Open drafts and confirmations keep the screen.</summary>
    static CrmModel Route(CrmModel m, PostedNotice notice)
    {
        if (notice.DealId is not { } dealId || m.Overlay is Overlay.NewDeal or Overlay.NewCompany || m.PendingNavigation is not null || !m.CanEdit)
            return m;
        if (m.Deals.FirstOrDefault(d => d.Id == dealId) is not { } deal || m.PlanFor(dealId) is null)
            return m;
        var closed = (m.Overlay is Overlay.Plan ? Minimize(EditStep(m, null)) : m) with
        {
            SettingsView = m.Settings with
            {
                Open = false,
                Menu = null,
                Query = ""
            }
        };
        var opened = Open(closed, deal.CompanyId);
        return opened.PendingNavigation is null ? OpenPlan(opened, dealId) : opened;
    }

    static Cmd<DesktopMsg> BringForward() => Cmd<DesktopMsg>.Run<ICrmDesktop>("desktop-bring-forward", static async (desktop, token) =>
    {
        await desktop.BringForward(token);
        return new DesktopMsg.Done("bring forward");
    }, static error => new DesktopMsg.Done("bring forward", error.Message));

    static Cmd<DesktopMsg> Withdraw(string id) => Cmd<DesktopMsg>.Run<ICrmDesktop>("desktop-withdraw " + id, async (desktop, token) =>
    {
        await desktop.Withdraw(id, token);
        return new DesktopMsg.Done("withdraw " + id);
    }, error => new DesktopMsg.Done("withdraw " + id, error.Message));

    static Cmd<DesktopMsg> Clear() => Cmd<DesktopMsg>.Run<ICrmDesktop>("desktop-clear", static async (desktop, token) =>
    {
        await desktop.Clear(token);
        return new DesktopMsg.Done("clear");
    }, static error => new DesktopMsg.Done("clear", error.Message));

    static Cmd<DesktopMsg> Post(CrmNotice notice) => Cmd<DesktopMsg>.Run<ICrmDesktop>("desktop-post " + notice.Id, async (desktop, token) =>
    {
        await desktop.Post(notice, token);
        return new DesktopMsg.Done("post " + notice.Id);
    }, error => new DesktopMsg.PostFailed(notice.Id, error switch
    {
        UnauthorizedAccessException => NoticeAccess.Denied,
        PlatformNotSupportedException => NoticeAccess.Unavailable,
        _ => null
    }, error.Message));
}
