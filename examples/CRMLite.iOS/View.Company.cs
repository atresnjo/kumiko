using CRMLite;
using Kumiko.Icons;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Harbor;
using static CRMLite.Palette;
using static CRMLite.Parts;

/// <summary>A company visit: identity, the details card, desktop section tabs and the selected section.</summary>
internal sealed partial class CompanyPage(CrmPage page, Screen.CompanyDetails route) : View<CrmPage, CrmMsg>(page)
{
    readonly Palette p = new(page.Model.Dark, page.Model.Preferences.Accent);

    protected override void Render()
    {
        var m = Model.Model;
        if (Crm.Company(m, route.Id) is not { } company)
        {
            using (C.Div(key: "missing").SizeFull().Center().Background(p.Page))
                Label(C, Phone, "This company is no longer available.", Scale.Body, p.Secondary, key: "text");
            return;
        }
        using var scroll = C.Div(key: "details-scroll").SizeFull().Column().Scroll().Background(p.Page)
            .Semantics(Role.ScrollArea, company.Name);
        using var content = C.Div(key: "content").FillWidth().Column().Gap(Space.S4).Padding(new Edges(Space.S4, Gutter, Space.S7, Gutter));
        Identity(company);
        Details(company);
        Sections(company);
        switch (Model.Entry.Tab)
        {
            case DetailTab.Deals:
                Deals(company);
                break;
            case DetailTab.People:
                People(company);
                break;
            case DetailTab.Notes:
                Timeline(C, p, Phone, [.. Crm.Timeline(m, company.Id).Where(a => a.Kind == ActivityKind.Note)], m.Now, "notes");
                break;
            default:
                Composer();
                Timeline(C, p, Phone, [.. Crm.Timeline(m, company.Id)], m.Now, "timeline");
                break;
        }
    }

    void Identity(Company company)
    {
        using (C.Div(key: "identity").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            Logo(C, Phone, company, 52, "logo");
            using var names = C.Div(key: "names").Flex().MinWidth(0).Column().Gap(4);
            Label(C, Phone, company.Name, Scale.Name, p.Text, Weight.Semibold, "name", wrap: true);
            Label(C, Phone, $"{company.Domain} · {company.Industry} · {company.Location}", Scale.Small, p.Secondary, key: "meta", wrap: true);
        }
        Label(C, Phone, company.Description, Scale.Body, p.Secondary, key: "description", wrap: true, leading: 1.35f);
    }

    void Details(Company company)
    {
        var m = Model.Model;
        var culture = C.Culture;
        var deal = Crm.LatestDeal(m, company.Id);
        using var card = C.Div(key: "details").FillWidth().Column().Py(4).Rounded(Corners.Dialog).Background(p.Page).Border(p.Border);
        Property(C, p, "Stage", "stage", () =>
        {
            if (Crm.StageOf(m, company.Id) is { } stage)
                StageTag(C, p, Phone, stage, "tag");
            else
                Label(C, Phone, "—", Scale.Body, p.Faint, key: "none");
        });
        Divider("stage-rule");
        Property(C, p, "Deal value", "value", () => Label(C, Phone, deal is null ? "—" : Format.Money(deal.Value, culture), Scale.Body, p.Text, key: "text"));
        Divider("value-rule");
        Property(C, p, "Owner", "owner", () =>
        {
            var owner = Crm.Member(company.OwnerId);
            Avatar(C, p, Phone, owner, 26, "avatar");
            Label(C, Phone, owner.Name, Scale.Body, p.Text, key: "name");
        });
        Divider("owner-rule");
        Property(C, p, "Next step", "next", () => Label(C, Phone, company.NextStep, Scale.Body, p.Text, key: "text", wrap: true));
        Divider("next-rule");
        Property(C, p, "Employees", "employees", () => Label(C, Phone, Format.Count(company.Employees, culture), Scale.Body, p.Text, key: "text"));
        Divider("employees-rule");
        Property(C, p, "Hot account", "hot", () =>
        {
            var id = company.Id;
            Ui.NativeSwitch("Hot account", company.Hot, on => new CrmMsg.SetHot(id, on), p.Accent, disabled: !m.CanEdit, key: "switch");
        });
    }

    void Divider(NodeKey key) => Rule(C, p.Line, key, inset: 14);

    void Sections(Company company)
    {
        var m = Model.Model;
        var culture = C.Culture;
        var entry = Model.Entry.Id;
        (DetailTab Tab, string Label, int Count)[] tabs =
        [
            (DetailTab.Activity, "Activity", Crm.Timeline(m, company.Id).Count()),
            (DetailTab.Deals, "Deals", Crm.DealsOf(m, company.Id).Count()),
            (DetailTab.People, "People", Crm.ContactsOf(m, company.Id).Count()),
            (DetailTab.Notes, "Notes", Crm.Timeline(m, company.Id).Count(a => a.Kind == ActivityKind.Note))
        ];
        using var block = C.Div(key: "sections").FillWidth().Column();
        using (C.Div(key: "tabs").Row().FillWidth().Gap(18).Semantics(Role.Group, "Company sections"))
        {
            foreach (var (tab, label, count) in tabs)
                Tab(Ui, p, label, Format.Count(count, culture), Model.Entry.Tab == tab, new CrmMsg.SetTab(entry, tab), key: label);
        }
        Rule(C, p.Border, "tabs-rule");
    }

    void Composer()
    {
        var m = Model.Model;
        if (!m.CanEdit)
            return;
        var entry = Model.Entry.Id;
        using var row = C.Div(key: "composer").Row().FillWidth().Items(Align.Start).Gap(Space.S2);
        using (C.Div(key: "who").Height(44).Center())
            Avatar(C, p, Phone, m.CurrentUser, 28, "avatar");
        using var column = C.Div(key: "column").Flex().MinWidth(0).Column().Gap(Space.S2);
        using (C.Div(key: "field").FillWidth().Px(12).Rounded(Corners.Dialog - 2).Background(p.Page).Border(p.Kit.Input))
            Ui.NativeTextEditor("Note draft", Model.Entry.Note, text => new CrmMsg.SetNote(entry, text), Input(p), "Add a note",
                Model.Entry.Note.Length > 0 ? 96 : 40, key: "note");
        if (Model.Entry.Note.Length > 0)
        {
            using (C.Div(key: "actions").Row().FillWidth().Justify(Justify.End))
                Primary(Ui, p, "Add note", new CrmMsg.AddNote(entry), Hugeicons.Add01, key: "add-note");
        }
    }

    void Deals(Company company)
    {
        var m = Model.Model;
        using var list = C.Div(key: "deals").FillWidth().Column().Gap(Space.S2);
        foreach (var deal in Crm.DealsOf(m, company.Id))
            Ui.Child(new DealCard(new(m, deal.Id)), deal.Id, static message => message);
    }

    void People(Company company)
    {
        using var list = C.Div(key: "people").FillWidth().Column().Rounded(Corners.Dialog).Border(p.Border);
        var first = true;
        foreach (var person in Crm.ContactsOf(Model.Model, company.Id))
        {
            using var row = C.Div(key: person.Id).FillWidth().Column();
            if (!first)
                Rule(C, p.Line, "rule", inset: 14);
            first = false;
            using var face = C.Div(key: "face").Row().FillWidth().ItemsCenter().Gap(12).Padding(new Edges(10, 14, 10, 14));
            Person(C, p, Phone, person.Name, 36, "avatar");
            using var text = C.Div(key: "text").Flex().MinWidth(0).Column().Gap(2);
            Label(C, Phone, person.Name, Scale.Row, p.Text, Weight.Medium, "name");
            Label(C, Phone, person.Title, Scale.Small, p.Secondary, key: "title");
            Label(C, Phone, person.Email, Scale.Small, p.Faint, key: "email");
        }
    }
}
