using System.Collections.Immutable;
using CRMLite.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    internal void DealsPage()
    {
        var deals = Crm.BoardDeals(Model).ToImmutableArray();
        using (Header(Hugeicons.Briefcase01, "header"))
        {
            Label("Deals", TextSize.Title, p.Text, Weight.Medium, "title");
            C.Spacer(key: "space");
            BoardOwnerFilter();
            if (Model.CanEdit)
                Ui.OnPress(ActionButton.Primary(C, p, "New deal", key: "new"), new CrmMsg.OpenDealDialog(null));
        }
        Rule("header-rule");
        Metrics(deals);
        Rule("metrics-rule");
        using (C.Div(key: "board").FillWidth().Grow().ScrollX().Semantics(Role.ScrollArea, "Deals board")
            .Padding(new Edges(Space.S4, Space.S4, Space.S2, Space.S4)).Gap(Space.S3))
        {
            foreach (var stage in Enum.GetValues<Stage>())
                Lane(stage, [.. deals.Where(d => d.Stage == stage)]);
        }
    }
    void BoardOwnerFilter()
    {
        var open = Model.Popup == Popup.BoardOwner;
        var owner = Model.BoardOwner is { } id ? Crm.Member(id) : null;
        var hit = ActionButton.Ghost(C, p, owner?.Name ?? "All owners", Hugeicons.FilterHorizontal, "Owner filter", owner?.Name ?? "All owners",
            open || owner is not null, key: "owner");
        Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.BoardOwner));
        if (open)
            OwnerMenu(hit.Rect, Model.BoardOwner, id => Ui.Dispatch(new CrmMsg.SetBoardOwner(id)), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "owner-menu");
    }
    void Metrics(ImmutableArray<Deal> deals)
    {
        var culture = C.Culture;
        (string Label, long Value)[] metrics =
        [
            ("Open pipeline", Crm.OpenPipeline(deals)),
            ("Weighted forecast", Crm.Weighted(deals)),
            ("Closing in 30 days", Crm.ClosingSoon(deals, Model.Now)),
            ("Won", Crm.Won(deals))
        ];
        using var strip = C.Div(key: "metrics").Row().FillWidth().ItemsCenter().Gap(Space.S6).Padding(new Edges(14, Space.S5, 14, Space.S5));
        foreach (var (label, value) in metrics)
        {
            using var metric = C.Div(key: label).Column().Gap(2);
            Label(label, TextSize.Small, p.Secondary, key: "label");
            Label(Format.Short(value, culture), TextSize.Metric, p.Text, Weight.Semibold, "value");
        }
        C.Spacer(key: "space");
        var open = Crm.OpenPipeline(deals);
        using var bar = C.Div(key: "share").Row().Width(360).Height(6).Gap(3).Semantics(Role.Image, "Open pipeline by stage");
        foreach (var stage in Enum.GetValues<Stage>().Where(s => s != Stage.Won))
        {
            var share = open == 0 ? 0 : deals.Where(d => d.Stage == stage).Sum(d => d.Value) / (double)open;
            if (share <= 0)
                continue;
            using (C.Div(key: stage.ToString()).Flex((float)share).FillHeight().Rounded(3).Background(p.Of(stage).Dot))
            {
            }
        }
    }

    const float CardHeight = 88, CardGap = 8, Pitch = CardHeight + CardGap;
    void Lane(Stage stage, ImmutableArray<Deal> deals)
    {
        var culture = C.Culture;
        var dot = p.Of(stage).Dot;
        using var lane = C.Div(key: stage.ToString()).Column().Width(Sizes.Lane).FillHeight().Padding(8).Gap(8).Rounded(Corners.Lane)
            .Semantics(Role.Group, stage.ToString());
        var drop = lane.DropTarget("deal");
        var held = C.Dragging is { Kind: "deal" } dragging ? dragging.Value : drop.Dropped?.Item is { Kind: "deal" } dropped ? dropped.Value : null;
        var target = held is not null && drop.IsOver || drop.Dropped is not null;
        lane.Background(target ? Color.Mix(p.Lane, dot, p.Dark ? .1f : .07f) : p.Lane).Border(target ? dot.Alpha(.55f) : p.Lane);
        using (C.Div(key: "head").Row().FillWidth().Height(26).ItemsCenter().Gap(7).Px(6))
        {
            Dot(dot, 7, "dot");
            Label(stage.ToString(), TextSize.Body, p.Text, Weight.Medium, "name");
            Label(Format.Count(deals.Length, culture), TextSize.Caption, p.Faint, key: "count");
            C.Spacer(key: "space");
            Label(Format.Short(deals.Sum(d => d.Value), culture), TextSize.Caption, p.Secondary, key: "total");
        }
        var shown = deals.Where(d => d.Id != held).ToImmutableArray();
        using var cards = C.Div(key: "cards").FillWidth().Grow().Scroll().Semantics(Role.ScrollArea, $"{stage} deals");
        // The slot follows the held card's top edge, so it opens where the card would land.
        var index = target && C.DraggedRect is { } box && cards.PreviousRect is { } top
            ? Math.Clamp((int)MathF.Floor((box.Y - top.Y + Pitch / 2) / Pitch), 0, shown.Length)
            : -1;
        if (drop.Dropped is { Item: { Kind: "deal" } item })
            Ui.Dispatch(new CrmMsg.MoveDealTo(item.Value, stage, Math.Max(0, index)));
        var slots = shown.Length + (index >= 0 && drop.Dropped is null ? 1 : 0);
        using var body = C.Div(key: "body").FillWidth().Height(MathF.Max(1, slots) * Pitch - CardGap, Motion.Quick);
        if (slots == 0)
        {
            using (C.Div(key: "empty").Absolute().FillWidth().Height(64).Center().OutlinedCard(p))
                Label("Drop a deal here", TextSize.Caption, p.Faint, key: "text");
        }
        var style = Model.Preferences.Tilt switch
        {
            CardTilt.Flat => DragStyle.Flat,
            CardTilt.Playful => DragStyle.Playful,
            _ => DragStyle.Tilted
        };
        for (int slot = 0, n = 0; slot < slots; slot++)
        {
            var y = slot * Pitch;
            if (slot == index && drop.Dropped is null)
            {
                using (C.Div(key: "drop-slot").Absolute().Offset(0, y, Motion.Quick).FillWidth().Height(CardHeight).Rounded(Corners.Card)
                    .Background(dot.Alpha(.08f)).Border(dot.Alpha(.5f), 1.5f))
                {
                }
                continue;
            }
            DealCard(shown[n++], y, style);
        }
    }
    void DealCard(Deal deal, float y, DragStyle style)
    {
        if (Crm.Company(Model, deal.CompanyId) is not { } company)
            return;
        using var card = C.Div(key: deal.Id).Absolute().Offset(0, y, Motion.Quick).FillWidth().Height(CardHeight).Rounded(Corners.Card)
            .Semantics(Role.Button, $"{company.Name}, {deal.Name}");
        if (Model.CanEdit)
            card.DragSource("deal", deal.Id, style);
        var hit = card.Control();
        Ui.OnPress(hit, new CrmMsg.OpenDeal(deal.Id));
        Ui.OnContextMenu(hit, at => new CrmMsg.OpenDealMenu(deal.Id, at));
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        CardFace(deal, company, hit.Hovered ? 1 : 0, hit.Focused && C.FocusVisible);
    }
    void CardFace(Deal deal, Company company, float lift, bool focused = false)
    {
        using var face = C.Div(key: "face").SizeFull().DealSurface(p, Desktop.Card, lift);
        // Native phone views have no shadows; the desktop board raises cards, more so while hovered or dragged.
        if (!p.Dark || lift > 0)
            face.Shadow(2 + lift * 14, 1 + lift * 6, p.Shade.Alpha(p.Shade.A * (1 + lift * .9f)));
        if (focused)
            face.AccentOutline(p);
        Parts.DealCard(C, p, Desktop, deal, company, Model.Now);
    }
}
