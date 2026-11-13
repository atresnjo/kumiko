using System.Collections.Immutable;
using System.Globalization;

namespace CRMLite;

/// <summary>
/// The demo agent. It drafts a plan for a deal and scripts the work shown under each step; everything follows from
/// the deal, its company and contact, so a plan runs the same way every time. Booking the meeting fails on the first
/// attempt, and the agent adds a security step of its own once it has read the customer's questions.
/// </summary>
public static class AgentScript
{
    public const string Model = "Claude Opus 5.5";

    /// <summary>After this step the agent inserts <see cref="Addition"/>.</summary>
    public const string AddsAfter = "summarize";

    /// <summary>Finishing this step moves the deal to its next stage on the board.</summary>
    public const string Advances = "advance";

    const string Books = "book";

    public static PlanStep Addition => new("security", "Answer the security review questionnaire", StepOrigin.Agent);

    public static AgentPlan Draft(CrmModel m, Deal deal)
    {
        var (company, contact, owner) = Parties(m, deal);
        var next = Next(deal.Stage);
        var value = Format.Short(deal.Value, CultureInfo.InvariantCulture);
        return new(deal.Id, $"Move {deal.Name} with {company.Name} to {next}",
        [
            new("review", $"Review the last 30 days with {company.Name}"),
            new(AddsAfter, $"Summarize {contact.Name}'s open questions"),
            new("propose", $"Draft a proposal at {value}"),
            new(Books, $"Book a {Meeting(next)} with {First(contact.Name)} next week"),
            new(Advances, $"Move the deal to {next} and update the forecast"),
            new("recap", $"Send a recap to {owner.Name}")
        ]);
    }

    /// <summary>What Keep planning proposes, in order.</summary>
    public static ImmutableArray<string> Suggestions(CrmModel m, Deal deal) =>
    [
        "Offer a two-year term with a capped uplift",
        $"Share a {Parties(m, deal).Company.Industry.ToLowerInvariant()} reference customer"
    ];

    /// <summary>The lines of work for a step's current attempt, and the reason it fails, if it does.</summary>
    public static (ImmutableArray<WorkItem> Work, string? Failure) Work(CrmModel m, Deal deal, PlanStep step)
    {
        var (company, contact, owner) = Parties(m, deal);
        var slug = Slug(deal.Name);
        var first = First(contact.Name);
        return step.Id switch
        {
            "review" =>
            ([
                new(WorkKind.Read, $"Read notes/{company.Id}.md"),
                new(WorkKind.Read, $"Read 6 emails with {first}"),
                new(WorkKind.Searched, "Searched call notes for pricing")
            ], null),
            AddsAfter =>
            ([
                new(WorkKind.Read, $"Read calls/{company.Id}-oct-14.md"),
                new(WorkKind.Created, $"Created {company.Id}-questions.md")
            ], null),
            "security" =>
            ([
                new(WorkKind.Read, $"Read security/{company.Id}-review.xlsx"),
                new(WorkKind.Edited, "Edited 3 answers in the questionnaire")
            ], null),
            "propose" =>
            ([
                new(WorkKind.Read, "Read pricing/enterprise.md"),
                new(WorkKind.Created, $"Created proposals/{slug}.docx"),
                new(WorkKind.Edited, "Edited 2 files")
            ], null),
            Books when step.Attempt == 0 =>
            ([
                new(WorkKind.Scheduled, $"Checked {first}'s calendar"),
                new(WorkKind.Emailed, $"Drafted an invite to {contact.Email}")
            ], $"No shared free slot next week; {first} is out until Monday"),
            Books =>
            ([
                new(WorkKind.Scheduled, $"Checked {first}'s calendar from Monday"),
                new(WorkKind.Emailed, "Sent an invite for Tuesday, 10:00 AM")
            ], null),
            Advances =>
            ([
                new(WorkKind.Edited, $"Edited deals/{slug}.json"),
                new(WorkKind.Ran, "Ran the forecast check")
            ], null),
            "recap" =>
            ([
                new(WorkKind.Created, "Created recap.md"),
                new(WorkKind.Emailed, $"Emailed {First(owner.Name)}")
            ], null),
            _ when step.Origin == StepOrigin.Suggested =>
            ([
                new(WorkKind.Read, "Read pricing/terms.md"),
                new(WorkKind.Edited, $"Edited proposals/{slug}.docx")
            ], null),
            _ =>
            ([
                new(WorkKind.Read, $"Read notes/{company.Id}.md"),
                new(WorkKind.Edited, "Edited 1 file")
            ], null)
        };
    }

    /// <summary>Agent seconds spent on one beat of a step, between 4 and 10.</summary>
    public static int Pace(PlanStep step) => 4 + (step.Id.Sum(ch => ch) + step.Shown * 5) % 7;

    public static Stage Next(Stage stage) => stage == Stage.Won ? Stage.Won : stage + 1;

    static string Meeting(Stage next) => next switch
    {
        Stage.Qualified => "discovery call",
        Stage.Demo => "demo",
        Stage.Won => "signing call",
        _ => "pricing call"
    };

    static (Company Company, Contact Contact, Member Owner) Parties(CrmModel m, Deal deal)
    {
        var company = Crm.Company(m, deal.CompanyId) ?? m.Companies[0];
        var contact = Crm.ContactsOf(m, company.Id).FirstOrDefault() ?? new("", company.Id, "Buyer", "", "buyer@" + company.Domain);
        return (company, contact, Crm.Member(deal.OwnerId));
    }

    static string First(string name) => name.Split(' ')[0];

    static string Slug(string name) => string.Join('-', name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
