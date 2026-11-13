using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DiffViewer.Core;

namespace DiffViewer.Sources;

/// <summary>
/// Where pull requests come from: the gh executable that knows the accounts and their tokens, and optionally one
/// API address for every host (tests use a local server). Pull request lists are kept for <see cref="CacheFor"/>.
/// </summary>
public sealed record GitHubConfig(string Gh, string? Api = null)
{
    public TimeSpan CacheFor { get; init; } = TimeSpan.FromMinutes(1);

    static readonly string[] Fallbacks = ["/opt/homebrew/bin/gh", "/usr/local/bin/gh", "/usr/bin/gh"];

    /// <summary>gh from PATH or a standard install location, as an app started from Finder has a short PATH; null without gh.</summary>
    public static GitHubConfig? Default()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = directory.Length == 0 ? "" : Path.Combine(directory, OperatingSystem.IsWindows() ? "gh.exe" : "gh");
            if (candidate.Length > 0 && File.Exists(candidate))
                return new(candidate);
        }
        return Fallbacks.FirstOrDefault(File.Exists) is { } fallback ? new(fallback) : null;
    }

    internal Uri ApiFor(string host) => new(Api ?? (host == "github.com" ? "https://api.github.com/" : $"https://{host}/api/v3/"));
}

/// <summary>An account gh is logged in to.</summary>
public sealed record GitHubAccount(GitHubConfig Config, string Host, string Login);

/// <summary>An open pull request that involves one of the accounts, and the account that found it.</summary>
public sealed record PullRequest(GitHubAccount Account, string Repository, int Number, string Title, string Author, bool Draft,
    DateTimeOffset Updated, string Url);

/// <summary>
/// GitHub through its REST API, with tokens from gh. Tokens are read for each request batch, sent only to the
/// account's API host and never kept, logged or shown. Runs on worker threads.
/// </summary>
public static class GitHub
{
    static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    /// <summary>
    /// Open pull requests involving each account (author, assignee, reviewer, mention), newest first and each
    /// listed once. Accounts that fail are named in the error; the others still list theirs.
    /// </summary>
    public static async Task<(ImmutableArray<PullRequest> Pulls, string? Error)> PullRequests(GitHubConfig config, CancellationToken token)
    {
        var accounts = await Accounts(config, token).ConfigureAwait(false);
        if (accounts.IsEmpty)
            return ([], "No gh accounts. Run gh auth login.");
        var results = await Task.WhenAll(accounts.Select(async account =>
        {
            try
            {
                var query = Uri.EscapeDataString($"is:pr is:open archived:false involves:{account.Login}");
                using var json = await Get(account, await Token(account, token).ConfigureAwait(false),
                    $"search/issues?q={query}&sort=updated&order=desc&per_page=100", token).ConfigureAwait(false);
                return (Pulls: json.RootElement.GetProperty("items").EnumerateArray().Select(item => Pull(account, item)).ToArray(), Error: (string?)null);
            }
            catch (Exception error) when (error is IOException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                return (Pulls: Array.Empty<PullRequest>(), Error: $"{account.Login}: {error.Message}");
            }
        })).ConfigureAwait(false);
        var pulls = results.SelectMany(r => r.Pulls).DistinctBy(p => p.Url, StringComparer.Ordinal).OrderByDescending(p => p.Updated);
        var errors = results.Select(r => r.Error).OfType<string>().ToArray();
        return ([.. pulls], errors.Length == 0 ? null : string.Join("; ", errors));
    }

    static PullRequest Pull(GitHubAccount account, JsonElement item)
    {
        var repository = item.GetProperty("repository_url").GetString()!;
        var parts = repository.Split('/');
        return new(account, parts[^2] + "/" + parts[^1], item.GetProperty("number").GetInt32(), item.GetProperty("title").GetString() ?? "",
            item.GetProperty("user").GetProperty("login").GetString() ?? "", item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            item.GetProperty("updated_at").GetDateTimeOffset(), item.GetProperty("html_url").GetString() ?? "");
    }

    /// <summary>
    /// The pull request's files from the merge base of its base and head to its head, as GitHub's "Files changed"
    /// shows them. Both versions of each file are read whole, so no local clone is needed.
    /// </summary>
    public static async Task<(string Title, string Detail, string Root, List<FilePair> Pairs)> Pairs(PullRequest pull, CancellationToken token)
    {
        var account = pull.Account;
        var key = await Token(account, token).ConfigureAwait(false);
        var repo = pull.Repository;
        string title, tip, head;
        using (var json = await Get(account, key, $"repos/{repo}/pulls/{pull.Number}", token).ConfigureAwait(false))
        {
            title = json.RootElement.GetProperty("title").GetString() ?? pull.Title;
            tip = json.RootElement.GetProperty("base").GetProperty("sha").GetString()!;
            head = json.RootElement.GetProperty("head").GetProperty("sha").GetString()!;
        }
        string from;
        using (var json = await Get(account, key, $"repos/{repo}/compare/{tip}...{head}?per_page=1", token).ConfigureAwait(false))
            from = json.RootElement.GetProperty("merge_base_commit").GetProperty("sha").GetString()!;
        var changes = new List<(FileStatus Status, string Path, string? OldPath)>();
        for (int page = 1; page <= 30; page++)
        {
            using var json = await Get(account, key, $"repos/{repo}/pulls/{pull.Number}/files?per_page=100&page={page}", token).ConfigureAwait(false);
            int count = 0;
            foreach (var file in json.RootElement.EnumerateArray())
            {
                count++;
                var path = file.GetProperty("filename").GetString()!;
                var old = file.TryGetProperty("previous_filename", out var previous) ? previous.GetString() : null;
                var status = file.GetProperty("status").GetString() switch
                {
                    "added" or "copied" => FileStatus.Added,
                    "removed" => FileStatus.Deleted,
                    "renamed" => FileStatus.Renamed,
                    _ => FileStatus.Modified
                };
                changes.Add((status, path, status == FileStatus.Renamed ? old : null));
            }
            if (count < 100)
                break;
        }
        using var gate = new SemaphoreSlim(8);
        var pairs = await Task.WhenAll(changes.Select(async change =>
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var before = change.Status == FileStatus.Added ? TextFile.Empty
                    : TextFile.Decode(await Content(account, key, repo, change.OldPath ?? change.Path, from, token).ConfigureAwait(false));
                var after = change.Status == FileStatus.Deleted ? TextFile.Empty
                    : TextFile.Decode(await Content(account, key, repo, change.Path, head, token).ConfigureAwait(false));
                return new FilePair(change.Path, change.OldPath, change.Status, before, after);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);
        return ($"{repo} #{pull.Number}", title, "", [.. pairs]);
    }

    /// <summary>A file's bytes at a commit; a file GitHub cannot serve (a submodule, a missing path) reads as empty.</summary>
    static async Task<byte[]> Content(GitHubAccount account, string key, string repo, string path, string sha, CancellationToken token)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        using var request = Request(account, key, $"repos/{repo}/contents/{escaped}?ref={Uri.EscapeDataString(sha)}", "application/vnd.github.raw");
        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }

    static async Task<JsonDocument> Get(GitHubAccount account, string key, string path, CancellationToken token)
    {
        using var request = Request(account, key, path, "application/vnd.github+json");
        using var response = await Http.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} for {path.Split('?')[0]}.");
        await using var body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(body, cancellationToken: token).ConfigureAwait(false);
    }

    static HttpRequestMessage Request(GitHubAccount account, string key, string path, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(account.Config.ApiFor(account.Host), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd("kumiko-diff");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    /// <summary>Every account gh is logged in to, the active one of each host first.</summary>
    static async Task<ImmutableArray<GitHubAccount>> Accounts(GitHubConfig config, CancellationToken token)
    {
        // gh exits with 1 when one account has a problem but still describes all of them.
        var (output, _) = await Gh(config, token, "auth", "status", "--json", "hosts").ConfigureAwait(false);
        using var json = JsonDocument.Parse(output);
        var accounts = ImmutableArray.CreateBuilder<GitHubAccount>();
        foreach (var host in json.RootElement.GetProperty("hosts").EnumerateObject())
            foreach (var account in host.Value.EnumerateArray().OrderByDescending(a => a.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.True))
                if (account.GetProperty("state").GetString() == "success" && account.GetProperty("login").GetString() is { Length: > 0 } login)
                    accounts.Add(new(config, host.Name, login));
        return accounts.ToImmutable();
    }

    static async Task<string> Token(GitHubAccount account, CancellationToken token)
    {
        var (output, code) = await Gh(account.Config, token, "auth", "token", "--hostname", account.Host, "--user", account.Login).ConfigureAwait(false);
        var key = output.Trim();
        return code == 0 && key.Length > 0 ? key : throw new IOException($"gh has no token for {account.Login} on {account.Host}.");
    }

    /// <summary>Runs gh without prompts or colour and returns standard output with the exit code.</summary>
    static async Task<(string Output, int Code)> Gh(GitHubConfig config, CancellationToken token, params string[] arguments)
    {
        var start = new ProcessStartInfo(config.Gh)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["GH_PROMPT_DISABLED"] = "1";
        start.Environment["NO_COLOR"] = "1";
        using var process = Process.Start(start) ?? throw new IOException("Could not start gh.");
        process.StandardInput.Close();
        using var cancel = token.Register(() => Git.Kill(process));
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        await error.ConfigureAwait(false);
        return (await output.ConfigureAwait(false), process.ExitCode);
    }
}
