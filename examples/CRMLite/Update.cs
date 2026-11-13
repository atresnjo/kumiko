using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace CRMLite;

public sealed partial class CrmApp : IKumiko<CrmModel, CrmMsg>, IKumikoComponent<CrmModel, AuthMsg>, IKumikoComponent<CrmModel, DesktopMsg>
{
    public static CrmModel Init => SignedOut();

    static CrmModel Reduce(CrmModel m, CrmMsg msg) => msg switch
    {
        CrmMsg.Authentication => m,
        CrmMsg.Desktop => m,
        CrmMsg.Navigate n => NavigateTo(m, m.Navigation.Reset(ScreenRoutes.Root(n.Page))),
        CrmMsg.Go go => go.Route is Screen.CompanyDetails company ? Open(m, company.Id) : NavigateTo(m, m.Navigation.Reset(go.Route)),
        CrmMsg.ShowList l => NavigateTo(m, m.Navigation.Reset(new Screen.Companies()), l.List),
        CrmMsg.OpenCompany o => Open(m, o.Id),
        CrmMsg.OpenThread t => OpenThread(m, t.Id),
        CrmMsg.StepCompany s => Step(m, s.Delta),
        CrmMsg.SetSearch s => m with { Search = s.Text },
        CrmMsg.SetStageFilter f => m with { StageFilter = f.Stage, Overlay = ClosePopup(m.Overlay) },
        CrmMsg.SetOwnerFilter f => m with { OwnerFilter = f.OwnerId, Overlay = ClosePopup(m.Overlay) },
        CrmMsg.FilterIndustries f => FilterIndustries(m, f.State),
        CrmMsg.PickCompany p => PickCompany(m, p.State),
        CrmMsg.CompaniesFound f => Found(m, f.Request, f.Ids),
        CrmMsg.CompanySearchFailed f => WithSearch(m, f.Request, s => s with { Loading = false, Error = "The company directory is unavailable. " + f.Error }),
        CrmMsg.SortBy s => m.Sort == s.Key ? m with { Descending = !m.Descending } : m with { Sort = s.Key, Descending = Crm.DescendingFirst(s.Key) },
        CrmMsg.SetTab t => EditEntry(m, t.EntryId, tab: t.Tab),
        CrmMsg.SetNote n => EditEntry(m, n.EntryId, note: n.Text),
        CrmMsg.AddNote n => AddNote(m, n.EntryId),
        CrmMsg.Back b => Back(m, b.Request),
        CrmMsg.MoveRoot r => m with { RootHistory = m.RootHistory.Move(r.Request, RootHost) },
        CrmMsg.ConfirmNavigation c => ConfirmNavigation(m, c.Request),
        CrmMsg.CancelNavigation => m with { PendingNavigation = null },
        CrmMsg.DeleteCompany d => DeleteCompany(m, d.Id),
        CrmMsg.SetDealStage s => MoveDeal(m, s.DealId, s.Stage),
        CrmMsg.MoveDealTo d => MoveDealTo(m, d.DealId, d.Stage, d.Index),
        CrmMsg.SetOwner o => SetOwner(m with { Overlay = m.Overlay is Overlay.RowMenu ? new Overlay.None() : m.Overlay }, o.CompanyId, o.OwnerId),
        CrmMsg.SetHot h => Edit(m with { Overlay = m.Overlay is Overlay.RowMenu ? new Overlay.None() : m.Overlay }, h.CompanyId, c => c with { Hot = h.Hot }),
        CrmMsg.OpenRowMenu r => Crm.Company(m, r.CompanyId) is null ? m : m with { Overlay = new Overlay.RowMenu(r.CompanyId, r.At, r.OwnerCell) },
        CrmMsg.OpenDealMenu d => m.Deals.Any(x => x.Id == d.DealId) ? m with { Overlay = new Overlay.DealMenu(d.DealId, d.At) } : m,
        CrmMsg.OpenDeal d => m.Deals.Any(x => x.Id == d.DealId) ? m with { Overlay = new Overlay.DealDrawer(d.DealId) } : m,
        CrmMsg.ToggleFavorite f => m with
        {
            Favorites = m.Favorites.Contains(f.CompanyId) ? m.Favorites.Remove(f.CompanyId) : m.Favorites.Add(f.CompanyId),
            Overlay = ClosePopup(m.Overlay)
        },
        CrmMsg.SetBoardOwner o => m with { BoardOwner = o.OwnerId, Overlay = ClosePopup(m.Overlay) },
        CrmMsg.SetPopup p => SetPopup(m, p.Popup),
        CrmMsg.SetQuery q => m with { Query = q.Text },
        CrmMsg.Jump j => Jump(m with { Overlay = ClosePopup(m.Overlay), Query = "" }, j.Target),
        CrmMsg.OpenDealDialog d => m with
        {
            Overlay = new Overlay.NewDeal(new(d.CompanyId, "", "", Stage.Qualified, m.CurrentUser.Id), Search: NewSearch(m, d.CompanyId))
        },
        CrmMsg.EditDeal e => m.Overlay is Overlay.NewDeal deal ? m with { Overlay = deal with { Draft = e.Draft, Picker = DealPicker.Closed } } : m,
        CrmMsg.SubmitDeal => SubmitDeal(m),
        CrmMsg.OpenCompanyDialog => m with { Overlay = new Overlay.NewCompany(new("", "", "", m.CurrentUser.Id)) },
        CrmMsg.EditCompany e => m.Overlay is Overlay.NewCompany ? m with { Overlay = new Overlay.NewCompany(e.Draft) } : m,
        CrmMsg.SubmitCompany => SubmitCompany(m),
        CrmMsg.CloseDialog => (m.Overlay is Overlay.Plan ? Minimize(EditStep(m, null)) : m) with { Overlay = new Overlay.None() },
        CrmMsg.SetDark d => m with { Dark = d.On, Overlay = ClosePopup(m.Overlay) },
        CrmMsg.ToggleSidebar => m with { SidebarCollapsed = !m.SidebarCollapsed, Overlay = ClosePopup(m.Overlay) },
        CrmMsg.OpenSettings o => m with
        {
            SettingsView = new(Open: true, Section: o.Section, Jump: new(o.Section, (m.Settings.Jump?.Request ?? 0) + 1, Instant: true)),
            Overlay = new Overlay.None()
        },
        CrmMsg.CloseSettings => m with { SettingsView = m.Settings with { Open = false, Menu = null, Query = "" } },
        CrmMsg.SetPreferences s => m with { Prefs = s.Preferences, SettingsView = m.Settings with { Menu = null } },
        CrmMsg.SetSettingsQuery q => m with { SettingsView = m.Settings with { Query = q.Text, Menu = null } },
        CrmMsg.SetSettingsMenu s => m with { SettingsView = m.Settings with { Menu = s.Menu } },
        CrmMsg.JumpToSection j => m with
        {
            SettingsView = m.Settings with
            {
                Section = j.Section,
                Menu = null,
                Jump = new(j.Section, (m.Settings.Jump?.Request ?? 0) + 1)
            }
        },
        CrmMsg.ScrolledToSection s => m with
        {
            SettingsView = m.Settings with
            {
                Section = s.Section,
                Jump = m.Settings.Jump is { } jump ? jump with
                {
                    Arrived = true
                } : null
            }
        },
        CrmMsg.OpenPlan o => OpenPlan(m, o.DealId),
        CrmMsg.EditStep e => EditStep(m, e.StepId),
        CrmMsg.RenameStep r => RenameStep(m, r.StepId, r.Title),
        CrmMsg.RemoveStep r => RemoveStep(m, r.StepId),
        CrmMsg.AddStep => AddStep(m),
        CrmMsg.MoveStep s => WithDraft(m, p => MoveStep(p, s.StepId, s.Index)),
        CrmMsg.KeepPlanning => KeepPlanning(m),
        CrmMsg.ApprovePlan => Approve(m),
        CrmMsg.PausePlan => WithPlan(m, p => p.Status == PlanStatus.Running ? p with { Status = PlanStatus.Paused } : p),
        CrmMsg.ResumePlan => WithPlan(m, p => p.Status == PlanStatus.Paused ? p with { Status = PlanStatus.Running } : p),
        CrmMsg.RetryStep => WithPlan(m, Retry),
        CrmMsg.SkipStep => WithPlan(m, Skip),
        CrmMsg.AgentTick => Tick(m),
        CrmMsg.DismissPlanToast d => m.PlanFor(d.DealId) is { } plan ? Save(m, plan, plan with { Minimized = false }) : m,
        CrmMsg.ResizeSidebar r => m with { SidebarWidth = Math.Clamp(MathF.Round(r.Width), CrmModel.MinSidebar, CrmModel.MaxSidebar) },
        _ => throw new ArgumentOutOfRangeException(nameof(msg))
    };

    /// <summary>Opens a company as a new visit; the company already on top stays that visit.</summary>
    static CrmModel Open(CrmModel m, string id) => NavigateTo(m, string.IsNullOrWhiteSpace(id) ? m.Navigation.Reset(new Screen.Companies())
        : m.Screen is Screen.CompanyDetails top && top.Id == id
        ? m.Navigation : m.Navigation.Push(new Screen.CompanyDetails(id)));

    static CrmModel OpenThread(CrmModel m, string id)
    {
        var thread = m.Inbox.FirstOrDefault(t => t.Id == id);
        if (thread is null)
            return m;
        if (!m.CanEdit)
            return Open(m, thread.CompanyId);
        return Open(m with
        {
            Inbox = m.Inbox.Replace(thread, thread with
            {
                Unread = false
            })
        }, thread.CompanyId);
    }

    /// <summary>Replaces the current visit with the previous or next company in table order; its draft is guarded.</summary>
    static CrmModel Step(CrmModel m, int delta)
    {
        var rows = Crm.Visible(m);
        var index = rows.IndexOf(rows.FirstOrDefault(c => c.Id == m.CompanyId)!);
        var next = index + delta;
        return index < 0 || next < 0 || next >= rows.Length ? m : NavigateTo(m, m.Navigation.Replace(new Screen.CompanyDetails(rows[next].Id)));
    }

    static CrmModel Edit(CrmModel m, string id, Func<Company, Company> change)
    {
        var company = Crm.Company(m, id);
        return company is null ? m : m with
        {
            Companies = m.Companies.Replace(company, change(company)),
            Overlay = ClosePopup(m.Overlay)
        };
    }

    static CrmModel AddNote(CrmModel m, int entryId)
    {
        var text = m.Note.Trim();
        if (entryId != m.Navigation.Top.Id || text.Length == 0 || m.CompanyId is not { } id)
            return m;
        var note = new Activity($"a-note-{m.Sequence}", id, ActivityKind.Note, m.Now, m.CurrentUser.Name, Body: text);
        return Edit(m, id, c => c with { LastInteraction = m.Now }) with
        {
            Activities = m.Activities.Add(note),
            Navigation = m.Navigation.Edit(entryId, static entry => entry with { Note = "" }),
            Sequence = m.Sequence + 1
        };
    }

    /// <summary>Changes a deal's stage from the board or the details panel and records it on the timeline.</summary>
    static CrmModel MoveDeal(CrmModel m, string dealId, Stage stage)
    {
        var deal = m.Deals.FirstOrDefault(d => d.Id == dealId);
        if (deal is null || deal.Stage == stage)
            return m with
            {
                Overlay = ClosePopup(m.Overlay)
            };
        var change = new Activity($"a-move-{m.Sequence}", deal.CompanyId, ActivityKind.StageChange, m.Now, m.CurrentUser.Name, From: deal.Stage, To: stage);
        return m with
        {
            Deals = m.Deals.Replace(deal, deal with
            {
                Stage = stage
            }),
            Activities = m.Activities.Add(change),
            Sequence = m.Sequence + 1,
            Overlay = ClosePopup(m.Overlay)
        };
    }

    /// <summary>
    /// Puts the held card into its target slot: the stage changes as from the details panel, and the deal moves
    /// before the slot's current card so the lane keeps the order the user dropped into.
    /// </summary>
    static CrmModel MoveDealTo(CrmModel m, string dealId, Stage stage, int index)
    {
        if (m.Deals.FirstOrDefault(d => d.Id == dealId) is not { } deal)
            return m;
        var moved = MoveDeal(m, deal.Id, stage);
        var held = moved.Deals.First(d => d.Id == deal.Id);
        var rest = moved.Deals.Remove(held);
        var lane = rest.Where(d => d.Stage == stage && (m.BoardOwner is null || d.OwnerId == m.BoardOwner)).ToArray();
        var slot = index < lane.Length ? rest.IndexOf(lane[Math.Max(0, index)]) : lane.Length > 0 ? rest.IndexOf(lane[^1]) + 1 : rest.Length;
        return moved with
        {
            Deals = rest.Insert(slot, held)
        };
    }

    static CrmModel SetOwner(CrmModel m, string companyId, string ownerId) => Edit(m, companyId, c => c with { OwnerId = ownerId }) with
    {
        Deals = [.. m.Deals.Select(d => d.CompanyId == companyId ? d with { OwnerId = ownerId } : d)]
    };

    /// <summary>Command menu targets are "company:{id}", "contact:{id}" or "page:{Page}".</summary>
    static CrmModel Jump(CrmModel m, string target)
    {
        if (target.StartsWith("company:", StringComparison.Ordinal))
            return Open(m, target["company:".Length..]);
        if (target.StartsWith("contact:", StringComparison.Ordinal) && m.Contacts.FirstOrDefault(p => p.Id == target["contact:".Length..]) is { } contact)
            return Open(m, contact.CompanyId);
        if (target.StartsWith("page:", StringComparison.Ordinal) && Enum.TryParse<ScreenKind>(target["page:".Length..], out var page))
            return NavigateTo(m, m.Navigation.Reset(ScreenRoutes.Root(page)));
        return m;
    }

    static CrmModel SubmitDeal(CrmModel m)
    {
        if (m.Overlay is not Overlay.NewDeal { Draft: var draft } open)
            return m;
        if (Crm.DealErrors(m, draft) is not (null, null, null) || Format.ParseMoney(draft.Value) is not { } value)
            return m with
            {
                Overlay = open with
                {
                    Draft = draft with
                    {
                        Submitted = true
                    },
                    Picker = DealPicker.Closed
                }
            };
        var close = DateOnly.FromDateTime(m.Now).AddDays(45);
        var deal = new Deal($"d-new-{m.Sequence}", draft.CompanyId!, draft.Name.Trim(), value, draft.Stage, close, draft.OwnerId);
        return m with
        {
            Deals = m.Deals.Add(deal),
            Overlay = new Overlay.None(),
            Sequence = m.Sequence + 1
        };
    }

    static readonly ImmutableArray<uint> LogoColors = [0x0369a1u, 0xc2410cu, 0x7c3aedu, 0x047857u, 0xb91c1cu, 0x0f766eu];

    static CrmModel SubmitCompany(CrmModel m)
    {
        if (m.CompanyDraft is not { } draft)
            return m;
        if (Crm.CompanyErrors(draft) is not (null, null))
            return m with
            {
                Overlay = new Overlay.NewCompany(draft with
                {
                    Submitted = true
                })
            };
        var owner = Crm.Member(draft.OwnerId);
        var industry = string.IsNullOrWhiteSpace(draft.Industry) ? "Unknown" : draft.Industry.Trim();
        var company = new Company($"c-new-{m.Sequence}", draft.Name.Trim(), draft.Domain.Trim().ToLowerInvariant(), industry, "—",
            $"Added by {m.CurrentUser.Name}.", LogoColors[m.Sequence % LogoColors.Length], owner.Id, 0, m.Now, "Book a discovery call", false,
            DateOnly.FromDateTime(m.Now));
        var created = new Activity($"a-created-{m.Sequence}", company.Id, ActivityKind.Created, m.Now, m.CurrentUser.Name, "manually");
        return Open(m with
        {
            Companies = m.Companies.Add(company),
            Activities = m.Activities.Add(created),
            Overlay = new Overlay.None(),
            Sequence = m.Sequence + 1
        }, company.Id);
    }
}
