using Kumiko.Runtime;

namespace CRMLite;

internal sealed class CompaniesPage(CrmPage model, Screen.Companies route) : View<CrmPage, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model.Model, route), "content", static message => message);
}

internal sealed class CompanyPage(CrmPage model, Screen.CompanyDetails route) : View<CrmPage, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model.Model, route), "content", static message => message);
}

internal sealed class DealsPage(CrmPage model, Screen.Deals route) : View<CrmPage, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model.Model, route), "content", static message => message);
}

internal sealed class PeoplePage(CrmPage model, Screen.People route) : View<CrmPage, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model.Model, route), "content", static message => message);
}

internal sealed class InboxPage(CrmPage model, Screen.Inbox route) : View<CrmPage, CrmMsg>(model)
{
    protected override void Render() => Ui.Child(new CrmView(Model.Model, route), "content", static message => message);
}

public sealed partial class CrmView
{
    void RenderPage(Screen route)
    {
        switch (route)
        {
            case Screen.Companies:
                CompaniesPage();
                break;
            case Screen.CompanyDetails details:
                if (Crm.Company(Model, details.Id) is { } company)
                    CompanyPage(company);
                else
                    C.Text("This company is no longer available.", key: "missing-company");
                break;
            case Screen.Deals:
                DealsPage();
                break;
            case Screen.People:
                PeoplePage();
                break;
            case Screen.Inbox:
                InboxPage();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }
    }
}
