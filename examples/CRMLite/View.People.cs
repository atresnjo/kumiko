using Kumiko.Icons;
using Kumiko.Runtime;
using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    internal void PeoplePage()
    {
        using (Header(Hugeicons.UserMultiple, "header"))
            Label("People", TextSize.Title, p.Text, Weight.Medium, "title");
        Rule("header-rule");
        using var list = C.Div(key: "people").FillWidth().Grow().Scroll().Semantics(Role.Grid, "People");
        using (C.Div(key: "head").TableHeaderRow())
        {
            (string Label, float Width)[] columns = [("Name", 240), ("Title", 220), ("Company", 240), ("Email", 0)];
            foreach (var (label, width) in columns)
            {
                using var cell = C.Div(key: label).Row().FillHeight().ItemsCenter().Semantics(Role.ColumnHeader, label);
                if (width > 0)
                    cell.Width(width);
                else
                    cell.Flex();
                Label(label, TextSize.Caption, p.Faint, key: "label");
            }
        }
        Rule("head-rule");
        foreach (var person in Model.Contacts.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            if (Crm.Company(Model, person.CompanyId) is not { } company)
                continue;
            using var row = C.Div(key: person.Id).Column().FillWidth().Height(Sizes.Row).Semantics(Role.Row, person.Name);
            var hit = row.Control();
            Feedback(row, hit);
            Ui.OnPress(hit, new CrmMsg.OpenCompany(company.Id));
            using (C.Div(key: "cells").TableBodyRow())
            {
                using (C.Div(key: "name").Width(240).Row().ItemsCenter().Gap(8))
                {
                    Avatar(person, 20, "avatar");
                    Label(person.Name, TextSize.Body, p.Text, Weight.Medium, "text");
                }
                using (C.Div(key: "title").Width(220))
                    Label(person.Title, TextSize.Body, p.Secondary, key: "text");
                using (C.Div(key: "company").Width(240).Row().ItemsCenter().Gap(8))
                {
                    Logo(company, 16, "logo");
                    Label(company.Name, TextSize.Body, p.Text, key: "text");
                }
                using (C.Div(key: "email").Flex().Clip())
                    Label(person.Email, TextSize.Body, p.Secondary, key: "text");
            }
            using (C.Div(key: "line").FillWidth().Height(1).Background(p.Line))
            {
            }
        }
    }
    internal void InboxPage()
    {
        var culture = C.Culture;
        using (Header(Hugeicons.Inbox, "header"))
        {
            Label("Inbox", TextSize.Title, p.Text, Weight.Medium, "title");
            Label($"{Format.Count(Crm.UnreadCount(Model), culture)} unread", TextSize.Caption, p.Faint, key: "unread");
        }
        Rule("header-rule");
        using var list = C.Div(key: "threads").FillWidth().Grow().Scroll().Semantics(Role.List, "Threads");
        foreach (var thread in Model.Inbox.OrderByDescending(t => t.At))
            Thread(thread);
    }
    void Thread(InboxThread thread)
    {
        var company = Crm.Company(Model, thread.CompanyId);
        using var item = C.Div(key: thread.Id).Column().FillWidth().Height(78).Semantics(Role.Button, $"{thread.From}: {thread.Subject}");
        var hit = item.Control();
        Feedback(item, hit);
        Ui.OnPress(hit, new CrmMsg.OpenThread(thread.Id));
        using (C.Div(key: "row").TableBodyRow().Gap(Space.S3))
        {
            using (C.Div(key: "unread").Width(8).Center())
                Dot(thread.Unread ? p.Accent : Color.Transparent, 7, "dot");
            Person(thread.From, 32, "avatar");
            using (C.Div(key: "text").Flex().Column().Gap(3).Clip())
            {
                using (C.Div(key: "top").Row().ItemsCenter().Gap(8))
                {
                    Label(thread.From, TextSize.Body, p.Text, thread.Unread ? Weight.Semibold : Weight.Medium, "from");
                    if (company is not null)
                        Label(company.Name, TextSize.Caption, p.Faint, key: "company");
                }
                Label(thread.Subject, TextSize.Body, p.Text, thread.Unread ? Weight.Medium : Weight.Regular, "subject");
                Label(thread.Preview, TextSize.Small, p.Secondary, key: "preview");
            }
            Label(Format.Relative(thread.At, Model.Now, C.Culture), TextSize.Caption, p.Faint, key: "time");
        }
        using (C.Div(key: "line").FillWidth().Height(1).Background(p.Line))
        {
        }
    }
}
