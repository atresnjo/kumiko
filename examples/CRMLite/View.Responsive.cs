using Kumiko.UI;
using static CRMLite.Palette;

namespace CRMLite;

public sealed partial class CrmView
{
    void CompanyListRows()
    {
        var rows = Crm.Visible(Model);
        using var list = C.Div(key: "company-list").FillWidth().Flex().Scroll().Semantics(Role.List, "Companies");
        foreach (var company in rows)
        {
            using var row = C.Div(key: company.Id).FillWidth().Padding(Space.S4).Gap(Space.S2).Semantics(Role.Button, company.Name);
            var hit = row.Control();
            Feedback(row, hit);
            Ui.OnPress(hit, new CrmMsg.OpenCompany(company.Id));
            using (C.Div(key: "identity").Row().ItemsCenter().Gap(Space.S2))
            {
                Logo(company, 20, "logo");
                C.Text(company.Name, new(Size: TextSize.Title, Weight: Weight.Medium, Color: p.Text, Wrap: true), key: "name");
            }
            C.Text(company.Industry, new(Size: TextSize.Body, Color: p.Secondary, Wrap: true), key: "industry");
            if (Crm.StageOf(Model, company.Id) is { } stage)
                StageTag(stage, "stage");
        }
        if (rows.Length == 0)
            Label("No companies match these filters.", TextSize.Body, p.Secondary, key: "empty");
    }
}
