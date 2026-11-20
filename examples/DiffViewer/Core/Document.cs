using System.Collections.Immutable;
using System.Runtime.InteropServices;
using DiffViewer.Highlight;

namespace DiffViewer.Core;

public enum FileStatus : byte
{
    Modified,
    Added,
    Deleted,
    Renamed
}

/// <summary>The raw material of one file comparison, before diffing.</summary>
public sealed record FilePair(string Path, string? OldPath, FileStatus Status, TextFile Old, TextFile New);

/// <summary>
/// One compared file. Lines concatenates every hunk's lines; HunkStarts[h] is the first index of hunk h.
/// Emphasis is parallel to Lines and holds word-level changes of paired removed/added lines.
/// </summary>
public sealed record FileDiff(
    string Path,
    string? OldPath,
    FileStatus Status,
    bool Binary,
    Language Language,
    ImmutableArray<string> OldLines,
    ImmutableArray<string> NewLines,
    Highlighting OldTokens,
    Highlighting NewTokens,
    ImmutableArray<Hunk> Hunks,
    ImmutableArray<DiffLine> Lines,
    ImmutableArray<int> HunkStarts,
    ImmutableArray<ImmutableArray<Emphasis>> Emphasis,
    int Additions,
    int Deletions,
    int MaxColumns,
    bool OldMissingNewline,
    bool NewMissingNewline)
{
    /// <summary>The larger size of a side that was not read because it is too large to carry over SSH; 0 otherwise.</summary>
    public long Omitted
    {
        get; init;
    }

    public string Name => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
    public string Text(DiffLine line) => line.Kind == LineKind.Removed ? OldLines[line.Old] : NewLines[line.New];
}

/// <summary>What was compared, for the toolbar, and every changed file sorted by path.</summary>
public sealed record DiffDocument(string Title, string Detail, string Root, ImmutableArray<FileDiff> Files)
{
    public static DiffDocument Empty { get; } = new("", "", "", []);
    public int Additions => Files.Sum(f => f.Additions);
    public int Deletions => Files.Sum(f => f.Deletions);
}

public static class Differ
{
    /// <summary>Diffs, highlights and pairs one file. Pure; safe to run for many files in parallel.</summary>
    public static FileDiff Build(FilePair pair, bool ignoreWhitespace, int context, CancellationToken token)
    {
        var language = Languages.FromPath(pair.Path);
        var binary = pair.Old.Binary || pair.New.Binary;
        var oldLines = pair.Old.Lines;
        var newLines = pair.New.Lines;
        if (binary)
        {
            oldLines = [];
            newLines = [];
        }
        var hunks = LineDiff.Compute(oldLines, newLines, context, ignoreWhitespace, token);
        var flat = new List<DiffLine>();
        var starts = new int[hunks.Length];
        int additions = 0, deletions = 0, columns = 0;
        for (int h = 0; h < hunks.Length; h++)
        {
            starts[h] = flat.Count;
            foreach (var line in hunks[h].Lines)
            {
                flat.Add(line);
                additions += line.Kind == LineKind.Added ? 1 : 0;
                deletions += line.Kind == LineKind.Removed ? 1 : 0;
                var text = line.Kind == LineKind.Removed ? oldLines[line.Old] : newLines[line.New];
                columns = Math.Max(columns, text.Length);
            }
        }
        token.ThrowIfCancellationRequested();
        var lines = flat.ToArray();
        var emphasis = Emphasize(lines, oldLines, newLines);
        // Only shown lines are read from here on. The others feed the diff and the highlighter's state across lines,
        // then are dropped, so memory follows the size of the diff rather than of the files.
        var keepOld = Shown(oldLines.Length, lines, LineKind.Added);
        var keepNew = Shown(newLines.Length, lines, LineKind.Removed);
        // Only the sides that are shown need tokens; files without changes on a side skip it.
        var oldTokens = deletions + lines.Length > 0 ? Highlighter.Tokenize(language, oldLines, keepOld) : Highlighting.None(oldLines.Length);
        var newTokens = Highlighter.Tokenize(language, newLines, keepNew);
        oldLines = Keep(oldLines, keepOld);
        newLines = Keep(newLines, keepNew);
        return new(pair.Path, pair.OldPath, pair.Status, binary, language, oldLines, newLines, oldTokens, newTokens, hunks,
            ImmutableCollectionsMarshal.AsImmutableArray(lines), ImmutableCollectionsMarshal.AsImmutableArray(starts),
            emphasis, additions, deletions, columns, pair.Old.MissingFinalNewline, pair.New.MissingFinalNewline)
        {
            Omitted = Math.Max(pair.Old.Omitted, pair.New.Omitted)
        };
    }

    /// <summary>Marks one side's shown lines: every kind but <paramref name="other"/>, the other side's own lines.</summary>
    static bool[] Shown(int count, DiffLine[] lines, LineKind other)
    {
        var shown = new bool[count];
        foreach (var line in lines)
            if (line.Kind != other)
                shown[other == LineKind.Added ? line.Old : line.New] = true;
        return shown;
    }

    /// <summary>Keeps the text of marked lines; the others become empty, so line numbers stay as they are.</summary>
    static ImmutableArray<string> Keep(ImmutableArray<string> lines, bool[] keep)
    {
        if (Array.TrueForAll(keep, static k => k))
            return lines;
        var kept = new string[lines.Length];
        for (int i = 0; i < kept.Length; i++)
            kept[i] = keep[i] ? lines[i] : "";
        return ImmutableCollectionsMarshal.AsImmutableArray(kept);
    }

    /// <summary>Pairs each block of removed lines with the added lines that follow it and marks changed words.</summary>
    static ImmutableArray<ImmutableArray<Emphasis>> Emphasize(DiffLine[] lines, ImmutableArray<string> oldLines, ImmutableArray<string> newLines)
    {
        var result = new ImmutableArray<Emphasis>[lines.Length];
        int i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Kind != LineKind.Removed)
            {
                i++;
                continue;
            }
            int removedStart = i;
            while (i < lines.Length && lines[i].Kind == LineKind.Removed)
                i++;
            int addedStart = i;
            while (i < lines.Length && lines[i].Kind == LineKind.Added)
                i++;
            int removed = addedStart - removedStart, added = i - addedStart;
            if (added == 0)
                continue;
            var partners = new int[removed];
            WordDiff.Pair(lines.AsSpan(removedStart, removed), lines.AsSpan(addedStart, added), oldLines, newLines, partners);
            for (int r = 0; r < removed; r++)
            {
                if (partners[r] < 0)
                    continue;
                var oldLine = lines[removedStart + r];
                var newLine = lines[addedStart + partners[r]];
                var (before, after) = WordDiff.Compute(oldLines[oldLine.Old], newLines[newLine.New]);
                result[removedStart + r] = before;
                result[addedStart + partners[r]] = after;
            }
        }
        for (int k = 0; k < result.Length; k++)
            if (result[k].IsDefault)
                result[k] = [];
        return ImmutableCollectionsMarshal.AsImmutableArray(result);
    }
}
