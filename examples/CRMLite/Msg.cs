using System.Collections.Immutable;
using Kumiko.UI;

namespace CRMLite;

public abstract partial record CrmMsg
{
    public sealed record Back(CrmBackRequest Request) : CrmMsg;
    public sealed record ConfirmNavigation(CrmBackRequest Request) : CrmMsg;
    public sealed record CancelNavigation : CrmMsg;
    public sealed record DeleteCompany(string Id) : CrmMsg;
    public sealed record Navigate(ScreenKind Page) : CrmMsg;
    /// <summary>A Back/Forward request for the application screen's history, not for the page path.</summary>
    public sealed record MoveRoot(Kumiko.Runtime.HistoryRequest Request) : CrmMsg;
    public sealed record Go(Screen Route) : CrmMsg;
    public sealed record ShowList(CompanyList List) : CrmMsg;
    public sealed record OpenCompany(string Id) : CrmMsg;
    public sealed record OpenThread(string Id) : CrmMsg;
    public sealed record StepCompany(int Delta) : CrmMsg;
    public sealed record SetSearch(string Text) : CrmMsg;
    public sealed record SetStageFilter(Stage? Stage) : CrmMsg;
    public sealed record SetOwnerFilter(string? OwnerId) : CrmMsg;
    /// <summary>The industry filter's dropdown changed: opened, searched, or chose industries.</summary>
    public sealed record FilterIndustries(DropdownState State) : CrmMsg;
    /// <summary>The New deal company picker changed; a new query searches the company directory.</summary>
    public sealed record PickCompany(DropdownState State) : CrmMsg;
    /// <summary>The directory's answer to search <paramref name="Request"/>; superseded answers are ignored.</summary>
    public sealed record CompaniesFound(int Request, ImmutableArray<string> Ids) : CrmMsg;
    public sealed record CompanySearchFailed(int Request, string Error) : CrmMsg;
    public sealed record SortBy(SortKey Key) : CrmMsg;
    public sealed record SetTab(int EntryId, DetailTab Tab) : CrmMsg;
    public sealed record SetNote(int EntryId, string Text) : CrmMsg;
    public sealed record AddNote(int EntryId) : CrmMsg;
    public sealed record SetDealStage(string DealId, Stage Stage) : CrmMsg;
    /// <summary>A deal dropped on the board: it moves to <paramref name="Stage"/> before the lane's card at <paramref name="Index"/>.</summary>
    public sealed record MoveDealTo(string DealId, Stage Stage, int Index) : CrmMsg;
    public sealed record SetOwner(string CompanyId, string OwnerId) : CrmMsg;
    public sealed record SetHot(string CompanyId, bool Hot) : CrmMsg;
    public sealed record OpenRowMenu(string CompanyId, Point At, bool OwnerCell = false) : CrmMsg;
    public sealed record OpenDealMenu(string DealId, Point At) : CrmMsg;
    public sealed record OpenDeal(string DealId) : CrmMsg;
    public sealed record ToggleFavorite(string CompanyId) : CrmMsg;
    public sealed record SetBoardOwner(string? OwnerId) : CrmMsg;
    public sealed record SetPopup(Popup Popup) : CrmMsg;
    public sealed record SetQuery(string Text) : CrmMsg;
    public sealed record Jump(string Target) : CrmMsg;
    public sealed record OpenDealDialog(string? CompanyId) : CrmMsg;
    public sealed record EditDeal(DealDraft Draft) : CrmMsg;
    public sealed record SubmitDeal : CrmMsg;
    public sealed record OpenCompanyDialog : CrmMsg;
    public sealed record EditCompany(CompanyDraft Draft) : CrmMsg;
    public sealed record SubmitCompany : CrmMsg;
    public sealed record CloseDialog : CrmMsg;
    public sealed record SetDark(bool On) : CrmMsg;
    public sealed record ToggleSidebar : CrmMsg;
    public sealed record ResizeSidebar(float Width) : CrmMsg;
    /// <summary>Opens settings scrolled to <paramref name="Section"/>.</summary>
    public sealed record OpenSettings(SettingsSection Section = SettingsSection.General) : CrmMsg;
    public sealed record CloseSettings : CrmMsg;
    public sealed record SetPreferences(Preferences Preferences) : CrmMsg;
    public sealed record SetSettingsQuery(string Text) : CrmMsg;
    public sealed record SetSettingsMenu(string? Menu) : CrmMsg;
    public sealed record JumpToSection(SettingsSection Section) : CrmMsg;
    public sealed record ScrolledToSection(SettingsSection Section) : CrmMsg;
    /// <summary>Opens the deal's agent plan, drafting one first if it has none.</summary>
    public sealed record OpenPlan(string DealId) : CrmMsg;
    /// <summary>Starts editing a draft step's title, or stops editing when <paramref name="StepId"/> is null.</summary>
    public sealed record EditStep(string? StepId) : CrmMsg;
    public sealed record RenameStep(string StepId, string Title) : CrmMsg;
    public sealed record RemoveStep(string StepId) : CrmMsg;
    public sealed record AddStep : CrmMsg;
    /// <summary>Moves a draft step so it lands at <paramref name="Index"/> among the other steps.</summary>
    public sealed record MoveStep(string StepId, int Index) : CrmMsg;
    public sealed record KeepPlanning : CrmMsg;
    public sealed record ApprovePlan : CrmMsg;
    public sealed record PausePlan : CrmMsg;
    public sealed record ResumePlan : CrmMsg;
    public sealed record RetryStep : CrmMsg;
    public sealed record SkipStep : CrmMsg;
    /// <summary>Closes a minimized plan's toast; the plan keeps running.</summary>
    public sealed record DismissPlanToast(string DealId) : CrmMsg;
    /// <summary>Advances every running plan by one beat of agent work.</summary>
    public sealed record AgentTick : CrmMsg;
}
