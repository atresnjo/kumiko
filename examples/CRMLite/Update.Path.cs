using System.Collections.Immutable;
using Kumiko.Runtime;

namespace CRMLite;

public sealed partial class CrmApp
{
    static CrmModel NavigateTo(CrmModel m, CrmNavigation desired, CompanyList? list = null, CrmNavigation? confirmed = null)
    {
        switch (GuardNavigation(m, desired))
        {
            case RouteDecision<Screen>.Defer:
                return RememberRoute(m, desired.Top.Route);
            case RouteDecision<Screen>.Redirect redirect:
                return NavigateTo(m, m.Navigation.Reset(redirect.To), list, confirmed);
        }
        m = m with
        {
            Auth = m.Auth with
            {
                ReturnTo = null
            }
        };
        var request = m.BackTo(desired.Top.Id);
        return m.Navigation.RequiresConfirmation(desired, static entry => !string.IsNullOrWhiteSpace(entry.Note), confirmed)
            ? m with
            {
                PendingNavigation = new(request, desired, list: list),
                Overlay = new Overlay.None()
            }
            : m with
            {
                Navigation = desired,
                PendingNavigation = null,
                Overlay = new Overlay.None(),
                List = list ?? m.List
            };
    }

    static CrmModel Back(CrmModel m, CrmBackRequest request) => m.Navigation.Back(request, "main") is { } desired
        ? NavigateTo(m, desired) : m;

    static CrmModel ConfirmNavigation(CrmModel m, CrmBackRequest request)
    {
        if (m.PendingNavigation is not { } pending || pending.Confirm(m.Navigation, request, "main") is not { } desired)
            return m;
        switch (GuardNavigation(m, desired))
        {
            case RouteDecision<Screen>.Defer:
                return RememberRoute(m, desired.Top.Route);
            case RouteDecision<Screen>.Redirect redirect:
                return NavigateTo(m, m.Navigation.Reset(redirect.To), pending.List, desired);
        }
        var next = m with
        {
            Auth = m.Auth with
            {
                ReturnTo = null
            },
            Navigation = desired,
            PendingNavigation = null,
            Overlay = new Overlay.None(),
            List = pending.List ?? m.List
        };
        return pending.DeleteCompanyId is { } id ? RemoveCompanyData(next, id) : next;
    }

    /// <summary>
    /// Edits one visit. Editing a draft that a pending confirmation would discard means the user kept
    /// editing, so that confirmation is withdrawn rather than left to discard newer text.
    /// </summary>
    static CrmModel EditEntry(CrmModel m, int id, DetailTab? tab = null, string? note = null)
    {
        var navigation = m.Navigation.Edit(id, entry => entry with
        {
            Tab = tab ?? entry.Tab,
            Note = note ?? entry.Note
        });
        if (ReferenceEquals(navigation, m.Navigation))
            return m;
        var withdrawn = note is not null && m.PendingNavigation is { } pending && pending.Desired.IndexOf(id) < 0;
        return m with
        {
            Navigation = navigation,
            PendingNavigation = withdrawn ? null : m.PendingNavigation
        };
    }

    static CrmModel DeleteCompany(CrmModel m, string id)
    {
        if (!m.Companies.Any(company => company.Id == id))
            return m;
        var desired = m.Navigation.RemoveWhere(entry => entry.Route is Screen.CompanyDetails company && company.Id == id);
        var next = NavigateTo(m, desired);
        return next.PendingNavigation is { } pending
            ? next with
            {
                PendingNavigation = pending with
                {
                    DeleteCompanyId = id
                }
            }
            : RemoveCompanyData(next, id);
    }

    static CrmModel RemoveCompanyData(CrmModel m, string id)
    {
        var deals = m.Deals.Where(deal => deal.CompanyId == id).Select(deal => deal.Id).ToImmutableHashSet();
        return m with
        {
            Companies = m.Companies.RemoveAll(co => co.Id == id),
            Deals = m.Deals.RemoveAll(deal => deal.CompanyId == id),
            Contacts = m.Contacts.RemoveAll(contact => contact.CompanyId == id),
            Activities = m.Activities.RemoveAll(activity => activity.CompanyId == id),
            Inbox = m.Inbox.RemoveAll(thread => thread.CompanyId == id),
            Favorites = m.Favorites.Remove(id),
            Plans = m.Plans.RemoveAll(plan => deals.Contains(plan.DealId))
        };
    }
}
