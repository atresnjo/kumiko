using CRMLite;
using Kumiko.Icons;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Harbor;
using static CRMLite.Parts;

/// <summary>
/// The Companies root. Compact hosts show the directory here; regular hosts show it in a pane beside the stack,
/// so this page only asks for a choice.
/// </summary>
internal sealed class CompaniesPage : View<CrmPage, CrmMsg>
{
    internal CompaniesPage(CrmPage page, Screen.Companies route) : base(page)
    {
    }

    protected override void Render()
    {
        var m = Model.Model;
        if (C.HorizontalSizeClass != SizeClass.Regular)
        {
            Ui.Child(new CompanyDirectory(new(m, 0)), "directory", static message => message);
            return;
        }
        var p = new Palette(m.Dark, m.Preferences.Accent);
        using var prompt = C.Div(key: "selection-prompt").SizeFull().Column().Center().Gap(Space.S2).Background(p.Page);
        C.Icon(Hugeicons.Building03, 28, p.Faint, key: "icon");
        Label(C, Phone, "Select a company", Scale.Title, p.Text, Weight.Semibold, "title");
        Label(C, Phone, "Its activity, deals and people open here.", Scale.Body, p.Secondary, key: "hint");
    }
}

/// <summary>The model and the height of a bar floating over the list's bottom edge (0 inside the stack, which insets itself).</summary>
internal sealed record DirectoryProps(CrmModel Model, float BottomInset);

/// <summary>The companies list: desktop list tabs, search and filters over touch rows that open a company.</summary>
internal sealed class CompanyDirectory(DirectoryProps props) : View<DirectoryProps, CrmMsg>(props)
{
    protected override void Render()
    {
        var m = Model.Model;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        var culture = C.Culture;
        var rows = Crm.Visible(m);
        using var screen = C.Div(key: "companies").SizeFull().Column().Background(p.Page);
        using (C.Div(key: "toolbar").FillWidth().Column().Gap(10).Padding(new Edges(2, Gutter, 12, Gutter)))
        {
            using (C.Div(key: "lists").Row().FillWidth().Gap(18).Semantics(Role.Group, "Company lists"))
            {
                (CompanyList List, string Label, string? Icon)[] tabs =
                    [(CompanyList.All, "All companies", null), (CompanyList.Mine, "My companies", null), (CompanyList.Hot, "Hot", Hugeicons.Fire)];
                foreach (var (kind, label, icon) in tabs)
                    Tab(Ui, p, label, Format.Count(Crm.InList(m, kind).Count(), culture), m.List == kind, new CrmMsg.ShowList(kind), icon, label);
            }
            using (C.Div(key: "tools").Row().FillWidth().ItemsCenter().Gap(8))
            {
                SearchField(Ui, p, "Search companies", m.Search, static text => new CrmMsg.SetSearch(text), "search");
                Chip(Ui, p, m.StageFilter?.ToString() ?? "Stage", Hugeicons.FilterHorizontal, m.StageFilter is not null,
                    new CrmMsg.SetPopup(Popup.StageFilter), "stage");
                var owner = m.OwnerFilter is { } id ? Crm.Member(id).Name.Split(' ')[0] : "Owner";
                Chip(Ui, p, owner, null, m.OwnerFilter is not null, new CrmMsg.SetPopup(Popup.OwnerFilter), "owner");
            }
        }
        Rule(C, p.Border, "toolbar-rule");
        using var list = C.Div(key: "directory-list").FillWidth().Flex().MinHeight(0).Column().Scroll().Semantics(Role.List, "Companies");
        foreach (var company in rows)
            Row(p, company);
        if (rows.Length == 0)
        {
            using (C.Div(key: "empty").FillWidth().Padding(32).Center())
                Label(C, Phone, "No companies match these filters.", Scale.Body, p.Secondary, key: "text");
            return;
        }
        using (C.Div(key: "summary").Row().FillWidth().Padding(new Edges(14, Gutter, 24 + Model.BottomInset, Gutter)).Gap(6))
        {
            Label(C, Phone, $"{Format.Count(rows.Length, culture)} companies", Scale.Small, p.Faint, key: "count");
            C.Spacer(key: "space");
            Label(C, Phone, "Sum", Scale.Small, p.Faint, key: "sum-label");
            Label(C, Phone, Format.Short(rows.Sum(company => Crm.Arr(m, company.Id)), culture), Scale.Small, p.Text, Weight.Semibold, "sum");
        }
    }

    void Row(Palette p, Company company)
    {
        var m = Model.Model;
        var culture = C.Culture;
        var owner = Crm.Member(company.OwnerId);
        using var press = Ui.NativePressable(company.Name, new CrmMsg.OpenCompany(company.Id), key: company.Id);
        press.FillWidth();
        using var row = C.Div(key: "row").FillWidth().Column();
        using (C.Div(key: "content").Row().FillWidth().ItemsCenter().Gap(12).Padding(new Edges(12, Gutter, 12, Gutter)))
        {
            Logo(C, Phone, company, 36, "logo");
            using (C.Div(key: "text").Flex().MinWidth(0).Column().Gap(4))
            {
                using (C.Div(key: "title").Row().FillWidth().ItemsCenter().Gap(5))
                {
                    using (C.Div(key: "name").Shrink(1).MinWidth(0))
                        Label(C, Phone, company.Name, Scale.Row, p.Text, Weight.Medium, "text");
                    if (company.Hot)
                        C.Icon(Hugeicons.Fire, 14, p.Flame, key: "hot");
                }
                using (C.Div(key: "meta").Row().FillWidth().ItemsCenter().Gap(6))
                {
                    using (C.Div(key: "detail").Shrink(1).MinWidth(0))
                        Label(C, Phone, $"{owner.Name.Split(' ')[0]} · {company.Industry}", Scale.Small, p.Secondary, key: "text");
                }
            }
            using (C.Div(key: "side").Column().Items(Align.End).Gap(6))
            {
                Label(C, Phone, Format.Money(Crm.Arr(m, company.Id), culture), Scale.Body, p.Text, Weight.Medium, "arr");
                if (Crm.StageOf(m, company.Id) is { } stage)
                    StageTag(C, p, Phone, stage, "stage");
            }
        }
        Rule(C, p.Line, "rule", inset: Gutter + 48);
    }
}
