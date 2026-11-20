using System.Collections.Immutable;

namespace DiffViewer.Sources;

/// <summary>An SSH host the palette offers: its alias and, when the config names them, user@HostName.</summary>
public sealed record SshHost(string Alias, string Address);

/// <summary>
/// The hosts named in <c>~/.ssh/config</c> and the files it includes, in the order ssh reads them. Only plain
/// <c>Host</c> aliases are listed: wildcard and negated patterns, <c>Match</c> blocks and Git hosting services, which
/// give no shell, are left out. Every call reads the files; run it from commands.
/// </summary>
public static class SshConfig
{
    /// <summary>ssh stops at this depth of nested Include directives.</summary>
    const int IncludeDepth = 16;

    static readonly HashSet<string> GitHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com", "ssh.github.com", "gitlab.com", "altssh.gitlab.com", "bitbucket.org", "altssh.bitbucket.org",
        "ssh.dev.azure.com", "vs-ssh.visualstudio.com", "codeberg.org", "gitea.com"
    };

    sealed class Block(ImmutableArray<string> aliases)
    {
        public ImmutableArray<string> Aliases { get; } = aliases;
        public string? HostName
        {
            get; set;
        }
        public string? User
        {
            get; set;
        }
    }

    public static ImmutableArray<SshHost> Hosts(string home)
    {
        var ssh = Path.Combine(home, ".ssh");
        var blocks = new List<Block>();
        Read(Path.Combine(ssh, "config"), home, blocks, 0);
        var hosts = ImmutableArray.CreateBuilder<SshHost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in blocks.SelectMany(b => b.Aliases))
        {
            if (!seen.Add(alias))
                continue;
            // As ssh does, the first value a matching block gives wins.
            var named = blocks.Where(b => b.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase)).ToArray();
            var hostName = named.Select(b => b.HostName).FirstOrDefault(v => v is not null)?.Replace("%h", alias, StringComparison.Ordinal);
            var user = named.Select(b => b.User).FirstOrDefault(v => v is not null);
            if (GitHosts.Contains(alias) || hostName is not null && GitHosts.Contains(hostName))
                continue;
            var address = hostName is null && user is null ? "" : (user is null ? "" : user + "@") + (hostName ?? alias);
            hosts.Add(new(alias, address));
        }
        return hosts.ToImmutable();
    }

    static void Read(string file, string home, List<Block> blocks, int depth)
    {
        string[] lines;
        try
        {
            if (depth > IncludeDepth || !File.Exists(file))
                return;
            lines = File.ReadAllLines(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        Block? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            // "Keyword value", "Keyword=value" and "Keyword = value" are all accepted.
            int end = line.IndexOfAny([' ', '\t', '=']);
            if (end < 0)
                continue;
            var keyword = line[..end];
            var value = line[end..].TrimStart(' ', '\t', '=').TrimStart();
            if (keyword.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                current = new([.. Words(value).Where(w => w.IndexOfAny(['*', '?', '!']) < 0)]);
                blocks.Add(current);
            }
            else if (keyword.Equals("Match", StringComparison.OrdinalIgnoreCase))
            {
                current = new([]);
                blocks.Add(current);
            }
            else if (keyword.Equals("Include", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pattern in Words(value))
                    foreach (var included in Expand(pattern, home))
                        Read(included, home, blocks, depth + 1);
            }
            else if (current is not null && keyword.Equals("HostName", StringComparison.OrdinalIgnoreCase))
                current.HostName ??= Words(value).FirstOrDefault();
            else if (current is not null && keyword.Equals("User", StringComparison.OrdinalIgnoreCase))
                current.User ??= Words(value).FirstOrDefault();
        }
    }

    /// <summary>Whitespace-separated words; a double-quoted word may contain spaces.</summary>
    static IEnumerable<string> Words(string value)
    {
        int i = 0;
        while (i < value.Length)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                i++;
                continue;
            }
            if (value[i] == '"')
            {
                int close = value.IndexOf('"', i + 1);
                close = close < 0 ? value.Length : close;
                yield return value[(i + 1)..close];
                i = close + 1;
                continue;
            }
            int start = i;
            while (i < value.Length && !char.IsWhiteSpace(value[i]))
                i++;
            yield return value[start..i];
        }
    }

    /// <summary>An Include path: ~ is the home folder, relative paths are in ~/.ssh, and the file name may hold * and ?.</summary>
    static IEnumerable<string> Expand(string pattern, string home)
    {
        var path = pattern.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, pattern[2..])
            : Path.IsPathRooted(pattern) ? pattern : Path.Combine(home, ".ssh", pattern);
        var name = Path.GetFileName(path);
        if (name.IndexOfAny(['*', '?']) < 0)
            return [path];
        var folder = Path.GetDirectoryName(path);
        try
        {
            return folder is not null && Directory.Exists(folder) ? Directory.EnumerateFiles(folder, name).Order(StringComparer.Ordinal).ToArray() : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
