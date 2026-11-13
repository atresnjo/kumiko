using System.Collections.Immutable;

namespace CRMLite;

public enum CompanyList
{
    All,
    Mine,
    Hot
}

public enum SortKey
{
    Company,
    Stage,
    Arr,
    Employees,
    Owner,
    LastInteraction
}

public enum DetailTab
{
    Activity,
    Deals,
    People,
    Notes
}

/// <summary>The single open popover or menu; opening one closes any other.</summary>
public enum Popup
{
    None,
    Workspace,
    Account,
    Search,
    StageFilter,
    OwnerFilter,
    BoardOwner,
    DetailStage,
    DetailOwner,
    More,
    DraftCompany,
    DraftStage,
    DraftOwner,
    NewCompanyOwner,
    DrawerStage,
    IndustryFilter
}

/// <summary>The New deal form. Errors appear only after the first submit attempt.</summary>
public sealed record DealDraft(string? CompanyId, string Name, string Value, Stage Stage, string OwnerId, bool Submitted = false);

public sealed record CompanyDraft(string Name, string Domain, string Industry, string OwnerId, bool Submitted = false);

/// <summary>
/// Everything CRMLite shows. <paramref name="Now"/> is the pinned demo clock, so relative dates and
/// snapshots are repeatable; new notes are stamped with it.
/// </summary>
public sealed partial record CrmModel(
    ImmutableArray<Company> Companies,
    ImmutableArray<Deal> Deals,
    ImmutableArray<Contact> Contacts,
    ImmutableArray<Activity> Activities,
    ImmutableArray<InboxThread> Inbox,
    ImmutableArray<string> Favorites,
    DateTime Now,
    bool Dark = false,
    CompanyList List = CompanyList.All,
    string Search = "",
    Stage? StageFilter = null,
    string? OwnerFilter = null,
    SortKey Sort = SortKey.LastInteraction,
    bool Descending = true,
    string? BoardOwner = null,
    string Query = "",
    int Sequence = 0,
    float SidebarWidth = CrmModel.DefaultSidebar,
    bool SidebarCollapsed = false,
    Preferences? Prefs = null,
    SettingsState? SettingsView = null
)
{
    public Preferences Preferences => Prefs ?? Preferences.Default;

    public SettingsState Settings => SettingsView ?? new();

    /// <summary>The expanded sidebar's width limits; collapsing keeps the chosen width for the next expand.</summary>
    public const float DefaultSidebar = 232, MinSidebar = 200, MaxSidebar = 360;
}
