using System.Collections.Immutable;

namespace CRMLite;

public static partial class Data
{
    /// <summary>Hand-written timelines for the favorite accounts plus a generated history for the rest. Built on each call.</summary>
    public static ImmutableArray<Activity> Activities => [.. Written(), .. Generated()];

    static IEnumerable<Activity> Written() =>
    [
        new("a-nw-1", "northwind", ActivityKind.Email, At(10, 20, 13, 20), "Hana Kobayashi", "Re: SSO rollout for the platform team",
            Body: "Marco approved SAML and SCIM provisioning. The open question is audit log retention for EU tenants. Can you confirm logs stay in the Frankfurt region?"),
        new("a-nw-2", "northwind", ActivityKind.Note, At(10, 20, 10, 5), "Maya Brooks",
            Body: "Ruth signs off on budget, not IT. Their billing vendor renews in six weeks, so we need a signature before then."),
        new("a-nw-3", "northwind", ActivityKind.Meeting, At(10, 19, 16), "Maya Brooks", "Security deep dive", "45 min with Hana Kobayashi and Marco Silva",
            "Walked through SOC 2, the audit log export and how usage events are signed. Marco wants the latest pen test summary."),
        new("a-nw-4", "northwind", ActivityKind.StageChange, At(10, 15, 17, 5), "Maya Brooks", From: Stage.Demo, To: Stage.Proposal),
        new("a-nw-5", "northwind", ActivityKind.Meeting, At(10, 15, 15, 30), "Maya Brooks", "Proposal review", "60 min with Ruth Adeyemi and Hana Kobayashi",
            "Agreed on the enterprise plan and the usage add-on. Ruth asked for a two year option with a capped uplift."),
        new("a-nw-6", "northwind", ActivityKind.Call, At(10, 8, 18), "Maya Brooks", "Call with Hana Kobayashi", "22 min",
            "Confirmed nine price plans in scope. Their payment processor stays as it is."),
        new("a-nw-7", "northwind", ActivityKind.StageChange, At(9, 24, 12), "Maya Brooks", From: Stage.Qualified, To: Stage.Demo),
        new("a-nw-8", "northwind", ActivityKind.Created, At(6, 24, 9, 12), "Maya Brooks", "from an email with Hana Kobayashi"),
        new("a-ke-1", "kestrel", ActivityKind.Email, At(10, 20, 7, 30), "Jonas Vermeer", "Order form questions",
            Body: "Can the volume tiers apply per region, or only across the whole contract? Ilse needs this for the forecast."),
        new("a-ke-2", "kestrel", ActivityKind.Meeting, At(10, 16, 14), "Maya Brooks", "Commercial review", "50 min with Jonas Vermeer and Pieter Jansen",
            "Walked through lane level rating. Pieter wants a sandbox with last quarter's shipments."),
        new("a-ke-3", "kestrel", ActivityKind.StageChange, At(10, 14, 11), "Maya Brooks", From: Stage.Proposal, To: Stage.Negotiation),
        new("a-ke-4", "kestrel", ActivityKind.Created, At(5, 11, 9, 40), "Maya Brooks", "from a referral by Ilse de Groot"),
        new("a-lu-1", "lumen", ActivityKind.Call, At(10, 19, 16), "Daniel Okafor", "Call with Grace Whitfield", "30 min",
            "Counsel is reviewing the data processing terms. Expect redlines by Friday."),
        new("a-lu-2", "lumen", ActivityKind.StageChange, At(10, 9, 15), "Daniel Okafor", From: Stage.Proposal, To: Stage.Negotiation),
        new("a-lu-3", "lumen", ActivityKind.Created, At(7, 2, 11, 15), "Daniel Okafor", "from the website demo form")
    ];

    static readonly ImmutableHashSet<string> HandWritten = ["northwind", "kestrel", "lumen"];

    /// <summary>A creation entry, a stage move and the latest interaction for every other company.</summary>
    static IEnumerable<Activity> Generated()
    {
        foreach (var (company, index) in Companies.Select((c, i) => (c, i)).Where(p => !HandWritten.Contains(p.c.Id)))
        {
            var deal = Deals.First(d => d.CompanyId == company.Id);
            var contact = Contacts.First(p => p.CompanyId == company.Id);
            var owner = Team.First(m => m.Id == company.OwnerId).Name;
            var at = company.LastInteraction;
            yield return (index % 3) switch
            {
                0 => new($"a-{company.Id}-last", company.Id, ActivityKind.Email, at, contact.Name, $"Re: {deal.Name}",
                    Body: $"Thanks for the follow up. Next step on our side: {company.NextStep.ToLowerInvariant()}."),
                1 => new($"a-{company.Id}-last", company.Id, ActivityKind.Call, at, owner, $"Call with {contact.Name}", "18 min",
                    $"Agreed next step: {company.NextStep.ToLowerInvariant()}."),
                _ => new($"a-{company.Id}-last", company.Id, ActivityKind.Meeting, at, owner, $"{deal.Name} review", $"30 min with {contact.Name}",
                    $"Walked through {deal.Name.ToLowerInvariant()}. Next: {company.NextStep.ToLowerInvariant()}.")
            };
            if (deal.Stage > Stage.Lead)
                yield return new($"a-{company.Id}-stage", company.Id, ActivityKind.StageChange, at.AddDays(-5).Date.AddHours(12), owner,
                    From: deal.Stage - 1, To: deal.Stage);
            yield return new($"a-{company.Id}-created", company.Id, ActivityKind.Created, company.Created.ToDateTime(new(9, 30)), owner,
                $"from an email with {contact.Name}");
        }
    }
}
