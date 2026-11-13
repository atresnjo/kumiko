using CRMLite.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    internal void CompanyPage(Company company)
    {
        var compact = C.WidthBreakpoint < Breakpoint.Expanded;
        CompanyHeader(company);
        Rule("header-rule");
        using var body = C.Div(key: "body").Row().FillWidth().Flex();
        using (C.Div(key: "center").Flex().FillHeight().Scroll().Semantics(Role.ScrollArea, "Company activity"))
        using (C.Div(key: "column").FillWidth().Column().Items(Align.Center).Padding(new Edges(28, Space.S5, Space.S6, Space.S5)))
        using (C.Div(key: "content").FillWidth().MaxWidth(Sizes.Column).Column().Gap(Space.S4))
        {
            Identity(company);
            DetailTabs(company);
            switch (Model.Tab)
            {
                case DetailTab.Deals:
                    DealList(company);
                    break;
                case DetailTab.People:
                    ContactList(company);
                    break;
                case DetailTab.Notes:
                    Timeline([.. Crm.Timeline(Model, company.Id).Where(a => a.Kind == ActivityKind.Note)], "notes");
                    break;
                default:
                    Composer();
                    Timeline([.. Crm.Timeline(Model, company.Id)], "timeline");
                    break;
            }
            if (compact)
                DetailsPanel(company);
        }
        if (!compact)
        {
            VRule("panel-rule");
            DetailsPanel(company);
        }
    }
    void CompanyHeader(Company company)
    {
        var rows = Crm.Visible(Model);
        var index = rows.IndexOf(company);
        using var bar = Header(Hugeicons.Building03, "header");
        if (C.WidthBreakpoint < Breakpoint.Expanded)
            bar.Height(Length.Auto).MinHeight(Sizes.Header).Wrap().Py(Space.S2).RowGap(Space.S2);
        using (var back = C.Div(key: "back").Row().Height(Sizes.Control).ItemsCenter().Px(6).Rounded(Corners.Control)
            .Semantics(Role.Button, "Back to companies"))
        {
            var hit = back.Control();
            Feedback(back, hit);
            Label("Companies", TextSize.Title, p.Secondary, key: "label");
            // Return to a Companies root with its retained state; from another section, open Companies.
            CrmMsg leave = Model.Navigation.Entries[0].Route is Screen.Companies
                ? new CrmMsg.Back(Model.BackTo(Model.Navigation.Entries[0].Id))
                : new CrmMsg.Navigate(ScreenKind.Companies);
            Ui.OnPress(hit, leave);
        }
        C.Icon(Hugeicons.ArrowRight01, 10, p.Faint, key: "chevron");
        var position = index < 0 ? company.Name : $"{Format.Count(index + 1, C.Culture)} of {Format.Count(rows.Length, C.Culture)}";
        Label(position, TextSize.Title, p.Text, Weight.Medium, "position");
        C.Spacer(key: "space");
        if (Model.Navigation.Entries.Length > 2)
            Ui.OnPress(ActionButton.Ghost(C, p, "Back", null, "Back", key: "back-one"),
                new CrmMsg.Back(Model.BackTo(Model.Navigation.Entries[^2].Id)));
        var favorite = Model.Favorites.Contains(company.Id);
        Ui.OnPress(ActionButton.IconButton(C, p, Hugeicons.Star, favorite ? "Remove from favorites" : "Add to favorites",
            enabled: Model.CanEdit, color: favorite ? p.Star : null, key: "favorite"), new CrmMsg.ToggleFavorite(company.Id));
        Ui.OnPress(ActionButton.IconButton(C, p, Hugeicons.ArrowLeft01, "Previous company", index > 0, key: "previous"), new CrmMsg.StepCompany(-1));
        Ui.OnPress(ActionButton.IconButton(C, p, Hugeicons.ArrowRight01, "Next company", index >= 0 && index < rows.Length - 1, key: "next"), new CrmMsg.StepCompany(1));
        MoreMenu(company);
        if (Model.CanEdit)
            Ui.OnPress(ActionButton.Primary(C, p, "New deal", key: "new-deal"), new CrmMsg.OpenDealDialog(company.Id));
    }
    void MoreMenu(Company company)
    {
        var open = Model.Popup == Popup.More;
        var hit = ActionButton.IconButton(C, p, Hugeicons.MoreHorizontal, "More actions", key: "more");
        Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.More));
        if (!open)
            return;
        var kit = C.Kit;
        using var menu = kit.Popover(hit.Rect, () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)),
            new(Align: Kumiko.Components.PopoverAlign.End, Width: 230, Name: "More actions"), key: "more-menu");
        Ui.OnPress(kit.MenuItem(company.Hot ? "Remove from hot accounts" : "Mark as hot account", new(Hugeicons.Fire, Disabled: !Model.CanEdit), key: "hot"), new CrmMsg.SetHot(company.Id, !company.Hot));
        var favorite = Model.Favorites.Contains(company.Id);
        Ui.OnPress(kit.MenuItem(favorite ? "Remove from favorites" : "Add to favorites", new(Hugeicons.Star, Disabled: !Model.CanEdit), key: "favorite"), new CrmMsg.ToggleFavorite(company.Id));
        kit.MenuSeparator(key: "rule");
        Ui.OnPress(kit.MenuItem("Back to companies", new(Hugeicons.Building03), key: "back"), new CrmMsg.Navigate(ScreenKind.Companies));
    }
    void Identity(Company company)
    {
        using (C.Div(key: "identity").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            Logo(company, 40, "logo");
            using var names = C.Div(key: "names").Flex().Column().Gap(3);
            C.Text(company.Name, new(Size: TextSize.Name, Weight: Weight.Semibold, Color: p.Text, Wrap: true), key: "name");
            using (C.Div(key: "meta").Row().Wrap().ItemsCenter().Gap(6))
            {
                Label(company.Domain, TextSize.Caption, p.Secondary, key: "domain");
                Label("·", TextSize.Caption, p.Faint, key: "dot1");
                Label(company.Industry, TextSize.Caption, p.Secondary, key: "industry");
                Label("·", TextSize.Caption, p.Faint, key: "dot2");
                Label(company.Location, TextSize.Caption, p.Secondary, key: "location");
            }
        }
        C.Text(company.Description, new(Size: TextSize.Body, Color: p.Secondary, Wrap: true, Leading: 1.35f), key: "description");
    }
    void DetailTabs(Company company)
    {
        var culture = C.Culture;
        (DetailTab Tab, string Label, int Count)[] tabs =
        [
            (DetailTab.Activity, "Activity", Crm.Timeline(Model, company.Id).Count()),
            (DetailTab.Deals, "Deals", Crm.DealsOf(Model, company.Id).Count()),
            (DetailTab.People, "People", Crm.ContactsOf(Model, company.Id).Count()),
            (DetailTab.Notes, "Notes", Crm.Timeline(Model, company.Id).Count(a => a.Kind == ActivityKind.Note))
        ];
        using var block = C.Div(key: "tabs-block").FillWidth().Column();
        using (C.Div(key: "tabs").Row().FillWidth().Height(36).Gap(Space.S4).Semantics(Role.Group, "Company sections"))
        {
            foreach (var (tab, label, count) in tabs)
            {
                Ui.OnPress(Tab("company-sections", label, Format.Count(count, culture), Model.Tab == tab, key: label), new CrmMsg.SetTab(Model.Navigation.Top.Id, tab));
            }
        }
        Rule("tabs-rule");
    }
    void Composer()
    {
        if (!Model.CanEdit)
            return;
        using var row = C.Div(key: "composer").Row().FillWidth().ItemsCenter().Gap(Space.S2);
        Avatar(Model.CurrentUser, 24, "avatar");
        using var field = C.Div(key: "field").Flex();
        if (C.FocusWithin(field.Id) && C.Shortcut(Key.Enter, Modifiers.None))
            Ui.Dispatch(new CrmMsg.AddNote(Model.Navigation.Top.Id));
        var width = MathF.Max(field.PreviousRect?.Width ?? Sizes.Column - 32, 200);
        var entryId = Model.Navigation.Top.Id;
        Ui.TextField(key: "note", label: "Add a note", value: Model.Note, onChange: text => new CrmMsg.SetNote(entryId, text),
            options: new("Add a note", width));
    }
}
