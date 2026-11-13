namespace CRMLite;

public sealed partial class CrmApp
{
    static Overlay ClosePopup(Overlay overlay) => overlay switch
    {
        Overlay.NewDeal deal => deal with { Picker = DealPicker.Closed },
        Overlay.NewCompany company => company with { Picker = CompanyPicker.Closed },
        Overlay.DealDrawer drawer => drawer with { StageOpen = false },
        Overlay.Plan plan => plan,
        _ => new Overlay.None()
    };

    static CrmModel SetPopup(CrmModel m, Popup popup)
    {
        var overlay = popup switch
        {
            Popup.None => ClosePopup(m.Overlay),
            Popup.DraftCompany when m.Overlay is Overlay.NewDeal deal => deal with { Picker = DealPicker.Company },
            Popup.DraftStage when m.Overlay is Overlay.NewDeal deal => deal with { Picker = DealPicker.Stage },
            Popup.DraftOwner when m.Overlay is Overlay.NewDeal deal => deal with { Picker = DealPicker.Owner },
            Popup.NewCompanyOwner when m.Overlay is Overlay.NewCompany company => company with { Picker = CompanyPicker.Owner },
            Popup.DrawerStage when m.Overlay is Overlay.DealDrawer drawer => drawer with { StageOpen = true },
            _ when m.Overlay is Overlay.NewDeal or Overlay.NewCompany or Overlay.Plan => m.Overlay,
            Popup.Workspace => new Overlay.Menu(PageMenu.Workspace),
            Popup.Account => new Overlay.Menu(PageMenu.Account),
            Popup.Search => new Overlay.Menu(PageMenu.Search),
            Popup.StageFilter => new Overlay.Menu(PageMenu.StageFilter),
            Popup.OwnerFilter => new Overlay.Menu(PageMenu.OwnerFilter),
            Popup.BoardOwner => new Overlay.Menu(PageMenu.BoardOwner),
            Popup.DetailStage => new Overlay.Menu(PageMenu.DetailStage),
            Popup.DetailOwner => new Overlay.Menu(PageMenu.DetailOwner),
            Popup.More => new Overlay.Menu(PageMenu.More),
            _ => m.Overlay
        };
        return m with
        {
            Overlay = overlay,
            Query = popup == Popup.Search ? "" : m.Query
        };
    }
}
