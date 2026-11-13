namespace CRMLite;

/// <summary>Immutable render props keep a route's visit identity separate from its destination.</summary>
public sealed record CrmPage(CrmModel Model, CrmEntry Entry);
