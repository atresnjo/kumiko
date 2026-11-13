using CRMLite.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void DetailsPanel(Company company)
    {
        var culture = C.Culture;
        var today = DateOnly.FromDateTime(Model.Now);
        var deal = Crm.LatestDeal(Model, company.Id);
        using var panel = C.Div(key: "details").Semantics(Role.ScrollArea, "Company details").Padding(Spacing.Panel).Gap(2);
        if (C.WidthBreakpoint < Breakpoint.Expanded)
            panel.FillWidth();
        else
            panel.Width(Sizes.Panel).FillHeight().Scroll();
        Label("Details", TextSize.Body, p.Text, Weight.Semibold, "title");
        using (C.Div(key: "title-gap").Height(8))
        {
        }
        Property("Stage", "stage", () =>
        {
            if (deal is null)
            {
                Label("No deal yet", TextSize.Body, p.Faint, key: "none");
                return;
            }
            var open = Model.Popup == Popup.DetailStage;
            using var trigger = C.Div(key: "trigger").Row().ItemsCenter().Padding(2).Rounded(Corners.Control)
                .Semantics(Role.Button, "Change stage", deal.Stage.ToString()).Accessibility(new(Expanded: open));
            var hit = trigger.Control(Model.CanEdit);
            Feedback(trigger, hit);
            StageTag(deal.Stage, "tag");
            Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.DetailStage));
            if (open)
                StageMenu(hit.Rect, deal.Stage, stage => Ui.Dispatch(new CrmMsg.SetDealStage(deal.Id, stage)), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "stage-menu");
        });
        Property("Deal value", "value", () => Label(Format.Money(Crm.Arr(Model, company.Id), culture), TextSize.Body, p.Text, key: "text"));
        Property("Owner", "owner", () =>
        {
            var owner = Crm.Member(company.OwnerId);
            var open = Model.Popup == Popup.DetailOwner;
            using var trigger = C.Div(key: "trigger").Row().ItemsCenter().Gap(6).Padding(new Edges(2, 6, 2, 2)).Rounded(Corners.Control)
                .Semantics(Role.Button, "Change owner", owner.Name).Accessibility(new(Expanded: open));
            var hit = trigger.Control(Model.CanEdit);
            Feedback(trigger, hit, open);
            Avatar(owner, 18, "avatar");
            Label(owner.Name, TextSize.Body, p.Text, key: "name");
            Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.DetailOwner));
            if (open)
                OwnerMenu(hit.Rect, company.OwnerId, id => Ui.Dispatch(new CrmMsg.SetOwner(company.Id, id ?? company.OwnerId)), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "owner-menu", false);
        });
        Property("Next step", "next", () => C.Text(company.NextStep, new(Size: TextSize.Body, Color: p.Text, Wrap: true), key: "text"));
        Property("Close date", "close", () =>
            Label(deal is null ? "—" : Format.Date(deal.Close, today, culture), TextSize.Body, p.Text, key: "text"));
        Property("Employees", "employees", () =>
            Label(company.Employees > 0 ? Format.Count(company.Employees, culture) : "—", TextSize.Body, p.Text, key: "text"));
        Property("Industry", "industry", () => Label(company.Industry, TextSize.Body, p.Text, key: "text"));
        Property("Location", "location", () => Label(company.Location, TextSize.Body, p.Text, key: "text"));
        Property("Hot account", "hot", () =>
        {
            ToggleSwitch.View(Ui, p, "Hot account", company.Hot,
                new CrmMsg.SetHot(company.Id, !company.Hot), enabled: Model.CanEdit, key: "switch");
        });
        Property("Last interaction", "last", () =>
            Label(Format.Relative(company.LastInteraction, Model.Now, culture), TextSize.Body, p.Text, key: "text"));
        Property("Created", "created", () =>
            Label(company.Created.ToString("MMM d, yyyy", culture), TextSize.Body, p.Text, key: "text"));
        using (C.Div(key: "people-gap").Height(18))
        {
        }
        Label("People", TextSize.Body, p.Text, Weight.Semibold, "people-title");
        foreach (var person in Crm.ContactsOf(Model, company.Id))
        {
            using var row = C.Div(key: person.Id).Row().FillWidth().ItemsCenter().Gap(Space.S2).Py(5);
            Avatar(person, 24, "avatar");
            using var who = C.Div(key: "who").Flex().Column().Gap(1);
            Label(person.Name, TextSize.Small, p.Text, Weight.Medium, "name");
            Label(person.Title, TextSize.Caption, p.Faint, key: "title");
        }
    }
    void StageMenu(Rect? anchor, Stage? selected, Action<Stage> chosen, Action close, NodeKey key, bool matchWidth = false)
    {
        using var menu = C.Kit.Popover(anchor, close, new(Width: matchWidth ? null : 190, Name: "Stage", MatchAnchorWidth: matchWidth), key: key);
        ulong previous = 0;
        foreach (var stage in Enum.GetValues<Stage>())
        {
            var dot = p.Of(stage).Dot;
            if (Option(stage.ToString(), selected == stage, ref previous, () => Dot(dot, 7, "dot"), key: stage.ToString()).Clicked)
                chosen(stage);
        }
    }
    void DealList(Company company)
    {
        var culture = C.Culture;
        using var list = C.Div(key: "deals").FillWidth().Column().Gap(Space.S2);
        foreach (var deal in Crm.DealsOf(Model, company.Id))
        {
            using var row = C.Div(key: deal.Id).Row().FillWidth().ItemsCenter().Gap(Space.S3).Padding(new Edges(10, 14, 10, 14))
                .OutlinedCard(p);
            using (C.Div(key: "names").Flex().Column().Gap(3))
            {
                Label(deal.Name, TextSize.Body, p.Text, Weight.Medium, "name");
                Label($"Closes {Format.Date(deal.Close, DateOnly.FromDateTime(Model.Now), culture)}", TextSize.Caption, p.Faint, key: "close");
            }
            StageTag(deal.Stage, "stage");
            Label(Format.Money(deal.Value, culture), TextSize.Body, p.Text, Weight.Medium, "value");
            Avatar(Crm.Member(deal.OwnerId), 22, "owner");
        }
        if (!Crm.DealsOf(Model, company.Id).Any())
            Label("No deals yet.", TextSize.Body, p.Secondary, key: "empty");
        using (C.Div(key: "actions").Row())
        {
            if (Model.CanEdit)
                Ui.OnPress(ActionButton.Ghost(C, p, "New deal", Hugeicons.Add01, "Add a deal", key: "add"), new CrmMsg.OpenDealDialog(company.Id));
        }
    }
    void ContactList(Company company)
    {
        using var list = C.Div(key: "people").FillWidth().Column();
        foreach (var person in Crm.ContactsOf(Model, company.Id))
        {
            using var row = C.Div(key: person.Id).Row().FillWidth().Height(52).ItemsCenter().Gap(Space.S3);
            Avatar(person, 30, "avatar");
            using (C.Div(key: "who").Flex().Column().Gap(2))
            {
                Label(person.Name, TextSize.Body, p.Text, Weight.Medium, "name");
                Label(person.Title, TextSize.Caption, p.Faint, key: "title");
            }
            Label(person.Email, TextSize.Small, p.Secondary, key: "email");
        }
    }
}
