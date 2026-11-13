using CRMLite.Components;
using Kumiko.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void DealDrawer(Overlay.DealDrawer open)
    {
        if (Model.Deals.FirstOrDefault(d => d.Id == open.DealId) is not { } deal || Crm.Company(Model, deal.CompanyId) is not { } company)
            return;
        var culture = C.Culture;
        var kit = C.Kit;
        // Escape closes the stage menu before the drawer.
        using var drawer = kit.Drawer(deal.Name, () => Ui.Dispatch(open.StageOpen ? new CrmMsg.SetPopup(Popup.None) : new CrmMsg.CloseDialog()),
            new(Description: $"{company.Name}, {company.Domain}", Size: 440), key: "deal-drawer");
        using (C.Div(key: "amount").FillWidth().Column().Gap(4))
        {
            C.Text(Format.Money(deal.Value, culture), new(Size: 28, Color: p.Text, Weight: Weight.Semibold), key: "value");
            var weighted = (long)Math.Round(deal.Value * Crm.Probability(deal.Stage));
            Label("Weighted " + Format.Money(weighted, culture), TextSize.Small, p.Faint, key: "weighted");
        }
        Rule("amount-rule");
        using (C.Div(key: "fields").FillWidth().Column().Gap(2))
        {
            DrawerProperty("Stage", "stage", () => DrawerStage(open, deal));
            DrawerProperty("Probability", "probability", () =>
                Label(Crm.Probability(deal.Stage).ToString("P0", culture), TextSize.Body, p.Text, key: "text"));
            DrawerProperty("Close date", "close", () => Label(Format.Close(deal.Close, Model.Now, culture).Text, TextSize.Body, p.Text, key: "text"));
            DrawerProperty("Owner", "owner", () =>
            {
                var owner = Crm.Member(deal.OwnerId);
                Avatar(owner, 22, "avatar");
                Label(owner.Name, TextSize.Body, p.Text, key: "name");
            });
            var contact = Crm.ContactsOf(Model, company.Id).FirstOrDefault();
            DrawerProperty("Contact", "contact", () =>
            {
                if (contact is null)
                {
                    Label("—", TextSize.Body, p.Faint, key: "none");
                    return;
                }
                Avatar(contact, 22, "avatar");
                Label(contact.Name, TextSize.Body, p.Text, key: "name");
            });
            DrawerProperty("Next step", "next", () => C.Text(company.NextStep, new(Size: TextSize.Body, Color: p.Text, Wrap: true), key: "text"));
        }
        using (C.Div(key: "actions").Row().FillWidth().Gap(Space.S2).Padding(new Edges(Space.S2, 0, Space.S2, 0)))
        {
            Ui.OnPress(ActionButton.Primary(C, p, "Open " + company.Name, Hugeicons.ArrowUpRight01, key: "open"), new CrmMsg.OpenCompany(company.Id));
            var thread = Model.Inbox.FirstOrDefault(t => t.CompanyId == company.Id);
            var first = Crm.ContactsOf(Model, company.Id).FirstOrDefault()?.Name.Split(' ')[0];
            if (thread is not null && first is not null)
                Ui.OnPress(ActionButton.Ghost(C, p, "Email " + first, Hugeicons.Mail01, "Email " + first, key: "email"), new CrmMsg.OpenThread(thread.Id));
            var plan = PlanLabel(Model.PlanFor(deal.Id));
            if (Model.CanEdit)
                Ui.OnPress(ActionButton.Ghost(C, p, plan, Hugeicons.Asterisk02, plan, key: "plan"), new CrmMsg.OpenPlan(deal.Id));
        }
        Label("Recent activity", TextSize.Body, p.Text, Weight.Semibold, "activity-title");
        foreach (var activity in Crm.Timeline(Model, company.Id).Take(3))
        {
            using var row = C.Div(key: activity.Id).FillWidth().Column().Gap(3);
            C.Text(Headline(activity), new(Size: TextSize.Body, Color: p.Text, Wrap: true), key: "title");
            Label($"{activity.Actor}, {Format.Relative(activity.At, Model.Now, culture)}", TextSize.Small, p.Faint, key: "meta");
        }
    }

    /// <summary>The drawer's way into the deal's agent plan, naming where the plan stands.</summary>
    static string PlanLabel(AgentPlan? plan) => plan?.Status switch
    {
        null => "Plan with agent",
        PlanStatus.Draft => "Review plan",
        PlanStatus.Failed => "Plan needs you",
        PlanStatus.Done => "Plan done",
        _ => $"Plan, step {plan.Current + 1} of {plan.Steps.Length}"
    };

    static string Headline(Activity a) => a switch
    {
        { Kind: ActivityKind.Created } => a.Title.Length > 0 ? "Created " + a.Title : "Created",
        { Title.Length: > 0 } => a.Title,
        { Kind: ActivityKind.StageChange, From: { } from, To: { } to } => $"Moved from {from} to {to}",
        _ => $"{a.Actor} {Parts.Verb(a.Kind)}"
    };
    void DrawerStage(Overlay.DealDrawer open, Deal deal)
    {
        using var trigger = C.Div(key: "trigger").Row().ItemsCenter().Gap(6).Padding(new Edges(3, 8, 3, 3)).Rounded(Corners.Control)
            .Border(p.Border).Semantics(Role.ComboBox, "Stage", deal.Stage.ToString()).Accessibility(new(Expanded: open.StageOpen));
        var hit = trigger.Control(Model.CanEdit);
        Feedback(trigger, hit, open.StageOpen);
        StageTag(deal.Stage, "tag");
        C.Icon(Hugeicons.ArrowDown01, 10, p.Secondary, key: "chevron");
        Ui.OnPress(hit, new CrmMsg.SetPopup(open.StageOpen ? Popup.None : Popup.DrawerStage));
        if (open.StageOpen)
            StageMenu(hit.Rect, deal.Stage, stage => Ui.Dispatch(new CrmMsg.SetDealStage(deal.Id, stage)), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "stage-menu");
    }
    void DealMenu(Overlay.DealMenu open)
    {
        if (Model.Deals.FirstOrDefault(d => d.Id == open.DealId) is not { } deal || Crm.Company(Model, deal.CompanyId) is not { } company)
            return;
        var kit = C.Kit;
        using var menu = kit.Popover(new Rect(open.At.X, open.At.Y, 0, 0), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)),
            new(Width: 220, Offset: 2, Name: $"{deal.Name} actions"), key: "deal-menu");
        Ui.OnPress(kit.MenuItem("Open deal", new(Hugeicons.Note01), key: "open"), new CrmMsg.OpenDeal(deal.Id));
        Ui.OnPress(kit.MenuItem("Open company", new(Hugeicons.ArrowUpRight01), key: "company"), new CrmMsg.OpenCompany(company.Id));
        Ui.OnPress(kit.MenuItem(PlanLabel(Model.PlanFor(deal.Id)), new(Hugeicons.Asterisk02, Disabled: !Model.CanEdit), key: "plan"), new CrmMsg.OpenPlan(deal.Id));
        kit.MenuSeparator(key: "rule");
        kit.MenuLabel("Move to", key: "stages");
        foreach (var stage in Enum.GetValues<Stage>())
            Ui.OnPress(kit.MenuItem(stage.ToString(), new(Checked: stage == deal.Stage, Disabled: !Model.CanEdit), key: stage.ToString()), new CrmMsg.SetDealStage(deal.Id, stage));
    }
}
