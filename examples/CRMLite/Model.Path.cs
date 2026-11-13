using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.Runtime;

namespace CRMLite;

public sealed record CrmEntry(int Id, Screen Route, DetailTab Tab = DetailTab.Activity, string Note = "") : INavigationEntry<Screen, CrmEntry>
{
    public static CrmEntry Create(int id, Screen route) => new(id, route);

    public static bool IsValidPath(ImmutableArray<CrmEntry> entries) => entries.All(e => e.Note is not null && Enum.IsDefined(e.Tab) &&
        (e.Route is not Screen.CompanyDetails company || !string.IsNullOrWhiteSpace(company.Id))) &&
        entries[0].Route is not Screen.CompanyDetails && entries.Skip(1).All(e => e.Route is Screen.CompanyDetails);
}

/// <summary>CRM intent travels with the framework's versioned confirmation.</summary>
public sealed record PendingNavigation : PendingNavigation<Screen, CrmEntry>
{
    public string? DeleteCompanyId
    {
        get; init;
    }
    public CompanyList? List
    {
        get; init;
    }

    [JsonConstructor]
    public PendingNavigation(CrmBackRequest request, CrmNavigation desired, string? deleteCompanyId = null, CompanyList? list = null)
        : base(request, desired)
    {
        DeleteCompanyId = deleteCompanyId;
        List = list;
    }
}

public sealed partial record CrmModel
{
    public CrmNavigation Navigation { get; init; } = CrmNavigation.Start(new Screen.Companies());
    public PendingNavigation? PendingNavigation
    {
        get; init;
    }
    [JsonIgnore] public DetailTab Tab => Navigation.Top.Tab;
    [JsonIgnore] public string Note => Navigation.Top.Note;
    public CrmBackRequest BackTo(int target) => Navigation.RequestTo(target, "main");
}
