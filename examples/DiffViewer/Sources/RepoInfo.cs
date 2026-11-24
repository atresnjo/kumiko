using System.Collections.Immutable;

namespace DiffViewer.Sources;

/// <summary>A checkout of a repository, as <c>git worktree list</c> names it; Main is the repository's own checkout.</summary>
public sealed record Worktree(string Path, string Branch, bool Main);

/// <summary>Where a repository's remote lives: a host and the owner/name path on it.</summary>
public sealed record GitRemote(string Host, string Repository)
{
    /// <summary>The repository's name without its owner.</summary>
    public string Name => Repository[(Repository.LastIndexOf('/') + 1)..];
}

/// <summary>What Git knows about the open repository: its worktrees and remote. Works the same over SSH.</summary>
public static class RepoInfo
{
    /// <summary>The worktrees, main first, and the URL of origin (else the first remote); null without remotes.</summary>
    public static async Task<(ImmutableArray<Worktree> Worktrees, string? RemoteUrl)> Read(Place at, CancellationToken token)
    {
        var porcelain = await Git.Text(at, token, "worktree", "list", "--porcelain").ConfigureAwait(false);
        var worktrees = ImmutableArray.CreateBuilder<Worktree>();
        foreach (var block in porcelain.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var path = lines.FirstOrDefault(l => l.StartsWith("worktree ", StringComparison.Ordinal))?["worktree ".Length..];
            if (path is null || lines.Contains("bare"))
                continue;
            var branch = lines.FirstOrDefault(l => l.StartsWith("branch ", StringComparison.Ordinal))?["branch ".Length..];
            var head = lines.FirstOrDefault(l => l.StartsWith("HEAD ", StringComparison.Ordinal))?["HEAD ".Length..] ?? "";
            branch = branch is null ? head[..Math.Min(7, head.Length)]
                : branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch["refs/heads/".Length..] : branch;
            worktrees.Add(new(path, branch, worktrees.Count == 0));
        }
        return (worktrees.ToImmutable(), (await Remote(at, token).ConfigureAwait(false))?.Url);
    }

    /// <summary>The name and URL of origin, else of the first remote; null without remotes.</summary>
    public static async Task<(string Name, string Url)?> Remote(Place at, CancellationToken token)
    {
        try
        {
            var remotes = (await Git.Text(at, token, "config", "--get-regexp", @"^remote\..*\.url$").ConfigureAwait(false))
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split(' ', 2)).Where(parts => parts.Length == 2).ToArray();
            var remote = remotes.FirstOrDefault(r => r[0] == "remote.origin.url") ?? remotes.FirstOrDefault();
            return remote is null ? null : (remote[0]["remote.".Length..^".url".Length], remote[1].Trim());
        }
        catch (IOException)
        {
            // Git exits with 1 when the repository has no remotes.
            return null;
        }
    }

    /// <summary>
    /// The host and owner/name of a remote URL: <c>git@host:owner/repo.git</c>, <c>https://host/owner/repo</c> or
    /// <c>ssh://git@host:22/owner/repo.git</c>. <paramref name="resolve"/> turns an SSH alias into its HostName. Local
    /// paths, file:// URLs and paths without an owner give null.
    /// </summary>
    public static GitRemote? Parse(string url, Func<string, string>? resolve = null)
    {
        url = url.Trim();
        string host, path;
        if (url.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.IsFile || uri.Host.Length == 0)
                return null;
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else
        {
            var colon = url.IndexOf(':');
            var slash = url.IndexOf('/');
            // A colon before any slash marks scp-like syntax; anything else is a local path.
            if (colon <= 0 || slash >= 0 && slash < colon)
                return null;
            host = url[..colon];
            host = host[(host.LastIndexOf('@') + 1)..];
            path = url[(colon + 1)..];
        }
        if (resolve is not null)
            host = resolve(host);
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.Ordinal))
            path = path[..^".git".Length];
        return host.Length == 0 || path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length < 2 ? null : new(host.ToLowerInvariant(), path);
    }
}
