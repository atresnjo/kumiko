using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Flint;

/// <summary>
/// A unified-diff row. Kind is ' ', '+', '-', '@' (hunk), 'f' (file header), 'm' (notice) or '\\' (no newline).
/// Changes holds start/length pairs of word-level edits in paired replacement lines.
/// </summary>
public sealed record DiffLine(string Id, string Text, char Kind, int? Old = null, int? New = null, ImmutableArray<int>? Changes = null)
{
    public ImmutableArray<int> Words => Changes ?? [];
}
public sealed record DiffMatch(int Row, int Start, int Length);
public static partial class Diff
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex Hunk();
    static readonly string[] Notices = ["new file mode", "deleted file mode", "old mode", "new mode", "rename from", "rename to",
        "copy from", "copy to", "Binary files", "GIT binary patch", "[Patch truncated"];
    public static ImmutableArray<DiffLine> Parse(string patch)
    {
        var rows = ImmutableArray.CreateBuilder<DiffLine>();
        var oldLine = 0;
        var newLine = 0;
        var hunk = false;
        foreach (var text in patch.Split('\n'))
        {
            var line = text.TrimEnd('\r');
            var match = hunk || line.StartsWith("@@", StringComparison.Ordinal) ? Hunk().Match(line) : Match.Empty;
            if (match.Success)
            {
                oldLine = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                newLine = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                hunk = true;
                rows.Add(new("line-" + rows.Count, line, '@'));
            }
            else if (hunk && line.Length > 0 && line[0] is ' ' or '+' or '-')
                rows.Add(new("line-" + rows.Count, line[1..], line[0], line[0] == '+' ? null : oldLine++, line[0] == '-' ? null : newLine++));
            else if (hunk && line.StartsWith('\\'))
                rows.Add(new("line-" + rows.Count, line[1..].Trim(), '\\'));
            else if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                hunk = false;
                rows.Add(new("line-" + rows.Count, Header(line), 'f'));
            }
            else if (!hunk && Notices.Any(n => line.StartsWith(n, StringComparison.Ordinal)) || line.StartsWith("[Patch truncated", StringComparison.Ordinal))
                rows.Add(new("line-" + rows.Count, line, 'm'));
            else if (line.Length > 0)
                hunk = false;
        }
        Words(rows);
        return rows.ToImmutable();
    }
    /// <summary>The file a header describes: its new path, with the old one when renamed.</summary>
    static string Header(string line)
    {
        var body = line[11..];
        var split = body.IndexOf(" b/", StringComparison.Ordinal);
        if (!body.StartsWith("a/", StringComparison.Ordinal) || split < 0)
            return body;
        var from = body[2..split];
        var to = body[(split + 3)..];
        return from == to ? to : from + " → " + to;
    }
    /// <summary>Pairs each removed block with the added block after it and marks the tokens that differ.</summary>
    static void Words(ImmutableArray<DiffLine>.Builder rows)
    {
        for (var i = 0; i < rows.Count;)
        {
            if (rows[i].Kind != '-')
            {
                i++;
                continue;
            }
            var removed = i;
            while (i < rows.Count && rows[i].Kind == '-')
                i++;
            var added = i;
            while (i < rows.Count && rows[i].Kind == '+')
                i++;
            var pairs = Math.Min(added - removed, i - added);
            for (var k = 0; k < pairs; k++)
            {
                var (before, after) = Compare(rows[removed + k].Text, rows[added + k].Text);
                if (before.IsEmpty && after.IsEmpty)
                    continue;
                rows[removed + k] = rows[removed + k] with
                {
                    Changes = before
                };
                rows[added + k] = rows[added + k] with
                {
                    Changes = after
                };
            }
        }
    }
    /// <summary>Token LCS; lines without a common word are a rewrite and get no word emphasis.</summary>
    public static (ImmutableArray<int> Before, ImmutableArray<int> After) Compare(string a, string b)
    {
        if (a == b)
            return ([], []);
        var x = Tokens(a);
        var y = Tokens(b);
        if ((long)x.Length * y.Length > 40_000)
            return Affix(a, b);
        var table = new int[x.Length + 1, y.Length + 1];
        for (var i = x.Length - 1; i >= 0; i--)
            for (var j = y.Length - 1; j >= 0; j--)
                table[i, j] = Same(a, x[i], b, y[j]) ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
        var keepA = new bool[x.Length];
        var keepB = new bool[y.Length];
        var shared = false;
        for (int i = 0, j = 0; i < x.Length && j < y.Length;)
        {
            if (Same(a, x[i], b, y[j]))
            {
                keepA[i] = keepB[j] = true;
                shared |= !char.IsWhiteSpace(a[x[i].Start]);
                i++;
                j++;
            }
            else if (table[i + 1, j] >= table[i, j + 1])
                i++;
            else
                j++;
        }
        return shared ? (Ranges(x, keepA), Ranges(y, keepB)) : ([], []);
    }
    static (ImmutableArray<int>, ImmutableArray<int>) Affix(string a, string b)
    {
        var start = 0;
        while (start < Math.Min(a.Length, b.Length) && a[start] == b[start])
            start++;
        var suffix = 0;
        while (suffix < Math.Min(a.Length, b.Length) - start && a[^(suffix + 1)] == b[^(suffix + 1)])
            suffix++;
        if (start == 0 && suffix == 0)
            return ([], []);
        return (a.Length - start - suffix > 0 ? [start, a.Length - start - suffix] : [], b.Length - start - suffix > 0 ? [start, b.Length - start - suffix] : []);
    }
    static bool Same(string a, Range x, string b, Range y) => a.AsSpan(x.Start, x.Length).SequenceEqual(b.AsSpan(y.Start, y.Length));
    readonly record struct Range(int Start, int Length);
    static Range[] Tokens(string text)
    {
        var tokens = new List<Range>();
        for (var i = 0; i < text.Length;)
        {
            var start = i;
            var kind = Class(text[i]);
            i++;
            if (kind != 2)
                while (i < text.Length && Class(text[i]) == kind)
                    i++;
            tokens.Add(new(start, i - start));
        }
        return [.. tokens];
    }
    static int Class(char ch) => char.IsLetterOrDigit(ch) || ch == '_' ? 0 : char.IsWhiteSpace(ch) ? 1 : 2;
    static ImmutableArray<int> Ranges(Range[] tokens, bool[] keep)
    {
        var result = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (keep[i])
                continue;
            var start = tokens[i].Start;
            var end = start + tokens[i].Length;
            while (i + 1 < tokens.Length && !keep[i + 1])
                end = tokens[++i].Start + tokens[i].Length;
            result.Add(start);
            result.Add(end - start);
        }
        return result.ToImmutable();
    }
    public static ImmutableArray<DiffMatch> Matches(ImmutableArray<DiffLine> lines, string query)
    {
        if (query.Length == 0 || lines.IsDefaultOrEmpty)
            return [];
        var matches = ImmutableArray.CreateBuilder<DiffMatch>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Kind is not (' ' or '+' or '-'))
                continue;
            var text = lines[i].Text;
            for (var start = 0; start <= text.Length - query.Length;)
            {
                var at = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                    break;
                matches.Add(new(i, at, query.Length));
                start = at + query.Length;
            }
        }
        return matches.ToImmutable();
    }
}
