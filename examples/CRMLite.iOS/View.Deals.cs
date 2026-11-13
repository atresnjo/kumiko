using System.Collections.Immutable;
using CRMLite;
using Kumiko.Icons;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Harbor;
using static CRMLite.Palette;
using static CRMLite.Parts;

/// <summary>Props for one deal: the model it reads and the deal's ID.</summary>
internal sealed record DealProps(CrmModel Model, string DealId);

/// <summary>
/// The deals board for a phone: the desktop metrics and pipeline share, then each stage lane as a section of the same
/// cards. A card opens the deal sheet.
/// </summary>
internal sealed class DealsPage : View<CrmPage, CrmMsg>
{
    internal DealsPage(CrmPage page, Screen.Deals route) : base(page)
    {
    }

    protected override void Render()
    {
        var m = Model.Model;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        var culture = C.Culture;
        var deals = Crm.BoardDeals(m).ToImmutableArray();
        using var scroll = C.Div(key: "board").SizeFull().Column().Scroll().Background(p.Page).Semantics(Role.ScrollArea, "Deals board");
        using (C.Div(key: "summary").FillWidth().Column().Gap(Space.S4).Padding(new Edges(Space.S2, Gutter, Space.S4, Gutter)))
        {
            using (C.Div(key: "filter").Row().FillWidth().ItemsCenter())
            {
                var owner = m.BoardOwner is { } id ? Crm.Member(id).Name : "All owners";
                Chip(Ui, p, owner, Hugeicons.FilterHorizontal, m.BoardOwner is not null, new CrmMsg.SetPopup(Popup.BoardOwner), "owner");
            }
            (string Label, long Value)[] metrics =
            [
                ("Open pipeline", Crm.OpenPipeline(deals)),
                ("Weighted forecast", Crm.Weighted(deals)),
                ("Closing in 30 days", Crm.ClosingSoon(deals, m.Now)),
                ("Won", Crm.Won(deals))
            ];
            using (C.Div(key: "metrics").Row().FillWidth().Wrap().RowGap(Space.S4))
            {
                foreach (var (label, value) in metrics)
                {
                    using var metric = C.Div(key: label).Width(Length.Percent(50)).Column().Gap(2);
                    Label(C, Phone, label, Scale.Small, p.Secondary, key: "label");
                    Label(C, Phone, Format.Short(value, culture), Scale.Metric, p.Text, Weight.Semibold, "value");
                }
            }
            Share(p, deals);
        }
        Rule(C, p.Border, "summary-rule");
        using var lanes = C.Div(key: "lanes").FillWidth().Column().Gap(Space.S5).Padding(new Edges(Space.S4, Gutter, Space.S7, Gutter));
        foreach (var stage in Enum.GetValues<Stage>())
        {
            ImmutableArray<Deal> lane = [.. deals.Where(d => d.Stage == stage)];
            using var section = C.Div(key: stage.ToString()).FillWidth().Column().Gap(Space.S2).Semantics(Role.Group, stage.ToString());
            using (C.Div(key: "head").Row().FillWidth().Height(28).ItemsCenter().Gap(8))
            {
                Dot(C, p.Of(stage).Dot, 8, "dot");
                Label(C, Phone, stage.ToString(), Scale.Body, p.Text, Weight.Semibold, "name");
                Label(C, Phone, Format.Count(lane.Length, culture), Scale.Small, p.Faint, key: "count");
                C.Spacer(key: "space");
                Label(C, Phone, Format.Short(lane.Sum(d => d.Value), culture), Scale.Small, p.Secondary, key: "total");
            }
            if (lane.IsEmpty)
            {
                using (C.Div(key: "empty").FillWidth().Height(56).Center().Rounded(Corners.Card + 2).Background(p.Lane).Border(p.Border))
                    Label(C, Phone, "No deals in this stage", Scale.Small, p.Faint, key: "text");
            }
            foreach (var deal in lane)
                Ui.Child(new DealCard(new(m, deal.Id)), deal.Id, static message => message);
        }
    }

    void Share(Palette p, ImmutableArray<Deal> deals)
    {
        var open = Crm.OpenPipeline(deals);
        using var bar = C.Div(key: "share").Row().FillWidth().Height(6).Gap(3);
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
}

/// <summary>The shared deal card at phone size, pressable to open the deal sheet.</summary>
internal sealed class DealCard(DealProps props) : View<DealProps, CrmMsg>(props)
{
    protected override void Render()
    {
        var m = Model.Model;
        if (m.Deals.FirstOrDefault(d => d.Id == Model.DealId) is not { } deal || Crm.Company(m, deal.CompanyId) is not { } company)
            return;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        using var press = Ui.NativePressable($"{company.Name}, {deal.Name}", new CrmMsg.OpenDeal(deal.Id), key: "card");
        press.FillWidth();
        using var face = C.Div(key: "face").FillWidth().DealSurface(p, Phone.Card);
        Parts.DealCard(C, p, Phone, deal, company, m.Now);
    }
}

/// <summary>The desktop deal drawer as a native sheet: amount, stage choices, fields and a way to the company.</summary>
internal sealed class DealSheet(DealProps props) : View<DealProps, CrmMsg>(props)
{
    protected override void Render()
    {
        var m = Model.Model;
        if (m.Deals.FirstOrDefault(d => d.Id == Model.DealId) is not { } deal || Crm.Company(m, deal.CompanyId) is not { } company)
            return;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        var culture = C.Culture;
        using var sheet = Ui.NativeBottomDrawer(deal.Name, new CrmMsg.CloseDialog(), 560, Sheet(p), key: "deal");
        using var scroll = C.Div(key: "scroll").FillWidth().Flex().MinHeight(0).Column().Scroll().Gap(Space.S4);
        using (C.Div(key: "amount").FillWidth().Column().Gap(4))
        {
            Label(C, Phone, $"{company.Name} · {company.Domain}", Scale.Small, p.Secondary, key: "company");
            Label(C, Phone, Format.Money(deal.Value, culture), 30, p.Text, Weight.Semibold, "value");
            var weighted = (long)Math.Round(deal.Value * Crm.Probability(deal.Stage));
            Label(C, Phone, "Weighted " + Format.Money(weighted, culture), Scale.Small, p.Faint, key: "weighted");
        }
        using (C.Div(key: "stages").Row().FillWidth().Wrap().Gap(8).Semantics(Role.Group, "Stage"))
        {
            var id = deal.Id;
            foreach (var stage in Enum.GetValues<Stage>())
            {
                var (dot, fill, ink) = p.Of(stage);
                var on = deal.Stage == stage;
                using var press = Ui.NativePressable(stage.ToString(), new CrmMsg.SetDealStage(id, stage), m.CanEdit, key: stage.ToString());
                using var tag = C.Div(key: "face").Row().Height(32).ItemsCenter().Gap(6).Px(10).Rounded(16)
                    .Background(on ? fill : p.Page).Border(on ? dot.Alpha(.6f) : p.Border);
                Dot(C, dot, 7, "dot");
                Label(C, Phone, stage.ToString(), Scale.Small, on ? ink : p.Secondary, on ? Weight.Medium : Weight.Regular, "label");
            }
        }
        using (C.Div(key: "fields").FillWidth().Column().Py(4).Rounded(Corners.Dialog).Border(p.Border))
        {
            Property(C, p, "Probability", "probability", () => Label(C, Phone, Crm.Probability(deal.Stage).ToString("P0", culture), Scale.Body, p.Text, key: "text"));
            Rule(C, p.Line, "probability-rule", inset: 14);
            Property(C, p, "Close date", "close", () => Label(C, Phone, Format.Close(deal.Close, m.Now, culture).Text, Scale.Body, p.Text, key: "text"));
            Rule(C, p.Line, "close-rule", inset: 14);
            Property(C, p, "Owner", "owner", () =>
            {
                var owner = Crm.Member(deal.OwnerId);
                Avatar(C, p, Phone, owner, 26, "avatar");
                Label(C, Phone, owner.Name, Scale.Body, p.Text, key: "name");
            });
            Rule(C, p.Line, "owner-rule", inset: 14);
            Property(C, p, "Next step", "next", () => Label(C, Phone, company.NextStep, Scale.Body, p.Text, key: "text", wrap: true));
        }
        Primary(Ui, p, "Open " + company.Name, new CrmMsg.OpenCompany(company.Id), Hugeicons.ArrowUpRight01, fill: true, key: "open");
    }
}
