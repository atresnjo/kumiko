using System.Collections.Immutable;
using System.Globalization;

namespace CRMLite;

/// <summary>Values derived from the model. Companies take their stage and ARR from their deals.</summary>
public static class Crm
{
    /// <summary>Related companies, ordered deterministically; prefer the same industry.</summary>
    public static IEnumerable<Company> Related(CrmModel model, string companyId) => model.Companies
        .Where(c => c.Id != companyId)
        .OrderByDescending(c => c.Industry == Company(model, companyId)?.Industry)
        .ThenBy(c => c.Name, StringComparer.Ordinal);

    public static Member Member(string id) => Data.Team.FirstOrDefault(m => m.Id == id) ?? Data.Team[0];

    public static Company? Company(CrmModel m, string? id) => m.Companies.FirstOrDefault(c => c.Id == id);

    public static IEnumerable<Deal> DealsOf(CrmModel m, string companyId) => m.Deals.Where(d => d.CompanyId == companyId);

    /// <summary>The stage of the company's most recently added deal, or null before its first deal.</summary>
    public static Stage? StageOf(CrmModel m, string companyId) => DealsOf(m, companyId).Select(d => (Stage?)d.Stage).LastOrDefault();

    public static Deal? LatestDeal(CrmModel m, string companyId) => DealsOf(m, companyId).LastOrDefault();

    public static long Arr(CrmModel m, string companyId) => DealsOf(m, companyId).Sum(d => d.Value);

    public static IEnumerable<Contact> ContactsOf(CrmModel m, string companyId) => m.Contacts.Where(p => p.CompanyId == companyId);

    /// <summary>The company's timeline, newest first.</summary>
    public static IEnumerable<Activity> Timeline(CrmModel m, string companyId) =>
        m.Activities.Where(a => a.CompanyId == companyId).OrderByDescending(a => a.At);

    public static IEnumerable<Company> InList(CrmModel m, CompanyList list) => list switch
    {
        CompanyList.Mine => m.Companies.Where(c => c.OwnerId == m.CurrentUser.Id),
        CompanyList.Hot => m.Companies.Where(c => c.Hot),
        _ => m.Companies
    };

    /// <summary>Sectors in the order the industry filter lists them; any other industry is under "Other".</summary>
    public static readonly ImmutableArray<(string Sector, ImmutableArray<string> Industries)> Sectors =
    [
        ("Technology", ["Analytics", "Cloud hosting", "Collaboration", "Data infrastructure", "Developer tools", "Security"]),
        ("Financial services", ["Banking", "Fintech", "Insurance"]),
        ("Consumer", ["Food & beverage", "Gaming", "Media", "Retail", "Travel"]),
        ("Industrial", ["Energy", "Logistics", "Manufacturing"]),
        ("Health", ["Biotech", "Healthcare"]),
        ("Services", ["Design agency", "Education", "Legal services"])
    ];

    public static string SectorOf(string industry) => Sectors.FirstOrDefault(s => s.Industries.Contains(industry)).Sector ?? "Other";

    /// <summary>The sector's position in <see cref="Sectors"/>; "Other" comes last.</summary>
    public static int SectorRank(string sector)
    {
        for (var rank = 0; rank < Sectors.Length; rank++)
        {
            if (Sectors[rank].Sector == sector)
                return rank;
        }
        return Sectors.Length;
    }

    /// <summary>The Companies table: list tab, search, stage, owner and industry filters, then the chosen sort.</summary>
    public static ImmutableArray<Company> Visible(CrmModel m)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var words = m.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rows = InList(m, m.List)
            .Where(c => words.All(w => compare.IndexOf($"{c.Name} {c.Domain} {c.Industry} {c.NextStep}", w, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0))
            .Where(c => m.StageFilter is null || StageOf(m, c.Id) == m.StageFilter)
            .Where(c => m.OwnerFilter is null || c.OwnerId == m.OwnerFilter)
            .Where(c => m.Industries.IsEmpty || m.Industries.Contains(c.Industry));
        var ordered = m.Sort switch
        {
            SortKey.Company => Order(rows, c => c.Name, m.Descending),
            SortKey.Stage => Order(rows, c => StageOf(m, c.Id) is { } s ? (int)s : -1, m.Descending),
            SortKey.Arr => Order(rows, c => Arr(m, c.Id), m.Descending),
            SortKey.Employees => Order(rows, c => c.Employees, m.Descending),
            SortKey.Owner => Order(rows, c => Member(c.OwnerId).Name, m.Descending),
            _ => Order(rows, c => c.LastInteraction, m.Descending)
        };
        return [.. ordered.ThenBy(c => c.Name, StringComparer.Ordinal)];
    }

    static IOrderedEnumerable<Company> Order<T>(IEnumerable<Company> rows, Func<Company, T> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    /// <summary>Numbers and dates sort largest first on their first click; text sorts A to Z.</summary>
    public static bool DescendingFirst(SortKey key) => key is SortKey.Arr or SortKey.Employees or SortKey.LastInteraction;

    /// <summary>Win probability used for the weighted forecast.</summary>
    public static double Probability(Stage stage) => stage switch
    {
        Stage.Lead => .1,
        Stage.Qualified => .25,
        Stage.Demo => .4,
        Stage.Proposal => .6,
        Stage.Negotiation => .8,
        _ => 1
    };

    public static IEnumerable<Deal> BoardDeals(CrmModel m) => m.Deals.Where(d => m.BoardOwner is null || d.OwnerId == m.BoardOwner);

    public static long OpenPipeline(IEnumerable<Deal> deals) => deals.Where(d => d.Stage != Stage.Won).Sum(d => d.Value);

    public static long Weighted(IEnumerable<Deal> deals) =>
        (long)Math.Round(deals.Where(d => d.Stage != Stage.Won).Sum(d => d.Value * Probability(d.Stage)));

    public static long ClosingSoon(IEnumerable<Deal> deals, DateTime now, int days = 30)
    {
        var today = DateOnly.FromDateTime(now);
        return deals.Where(d => d.Stage != Stage.Won && d.Close >= today && d.Close.DayNumber - today.DayNumber <= days).Sum(d => d.Value);
    }

    public static long Won(IEnumerable<Deal> deals) => deals.Where(d => d.Stage == Stage.Won).Sum(d => d.Value);

    public static int UnreadCount(CrmModel m) => m.Inbox.Count(t => t.Unread);

    /// <summary>Form errors for the New deal dialog; all null when it can be created.</summary>
    public static (string? Company, string? Name, string? Value) DealErrors(CrmModel m, DealDraft d) => (
        Company(m, d.CompanyId) is null ? "Choose a company." : null,
        string.IsNullOrWhiteSpace(d.Name) ? "Name the deal." : null,
        Format.ParseMoney(d.Value) is null ? "Enter an amount such as $120k." : null);

    public static (string? Name, string? Domain) CompanyErrors(CompanyDraft d) => (
        string.IsNullOrWhiteSpace(d.Name) ? "Enter the company name." : null,
        d.Domain.Trim() is var domain && (domain.Length < 3 || !domain.Contains('.', StringComparison.Ordinal) || domain.Contains(' ', StringComparison.Ordinal))
            ? "Enter a domain such as acme.com." : null);
}
