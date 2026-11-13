namespace Kumiko.UI;

/// <summary>An exact semantic query. Scope is a stable ID; names are never resolved by position.</summary>
public sealed record SemanticQuery(Role? Role = null, string? Name = null, string? Key = null, ulong? Within = null, bool IncludeOffscreen = false);

public sealed partial class Context
{
    public IReadOnlyList<AccessibleNode> Query(SemanticQuery query)
    {
        CheckThread();
        return semanticTree.Where(n => !n.Hidden && (query.IncludeOffscreen || !n.Offscreen) &&
            (query.Role is null || n.Role == query.Role) && (query.Name is null || n.Name == query.Name) &&
            (query.Key is null || n.Key == query.Key) && (query.Within is null || Within(n.Id, query.Within.Value))).ToArray();
    }
}
