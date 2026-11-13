using System.Collections.Immutable;
using CRMLite.Components;
using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    sealed record Column(SortKey? Key, string Label, float Width, bool Right = false);

    static readonly ImmutableArray<Column> Columns =
    [
        new(SortKey.Company, "Company", 200),
        new(SortKey.Stage, "Stage", 124),
        new(SortKey.Arr, "ARR", 100, true),
        new(SortKey.Employees, "Employees", 96, true),
        new(SortKey.Owner, "Owner", 176),
        new(SortKey.LastInteraction, "Last interaction", 140),
        new(null, "Next step", 0)
    ];
    internal void CompaniesPage()
    {
        using (Header(Hugeicons.Building03, "header"))
        {
            Label("Companies", TextSize.Title, p.Text, Weight.Medium, "title");
            C.Spacer(key: "space");
            if (Model.CanEdit)
                Ui.OnPress(ActionButton.Primary(C, p, "New company", key: "new"), new CrmMsg.OpenCompanyDialog());
        }
        Rule("header-rule");
        Toolbar();
        Rule("toolbar-rule");
        if (C.WidthBreakpoint < Breakpoint.Expanded)
            CompanyListRows();
        else
            Table();
    }
    void Toolbar()
    {
        var culture = C.Culture;
        var sidebarWidth = ForcedRail || Model.SidebarCollapsed ? Sizes.Rail : Model.SidebarWidth;
        var paneWidth = MathF.Min(Sizes.Workspace, C.Size.Width - sidebarWidth);
        var compact = paneWidth < Sizes.ToolbarInlineMin;
        using var bar = C.Div(key: "toolbar").FillWidth().Gap(Space.S2).Px(Space.S5);
        if (compact)
            bar.Py(Space.S2);
        else
            bar.Row().Height(Sizes.Toolbar);
        using (C.Div(key: "tabs").Row().Height(Sizes.Toolbar).Gap(Space.S4).Semantics(Role.Group, "Company lists"))
        {
            (CompanyList List, string Label, string? Icon)[] tabs = [(CompanyList.All, "All companies", null), (CompanyList.Mine, "My companies", null),
                (CompanyList.Hot, "Hot", Hugeicons.Fire)];
            foreach (var (list, label, icon) in tabs)
            {
                var count = Format.Count(Crm.InList(Model, list).Count(), culture);
                Ui.OnPress(Tab("company-lists", label, count, Model.List == list, icon, key: label), new CrmMsg.ShowList(list));
            }
        }
        using var tools = C.Div(key: "tools").Row().MinHeight(Sizes.Toolbar).ItemsCenter().Gap(Space.S1);
        if (compact)
            tools.FillWidth().Wrap();
        else
            tools.Margin(LayoutEdge.Start, Length.Auto);
        Ui.TextField(key: "search", label: "Search companies", value: Model.Search, onChange: text => new CrmMsg.SetSearch(text),
            options: new("Search companies", compact ? 150 : 200));
        IndustryFilter(compact ? 144 : 176);
        StageFilter();
        OwnerFilter();
    }
    void IndustryFilter(float width)
    {
        var culture = C.Culture;
        ImmutableArray<Choice> industries =
        [
            .. Model.Companies.GroupBy(company => company.Industry)
                .Select(group => (Industry: group.Key, Count: group.Count(), Sector: Crm.SectorOf(group.Key)))
                .OrderBy(x => Crm.SectorRank(x.Sector))
                .ThenBy(x => x.Industry, StringComparer.Ordinal)
                .Select(x => new Choice(x.Industry, x.Industry)
                {
                    Section = x.Sector,
                    Detail = Format.Count(x.Count, culture)
                })
        ];
        Ui.Dropdown("industry", "Industry", (Model.IndustryPicker ?? new()) with
        {
            IsOpen = Model.Popup == Popup.IndustryFilter
        }, industries,
            state => new CrmMsg.FilterIndustries(state),
            new(Width: width, Placeholder: "All industries", EmptyMessage: "No industries match", Multiple: true));
    }
    Interaction Tab(string group, string label, string count, bool active, string? icon = null, NodeKey key = default)
    {
        using var tab = C.Div(key: key).Column().FillHeight().Semantics(Role.Option, label, isChecked: active);
        var hit = tab.Control();
        if (hit.Hovered)
            C.Cursor = Cursor.Hand;
        using (var body = C.Div(key: "body").Row().Grow().ItemsCenter().Gap(5).Px(2).Rounded(Corners.Tag))
        {
            if (hit.Focused && C.FocusVisible)
                body.AccentOutline(p);
            if (icon is not null)
                C.Icon(icon, 12, active ? p.Text : p.Secondary, key: "icon");
            Label(label, TextSize.Body, active || hit.Hovered ? p.Text : p.Secondary, active ? Weight.Medium : Weight.Regular, "label");
            Label(count, TextSize.Caption, p.Faint, key: "count");
        }
        using (var rule = C.Div(key: "rule").FillWidth().Height(2).Rounded(1))
        {
            if (active)
                rule.Background(p.Text).SharedLayout(group);
        }
        return hit;
    }
    void StageFilter()
    {
        var open = Model.Popup == Popup.StageFilter;
        var label = Model.StageFilter is { } chosen ? $"Stage: {chosen}" : "Stage";
        var hit = ActionButton.Ghost(C, p, label, Hugeicons.FilterHorizontal, "Stage filter", Model.StageFilter?.ToString() ?? "All stages",
            open || Model.StageFilter is not null, key: "stage");
        Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.StageFilter));
        if (!open)
            return;
        using var menu = C.Kit.Popover(hit.Rect, () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)),
            new(Align: Kumiko.Components.PopoverAlign.End, Width: 200, Name: "Stage"), key: "stage-menu");
        MenuHeading("Stage", "heading");
        ulong previous = 0;
        Ui.OnPress(Option("All stages", Model.StageFilter is null, ref previous, key: "all"), new CrmMsg.SetStageFilter(null));
        MenuRule("rule");
        foreach (var stage in Enum.GetValues<Stage>())
        {
            var dot = p.Of(stage).Dot;
            Ui.OnPress(Option(stage.ToString(), Model.StageFilter == stage, ref previous, () => Dot(dot, 7, "dot"), key: stage.ToString()), new CrmMsg.SetStageFilter(stage));
        }
    }
    void OwnerFilter()
    {
        var open = Model.Popup == Popup.OwnerFilter;
        var owner = Model.OwnerFilter is { } id ? Crm.Member(id) : null;
        var hit = ActionButton.Ghost(C, p, owner is null ? "Owner" : $"Owner: {owner.Name}", Hugeicons.FilterHorizontal, "Owner filter",
            owner?.Name ?? "All owners", open || owner is not null, key: "owner");
        Ui.OnPress(hit, new CrmMsg.SetPopup(open ? Popup.None : Popup.OwnerFilter));
        if (open)
            OwnerMenu(hit.Rect, Model.OwnerFilter, id => Ui.Dispatch(new CrmMsg.SetOwnerFilter(id)), () => Ui.Dispatch(new CrmMsg.SetPopup(Popup.None)), "owner-menu");
    }
    void OwnerMenu(Rect? anchor, string? selected, Action<string?> chosen, Action close, NodeKey key, bool everyone = true)
    {
        using var menu = C.Kit.Popover(anchor, close, new(Align: Kumiko.Components.PopoverAlign.End, Width: 240, Name: "Owner"), key: key);
        MenuHeading("Owner", "heading");
        ulong previous = 0;
        if (everyone)
        {
            if (Option("All owners", selected is null, ref previous, key: "all").Clicked)
                chosen(null);
            MenuRule("rule");
        }
        foreach (var member in Data.Team)
        {
            var you = member.Id == Model.CurrentUser.Id ? "You" : null;
            if (Option(member.Name, selected == member.Id, ref previous, () => Avatar(member, 20, "avatar"), you, key: member.Id).Clicked)
                chosen(member.Id);
        }
    }
}
