using CRMLite;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Harbor;
using static CRMLite.Parts;

/// <summary>Everyone at every company, alphabetically; a row opens the person's company.</summary>
internal sealed class PeoplePage : View<CrmPage, CrmMsg>
{
    internal PeoplePage(CrmPage page, Screen.People route) : base(page)
    {
    }

    protected override void Render()
    {
        var m = Model.Model;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        using var list = C.Div(key: "people").SizeFull().Column().Scroll().Background(p.Page).Semantics(Role.List, "People");
        foreach (var person in m.Contacts.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            if (Crm.Company(m, person.CompanyId) is not { } company)
                continue;
            using var press = Ui.NativePressable($"{person.Name}, {company.Name}", new CrmMsg.OpenCompany(company.Id), key: person.Id);
            press.FillWidth();
            using var row = C.Div(key: "row").FillWidth().Column();
            using (C.Div(key: "face").Row().FillWidth().ItemsCenter().Gap(12).Padding(new Edges(12, Gutter, 12, Gutter)))
            {
                Person(C, p, Phone, person.Name, 40, "avatar");
                using (C.Div(key: "text").Flex().MinWidth(0).Column().Gap(3))
                {
                    Label(C, Phone, person.Name, Scale.Row, p.Text, Weight.Medium, "name");
                    Label(C, Phone, person.Title, Scale.Small, p.Secondary, key: "title");
                }
                using (C.Div(key: "company").Row().ItemsCenter().Gap(6).MaxWidth(140))
                {
                    Logo(C, Phone, company, 20, "logo");
                    using (C.Div(key: "name").Shrink(1).MinWidth(0))
                        Label(C, Phone, company.Name, Scale.Small, p.Secondary, key: "text");
                }
            }
            Rule(C, p.Line, "rule", inset: Gutter + 52);
        }
    }
}

/// <summary>Email threads, newest first; unread threads are marked and bold. A thread opens its company.</summary>
internal sealed class InboxPage : View<CrmPage, CrmMsg>
{
    internal InboxPage(CrmPage page, Screen.Inbox route) : base(page)
    {
    }

    protected override void Render()
    {
        var m = Model.Model;
        var p = new Palette(m.Dark, m.Preferences.Accent);
        var culture = C.Culture;
        using var list = C.Div(key: "threads").SizeFull().Column().Scroll().Background(p.Page).Semantics(Role.List, "Threads");
        using (C.Div(key: "unread").FillWidth().Padding(new Edges(Space.S2, Gutter, Space.S2, Gutter)))
            Label(C, Phone, $"{Format.Count(Crm.UnreadCount(m), culture)} unread", Scale.Small, p.Faint, key: "text");
        Rule(C, p.Border, "unread-rule");
        foreach (var thread in m.Inbox.OrderByDescending(t => t.At))
        {
            var company = Crm.Company(m, thread.CompanyId);
            using var press = Ui.NativePressable($"{thread.From}: {thread.Subject}", new CrmMsg.OpenThread(thread.Id), key: thread.Id);
            press.FillWidth();
            using var row = C.Div(key: "row").FillWidth().Column();
            using (C.Div(key: "face").Row().FillWidth().Items(Align.Start).Gap(10).Padding(new Edges(12, 12, 12, Gutter)))
            {
                using (C.Div(key: "avatar").Row().ItemsCenter().Gap(8).Height(40))
                    Person(C, p, Phone, thread.From, 40, "image");
                using var text = C.Div(key: "text").Flex().MinWidth(0).Column().Gap(3);
                using (C.Div(key: "top").Row().FillWidth().ItemsCenter().Gap(6))
                {
                    using (C.Div(key: "from").Shrink(1).MinWidth(0))
                        Label(C, Phone, thread.From, Scale.Row, p.Text, thread.Unread ? Weight.Semibold : Weight.Medium, "text");
                    if (company is not null)
                    {
                        using (C.Div(key: "company").Shrink(1).MinWidth(0))
                            Label(C, Phone, company.Name, Scale.Small, p.Faint, key: "text");
                    }
                    C.Spacer(key: "space");
                    Label(C, Phone, Format.Relative(thread.At, m.Now, culture), Scale.Caption, p.Faint, key: "time");
                    if (thread.Unread)
                        Dot(C, p.Accent, 8, "dot");
                }
                Label(C, Phone, thread.Subject, Scale.Body, p.Text, thread.Unread ? Weight.Medium : Weight.Regular, "subject");
                Label(C, Phone, thread.Preview, Scale.Small, p.Secondary, key: "preview");
            }
            Rule(C, p.Line, "rule", inset: Gutter + 50);
        }
    }
}
