using System.Collections.Immutable;
using System.Globalization;
using Kumiko.Runtime;

namespace CRMLite;

/// <summary>
/// A stand-in for a remote company directory. Each search is a debounced command that answers after a network-like
/// delay, so the New deal picker shows its searching state and only the latest search answers.
/// </summary>
public static class CompanyDirectory
{
    public static readonly TimeSpan Latency = TimeSpan.FromMilliseconds(250);

    /// <summary>The most results one search returns.</summary>
    public const int Limit = 50;

    /// <summary>How long typing must pause before the directory is asked; opening the picker asks at once.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Searches after a typing pause. A newer search cancels this one, whether it is still waiting or already asking
    /// the directory, so a burst of keystrokes sends one request.
    /// </summary>
    public static Cmd<CrmMsg> Search(AuthSession session, string query, int request, int workspaceRequest) =>
        Cmd<CrmMsg>.Debounce<ICrmApi>("search-companies", query.Length == 0 ? TimeSpan.Zero : Pause, async (api, token) =>
        {
            return new CrmMsg.CompaniesFound(request, await api.SearchAsync(session, query, token).ConfigureAwait(false));
        }, error => error is UnauthorizedAccessException ? new CrmMsg.Authentication(new AuthMsg.SessionExpired(workspaceRequest))
            : new CrmMsg.CompanySearchFailed(request, error.Message));

    /// <summary>IDs of the companies whose name, domain or industry contains every word of <paramref name="query"/>.</summary>
    public static ImmutableArray<string> Match(ImmutableArray<Company> companies, string query)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return [.. companies
            .Where(c => words.All(w => compare.IndexOf($"{c.Name} {c.Domain} {c.Industry}", w, options) >= 0))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Take(Limit)
            .Select(c => c.Id)];
    }
}
