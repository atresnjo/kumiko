using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace CRMLite;

public sealed partial class CrmApp
{
    public static (CrmModel, Cmd<CrmMsg>) Update(CrmModel m, CrmMsg msg)
    {
        if (msg is CrmMsg.Desktop desktop)
        {
            var (state, effects) = Update(m, desktop.Message);
            return Notify(m, state, effects.Map<CrmMsg>(static child => new CrmMsg.Desktop(child)));
        }
        var (next, command) = Transition(m, msg);
        // The shell's state belongs to the host, so sign-out and session resets keep it; Notify then clears notices.
        return Notify(m, next with
        {
            DesktopReport = m.DesktopReport
        }, command);
    }

    static (CrmModel, Cmd<CrmMsg>) Transition(CrmModel m, CrmMsg msg)
    {
        if (msg is CrmMsg.Authentication auth)
        {
            var (state, command) = Update(m, auth.Message);
            return (state, command.Map<CrmMsg>(static child => new CrmMsg.Authentication(child)));
        }
        if ((m.Auth.Session is null || !m.Backend.Loaded || m.Backend.Loading) && !IsNavigation(msg) || !m.CanEdit && RequiresEditor(msg))
            return (m, Cmd<CrmMsg>.None);
        var next = Reduce(m, msg);
        var changed = WorkspaceData.From(m) != WorkspaceData.From(next);
        if (changed && !m.CanEdit)
            return (m, Cmd<CrmMsg>.None);
        if (changed)
            next = next with
            {
                Backend = next.Backend with
                {
                    Revision = m.Backend.Revision + 1,
                    Saving = true,
                    Dirty = true,
                    Error = null
                }
            };
        return (next, Cmd<CrmMsg>.Batch(SearchCommand(m, next), changed ? SaveWorkspace(next).Map<CrmMsg>(static child => new CrmMsg.Authentication(child)) : Cmd<CrmMsg>.None));
    }

    /// <summary>Asks the company directory whenever an update numbered a new company search.</summary>
    static Cmd<CrmMsg> SearchCommand(CrmModel before, CrmModel after) =>
        after.Overlay is Overlay.NewDeal { Search: { Loading: true } search } && search.Request != ((before.Overlay as Overlay.NewDeal)?.Search?.Request ?? 0)
            ? CompanyDirectory.Search(after.Auth.Session!, search.Picker.Query, search.Request, after.Backend.Request)
            : Cmd<CrmMsg>.None;

    /// <summary>A company picker that already shows <paramref name="companyId"/>, labelled before any search answers.</summary>
    static CompanySearch NewSearch(CrmModel m, string? companyId) => new(Crm.Company(m, companyId) is { } company
        ? new DropdownState(company.Id)
        {
            Selection = [new(company.Id, company.Name)]
        }
        : new DropdownState());

    /// <summary>
    /// Keeps the picker's state and the deal's company in step. Opening the picker or changing its query numbers a new
    /// directory search; the picker owns the dialog's one open select while it is open.
    /// </summary>
    static CrmModel PickCompany(CrmModel m, DropdownState state)
    {
        if (m.Overlay is not Overlay.NewDeal deal)
            return m;
        var search = deal.Search ?? NewSearch(m, deal.Draft.CompanyId);
        var picker = state.IsOpen ? DealPicker.Company : deal.Picker == DealPicker.Company ? DealPicker.Closed : deal.Picker;
        var next = search with
        {
            Picker = state
        };
        if (state.IsOpen && (!search.Picker.IsOpen || state.Query != search.Picker.Query))
            next = next with
            {
                Request = m.Backend.SearchRequest + 1,
                Loading = true,
                Error = null
            };
        var draft = deal.Draft with
        {
            CompanyId = state.SelectedId
        };
        return m with
        {
            Backend = m.Backend with
            {
                SearchRequest = next.Request
            },
            Overlay = deal with
            {
                Draft = draft,
                Picker = picker,
                Search = next
            }
        };
    }

    static CrmModel Found(CrmModel m, int request, ImmutableArray<string> ids) =>
        WithSearch(m, request, search => search with { Loading = false, Error = null, Results = ids });

    /// <summary>Applies a directory answer only while it belongs to the latest search of the open New deal form.</summary>
    static CrmModel WithSearch(CrmModel m, int request, Func<CompanySearch, CompanySearch> change) =>
        m.Overlay is Overlay.NewDeal { Search: { } search } deal && search.Request == request
            ? m with
            {
                Overlay = deal with
                {
                    Search = change(search)
                }
            }
            : m;

    /// <summary>The industry filter's dropdown is the page's one open menu while it is open.</summary>
    static CrmModel FilterIndustries(CrmModel m, DropdownState state) => m with
    {
        IndustryPicker = state,
        Overlay = state.IsOpen ? new Overlay.Menu(PageMenu.IndustryFilter)
            : m.Popup == Popup.IndustryFilter ? new Overlay.None() : m.Overlay
    };
}
