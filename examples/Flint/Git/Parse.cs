using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Flint.Git;

static class Parse
{
    public const string LogFormat = "%H%x00%P%x00%an%x00%ae%x00%at%x00%s%x00%D%x1e";
    public const string RefFormat = "%(refname)%00%(objectname)%00%(*objectname)%00%(upstream:short)%00%(HEAD)%00%(upstream:track,nobracket)";
    public static WorkingStatus Status(string output)
    {
        var head = new Head();
        var staged = ImmutableArray.CreateBuilder<FileChange>();
        var unstaged = ImmutableArray.CreateBuilder<FileChange>();
        var entries = output.Split('\0');
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Length < 2)
                continue;
            if (entry[0] == '#')
            {
                var p = entry.Split(' ');
                head = p[1] switch
                {
                    "branch.oid" => head with { Sha = p[2] == "(initial)" ? "" : p[2], Unborn = p[2] == "(initial)" },
                    "branch.head" => head with { Branch = p[2] == "(detached)" ? "" : p[2], Detached = p[2] == "(detached)" },
                    "branch.upstream" => head with { Upstream = p[2] },
                    "branch.ab" => head with { Ahead = int.Parse(p[2][1..], CultureInfo.InvariantCulture), Behind = int.Parse(p[3][1..], CultureInfo.InvariantCulture) },
                    _ => head
                };
            }
            else if (entry[0] is '1' or '2')
            {
                var parts = entry.Split(' ', entry[0] == '1' ? 9 : 10);
                var path = parts[^1];
                var original = entry[0] == '2' ? entries[++i] : null;
                if (parts[1][0] != '.')
                    staged.Add(new(path, Letter(parts[1][0]), original));
                if (parts[1][1] != '.')
                    unstaged.Add(new(path, Letter(parts[1][1]), original));
            }
            else if (entry[0] == '?')
                unstaged.Add(new(entry[2..], '?'));
            else if (entry[0] == 'u')
                unstaged.Add(new(entry.Split(' ', 11)[^1], 'U'));
        }
        staged.Sort(TreeOrder.Files);
        unstaged.Sort(TreeOrder.Files);
        return new(head, staged.ToImmutable(), unstaged.ToImmutable());
    }
    static char Letter(char status) => status is 'A' or 'M' or 'D' or 'R' or 'C' or 'T' or 'U' ? status : 'M';
    public static ImmutableArray<Commit> Log(string output)
    {
        var commits = ImmutableArray.CreateBuilder<Commit>();
        var avatars = new Dictionary<string, Avatar>(StringComparer.Ordinal);
        foreach (var record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = record.TrimStart('\n').Split('\0');
            if (f.Length < 7)
                continue;
            if (!avatars.TryGetValue(f[3], out var avatar))
                avatars[f[3]] = avatar = Identicon(f[3]);
            commits.Add(new(f[0], [.. f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)], f[2], f[3],
                long.Parse(f[4], CultureInfo.InvariantCulture), f[5], Decoration(f[6]), avatar));
        }
        return commits.ToImmutable();
    }
    /// <summary>`%D` with --decorate=full: HEAD -> refs/heads/main, refs/remotes/origin/main, tag: refs/tags/v1.</summary>
    public static ImmutableArray<RefLabel> Decoration(string raw)
    {
        if (raw.Length == 0)
            return [];
        var refs = new List<RefLabel>();
        foreach (var item in raw.Split(", "))
        {
            var part = item;
            var current = false;
            if (part.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                current = true;
                part = part[8..];
            }
            else if (part == "HEAD")
            {
                refs.Add(new("HEAD", RefKind.Head, true));
                continue;
            }
            if (part.StartsWith("tag: ", StringComparison.Ordinal))
                part = part[5..];
            if (part.StartsWith("refs/heads/", StringComparison.Ordinal))
                refs.Add(new(part[11..], RefKind.Local, current));
            else if (part.StartsWith("refs/remotes/", StringComparison.Ordinal) && !part.EndsWith("/HEAD", StringComparison.Ordinal))
                refs.Add(new(part[13..], RefKind.Remote));
            else if (part.StartsWith("refs/tags/", StringComparison.Ordinal))
                refs.Add(new(part[10..], RefKind.Tag));
        }
        return [.. refs.OrderByDescending(r => r.Current).ThenBy(r => r.Kind)];
    }
    public static ImmutableArray<GitRef> Refs(string output)
    {
        var refs = new List<GitRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 5 || f[0].EndsWith("/HEAD", StringComparison.Ordinal))
                continue;
            var track = f.Length > 5 ? f[5] : "";
            var kind = f[0].StartsWith("refs/heads/", StringComparison.Ordinal) ? "branch" : f[0].StartsWith("refs/remotes/", StringComparison.Ordinal) ? "remote" : "tag";
            var name = kind switch
            {
                "branch" => f[0][11..],
                "remote" => f[0][13..],
                _ => f[0].Replace("refs/tags/", "", StringComparison.Ordinal)
            };
            refs.Add(new(name, f[2].Length > 0 ? f[2] : f[1], kind, f[4] == "*", f[3], Count(track, "ahead "), Count(track, "behind "), track == "gone"));
        }
        // Tree order per kind: leaves before folders at each level; newest tags first like the original.
        return [.. refs.Where(r => r.Kind == "branch").Order(TreeOrder.Refs), .. refs.Where(r => r.Kind == "remote").Order(TreeOrder.Refs),
            .. refs.Where(r => r.Kind == "tag").Reverse()];
    }
    static int Count(string track, string label)
    {
        var at = track.IndexOf(label, StringComparison.Ordinal);
        if (at < 0)
            return 0;
        var end = at + label.Length;
        while (end < track.Length && char.IsAsciiDigit(track[end]))
            end++;
        return int.Parse(track.AsSpan(at + label.Length, end - at - label.Length), CultureInfo.InvariantCulture);
    }
    public static ImmutableArray<FileChange> Files(string output)
    {
        var fields = output.Split('\0');
        var files = ImmutableArray.CreateBuilder<FileChange>();
        for (var i = 0; i + 1 < fields.Length; i++)
        {
            if (fields[i].Length == 0)
                continue;
            var status = Letter(fields[i][0]);
            var path = fields[++i];
            if (status is 'R' or 'C' && i + 1 < fields.Length)
                files.Add(new(fields[++i], status, path));
            else
                files.Add(new(path, status));
        }
        return files.ToImmutable();
    }
    public static ImmutableArray<Stash> Stashes(string output) => [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Split('\0')).Where(f => f.Length >= 3).Select(f => new Stash(f[0], f[1], StashMessage(f[2])))];
    static string StashMessage(string message)
    {
        if (message.StartsWith("WIP on ", StringComparison.Ordinal) && message.IndexOf(": ", StringComparison.Ordinal) is var wip and > 0)
            return "WIP: " + message[(wip + 2)..];
        if (message.StartsWith("On ", StringComparison.Ordinal) && message.IndexOf(": ", StringComparison.Ordinal) is var on and > 0)
            return message[(on + 2)..];
        return message;
    }
    public static ImmutableArray<Worktree> Worktrees(string output)
    {
        var result = ImmutableArray.CreateBuilder<Worktree>();
        Worktree? current = null;
        foreach (var line in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                if (current is not null)
                    result.Add(current);
                current = new(line[9..], "", "");
            }
            else if (current is not null)
                current = line switch
                {
                    var s when s.StartsWith("HEAD ", StringComparison.Ordinal) => current with { Sha = s[5..] },
                    var s when s.StartsWith("branch refs/heads/", StringComparison.Ordinal) => current with { Branch = s[18..] },
                    "detached" => current with { Detached = true },
                    "bare" => current with { Bare = true },
                    var s when s.StartsWith("locked", StringComparison.Ordinal) => current with { Locked = true },
                    var s when s.StartsWith("prunable", StringComparison.Ordinal) => current with { Prunable = true },
                    _ => current
                };
        }
        if (current is not null)
            result.Add(current);
        return result.ToImmutable();
    }
    /// <summary>The original's local identicon: MD5 of the trimmed lower-case email, symmetric cells and an HSL tint.</summary>
    public static Avatar Identicon(string email)
    {
        var h = MD5.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        var cells = 0;
        for (var i = 0; i < 15; i++)
            if ((h[i] & 1) == 0)
                cells |= 1 << i;
        var hue = Math.Round(((h[12] << 8) | h[13]) / 65535.0 * 360);
        var saturation = 0.45 + h[14] % 20 / 100.0;
        var lightness = 0.5 + h[15] % 12 / 100.0;
        var a = saturation * Math.Min(lightness, 1 - lightness);
        uint Channel(double n)
        {
            var k = (n + hue / 30) % 12;
            return (uint)Math.Round(255 * (lightness - a * Math.Max(-1, Math.Min(Math.Min(k - 3, 9 - k), 1))));
        }
        return new(cells, Channel(0) << 16 | Channel(8) << 8 | Channel(4));
    }
}

/// <summary>Orders slash-delimited paths into the tree order the views walk linearly.</summary>
static class TreeOrder
{
    /// <summary>Folders before files at each level, then names case-insensitively.</summary>
    public static readonly IComparer<FileChange> Files = Comparer<FileChange>.Create(static (a, b) => Compare(a.Path, b.Path, true));
    /// <summary>Leaves before folders at each level (main sits above feature/), like the original sidebar.</summary>
    public static readonly IComparer<GitRef> Refs = Comparer<GitRef>.Create(static (a, b) => Compare(a.Name, b.Name, false));
    public static int Compare(string a, string b, bool foldersFirst)
    {
        var i = 0;
        var j = 0;
        while (true)
        {
            var aEnd = a.IndexOf('/', i);
            var bEnd = b.IndexOf('/', j);
            var aLeaf = aEnd < 0;
            var bLeaf = bEnd < 0;
            if (aLeaf != bLeaf)
                return aLeaf == foldersFirst ? 1 : -1;
            var x = a.AsSpan(i, (aLeaf ? a.Length : aEnd) - i);
            var y = b.AsSpan(j, (bLeaf ? b.Length : bEnd) - j);
            var order = x.CompareTo(y, StringComparison.OrdinalIgnoreCase);
            if (order == 0)
                order = x.CompareTo(y, StringComparison.Ordinal);
            if (order != 0 || aLeaf)
                return order;
            i = aEnd + 1;
            j = bEnd + 1;
        }
    }
}
