using System.Text.Json.Serialization;
using Kumiko.Runtime;

namespace CRMLite;

[RouteUnion("screen")]
public abstract partial record Screen
{
    private Screen()
    {
    }
    [Route("companies", "companies", 1)] public sealed record Companies : Screen;
    [Route("companies/{Id}", "company", 4, Name = "Company")] public sealed record CompanyDetails(string Id) : Screen;
    [Route("deals", "deals", 3)] public sealed record Deals : Screen;
    [Route("people", "people", 2)] public sealed record People : Screen;
    [Route("inbox", "inbox", 0)] public sealed record Inbox : Screen;
}

public enum DealPicker
{
    Closed, Company, Stage, Owner
}
public enum CompanyPicker
{
    Closed, Owner
}
public enum PageMenu
{
    Workspace, Account, Search, StageFilter, OwnerFilter, BoardOwner, DetailStage, DetailOwner, More, IndustryFilter
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "overlay")]
[JsonDerivedType(typeof(Overlay.None), "none")]
[JsonDerivedType(typeof(Overlay.Menu), "menu")]
[JsonDerivedType(typeof(Overlay.NewCompany), "new-company")]
[JsonDerivedType(typeof(Overlay.NewDeal), "new-deal")]
[JsonDerivedType(typeof(Overlay.RowMenu), "row-menu")]
[JsonDerivedType(typeof(Overlay.DealMenu), "deal-menu")]
[JsonDerivedType(typeof(Overlay.DealDrawer), "deal-drawer")]
[JsonDerivedType(typeof(Overlay.Plan), "plan")]
public abstract record Overlay
{
    private Overlay()
    {
    }
    public sealed record None : Overlay;
    public sealed record Menu(PageMenu Kind) : Overlay;
    public sealed record NewCompany(CompanyDraft Draft, CompanyPicker Picker = CompanyPicker.Closed) : Overlay;
    /// <summary>The New deal form; <paramref name="Search"/> is its company picker and the directory search behind it.</summary>
    public sealed record NewDeal(DealDraft Draft, DealPicker Picker = DealPicker.Closed, CompanySearch? Search = null) : Overlay;
    /// <summary>A company row's actions, opened at the window point <paramref name="At"/>.</summary>
    /// <summary>
    /// A company row's actions, opened at the window point <paramref name="At"/>; an owner cell adds owner choices.
    /// </summary>
    public sealed record RowMenu(string CompanyId, Kumiko.UI.Point At, bool OwnerCell = false) : Overlay;
    /// <summary>A deal card's actions and stages, opened at the window point <paramref name="At"/>.</summary>
    public sealed record DealMenu(string DealId, Kumiko.UI.Point At) : Overlay;
    /// <summary>A deal's details docked at the window edge; <paramref name="StageOpen"/> shows its stage menu.</summary>
    public sealed record DealDrawer(string DealId, bool StageOpen = false) : Overlay;
    /// <summary>A deal's agent plan in a dialog; <paramref name="Editing"/> is the draft step whose title is being edited.</summary>
    public sealed record Plan(string DealId, string? Editing = null) : Overlay;
}

public sealed partial record CrmModel
{
    [JsonIgnore] public Screen Screen => Navigation.Top.Route;
    public Overlay Overlay { get; init; } = new Overlay.None();

    [JsonIgnore] public string? CompanyId => (Screen as Screen.CompanyDetails)?.Id;
    [JsonIgnore] public DealDraft? DealDraft => (Overlay as Overlay.NewDeal)?.Draft;
    [JsonIgnore] public CompanyDraft? CompanyDraft => (Overlay as Overlay.NewCompany)?.Draft;
    [JsonIgnore]
    public Popup Popup => Overlay switch
    {
        Overlay.Menu menu => menu.Kind switch
        {
            PageMenu.Workspace => Popup.Workspace,
            PageMenu.Account => Popup.Account,
            PageMenu.Search => Popup.Search,
            PageMenu.StageFilter => Popup.StageFilter,
            PageMenu.OwnerFilter => Popup.OwnerFilter,
            PageMenu.BoardOwner => Popup.BoardOwner,
            PageMenu.DetailStage => Popup.DetailStage,
            PageMenu.DetailOwner => Popup.DetailOwner,
            PageMenu.IndustryFilter => Popup.IndustryFilter,
            _ => Popup.More
        },
        Overlay.NewDeal { Picker: DealPicker.Company } => Popup.DraftCompany,
        Overlay.NewDeal { Picker: DealPicker.Stage } => Popup.DraftStage,
        Overlay.NewDeal { Picker: DealPicker.Owner } => Popup.DraftOwner,
        Overlay.NewCompany { Picker: CompanyPicker.Owner } => Popup.NewCompanyOwner,
        Overlay.DealDrawer { StageOpen: true } => Popup.DrawerStage,
        _ => Popup.None
    };
}
