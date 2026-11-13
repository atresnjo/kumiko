using System.Collections.Immutable;

namespace DiffViewer.Core;

/// <summary>Finds shown lines containing a query, case-insensitively.</summary>
public static class TextSearch
{
    public static ImmutableArray<int> Rows(ImmutableArray<FileDiff> files, RowLayout layout, string query)
    {
        var rows = ImmutableArray.CreateBuilder<int>();
        for (int r = 0; r < layout.Count; r++)
        {
            var row = layout.Rows[r];
            if (row.Kind is not (RowKind.Line or RowKind.Pair))
                continue;
            var file = files[row.File];
            if (row.A >= 0 && file.Text(file.Lines[row.A]).Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.B >= 0 && row.B != row.A && file.Text(file.Lines[row.B]).Contains(query, StringComparison.OrdinalIgnoreCase))
                rows.Add(r);
        }
        return rows.ToImmutable();
    }
}

/// <summary>Fuzzy path matching for the file finder: query characters in order, rewarding file-name hits.</summary>
public static class FileFinder
{
    public const int Limit = 200;

    public static ImmutableArray<int> Match(ImmutableArray<FileDiff> files, string query)
    {
        if (query.Length == 0)
            return [.. Enumerable.Range(0, Math.Min(files.Length, Limit))];
        var scored = ImmutableArray.CreateBuilder<(int Score, int File)>();
        for (int f = 0; f < files.Length; f++)
            if (Score(files[f].Path, query) is { } score)
                scored.Add((score, f));
        scored.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.File.CompareTo(b.File));
        return [.. scored.Take(Limit).Select(s => s.File)];
    }

    /// <summary>Null when the query is not a subsequence of the path.</summary>
    public static int? Score(string path, string query)
    {
        int name = path.LastIndexOf('/') + 1;
        int score = 0, at = 0, run = 0;
        foreach (var ch in query)
        {
            if (ch == ' ')
                continue;
            int found = -1;
            for (int i = at; i < path.Length; i++)
            {
                if (char.ToLowerInvariant(path[i]) != char.ToLowerInvariant(ch))
                    continue;
                found = i;
                break;
            }
            if (found < 0)
                return null;
            run = found == at ? run + 1 : 1;
            score += run * 2 + (found >= name ? 3 : 0) + (found == name || found > 0 && path[found - 1] is '/' or '.' or '-' or '_' ? 4 : 0);
            at = found + 1;
        }
        return score - path.Length / 16;
    }
}

/// <summary>Where selected text shows: a row, the line (an index into FileDiff.Lines) and its first character.</summary>
public readonly record struct Occurrence(int Row, int Line, int Start);

/// <summary>Finds every shown copy of selected text, case-sensitively, and the word or line a click names.</summary>
public static class TextMatches
{
    /// <summary>Occurrences kept for stepping; a single character can repeat far more often in a large diff.</summary>
    public const int Limit = 50_000;

    /// <summary>
    /// Every non-overlapping occurrence in row order; a split row's old side comes before its new side, and a
    /// context line both sides show counts once.
    /// </summary>
    public static ImmutableArray<Occurrence> Find(ImmutableArray<FileDiff> files, RowLayout layout, string text)
    {
        var found = ImmutableArray.CreateBuilder<Occurrence>();
        if (text.Length == 0)
            return [];
        for (int r = 0; r < layout.Count && found.Count < Limit; r++)
        {
            var row = layout.Rows[r];
            if (row.Kind is not (RowKind.Line or RowKind.Pair))
                continue;
            var file = files[row.File];
            if (row.A >= 0 && row.A != row.B)
                Add(found, file, r, row.A, text);
            if (row.B >= 0)
                Add(found, file, r, row.B, text);
        }
        return found.ToImmutable();
    }

    static void Add(ImmutableArray<Occurrence>.Builder found, FileDiff file, int row, int line, string text)
    {
        var shown = file.Text(file.Lines[line]);
        for (int at = shown.IndexOf(text, StringComparison.Ordinal); at >= 0 && found.Count < Limit;
            at = at + text.Length < shown.Length ? shown.IndexOf(text, at + text.Length, StringComparison.Ordinal) : -1)
            found.Add(new(row, line, at));
    }

    /// <summary>
    /// The range a double click (2) or triple click (3) selects around <paramref name="column"/>: the identifier
    /// there or the single character that is not part of one, or the line without its indentation.
    /// </summary>
    public static (int Start, int End) Around(string line, int column, int clicks)
    {
        if (line.Length == 0)
            return (0, 0);
        if (clicks >= 3)
        {
            int first = line.Length - line.TrimStart().Length;
            return (first, Math.Max(first, line.TrimEnd().Length));
        }
        int at = Math.Clamp(column, 0, line.Length - 1);
        if (!Word(line[at]))
            return (at, at + 1);
        int start = at, end = at + 1;
        while (start > 0 && Word(line[start - 1]))
            start--;
        while (end < line.Length && Word(line[end]))
            end++;
        return (start, end);
    }

    static bool Word(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
