namespace CRMLite;

/// <summary>Pipeline stages in board order. Won deals are closed; every other stage is open.</summary>
public enum Stage
{
    Lead,
    Qualified,
    Demo,
    Proposal,
    Negotiation,
    Won
}

public enum ActivityKind
{
    Email,
    Note,
    Meeting,
    Call,
    StageChange,
    Created
}

/// <summary>A member of the sales team who can own companies and deals.</summary>
public sealed record Member(string Id, string Name, string Email, uint Color);

/// <summary>An account. Its stage and ARR are derived from its deals.</summary>
public sealed record Company(
    string Id,
    string Name,
    string Domain,
    string Industry,
    string Location,
    string Description,
    uint Logo,
    string OwnerId,
    int Employees,
    DateTime LastInteraction,
    string NextStep,
    bool Hot,
    DateOnly Created
);

public sealed record Deal(string Id, string CompanyId, string Name, long Value, Stage Stage, DateOnly Close, string OwnerId);

/// <summary>A person at a customer company.</summary>
public sealed record Contact(string Id, string CompanyId, string Name, string Title, string Email);

/// <summary>
/// One timeline entry. <paramref name="Detail"/> is the secondary line under the title; stage changes carry
/// <paramref name="From"/> and <paramref name="To"/>.
/// </summary>
public sealed record Activity(
    string Id,
    string CompanyId,
    ActivityKind Kind,
    DateTime At,
    string Actor,
    string Title = "",
    string? Detail = null,
    string? Body = null,
    Stage? From = null,
    Stage? To = null
);

public sealed record InboxThread(string Id, string CompanyId, string From, string Subject, string Preview, DateTime At, bool Unread);
