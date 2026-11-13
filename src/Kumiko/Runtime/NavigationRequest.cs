using System.Text.Json.Serialization;

namespace Kumiko.Runtime;

public sealed record BackRequest(string Stack, long Revision, int From, int To);

/// <summary>A proposed removal bound to its source path. Applications may derive records with intent payloads.</summary>
public record PendingNavigation<TRoute, TEntry> where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
{
    public BackRequest Request
    {
        get;
    }
    public NavigationPath<TRoute, TEntry> Desired
    {
        get;
    }

    [JsonConstructor]
    public PendingNavigation(BackRequest request, NavigationPath<TRoute, TEntry> desired)
    {
        if (request is null || desired is null || request.To != desired.Top.Id || desired.Revision <= request.Revision)
            throw new InvalidOperationException("Invalid pending navigation.");
        Request = request;
        Desired = desired;
    }

    /// <summary>Returns the reconciled path only for this exact, still-current confirmation.</summary>
    public NavigationPath<TRoute, TEntry>? Confirm(NavigationPath<TRoute, TEntry> current, BackRequest request, string stack)
    {
        ArgumentNullException.ThrowIfNull(current);
        return Request == request && current.Matches(request, stack) ? current.Follow(Desired) : null;
    }
}
