using System.Collections.Immutable;

namespace DiffViewer.Core;

public enum ViewMode : byte
{
    Unified,
    Split
}

public enum RowKind : byte
{
    /// <summary>A file header; pinned while its file scrolls.</summary>
    File,
    /// <summary>A hunk header; A is the hunk index.</summary>
    Hunk,
    /// <summary>A unified line; A is the index into FileDiff.Lines.</summary>
    Line,
    /// <summary>A split row; A is the old line and B the new line (indices into FileDiff.Lines, or -1).</summary>
    Pair,
    /// <summary>Text instead of lines, such as a binary or unchanged file.</summary>
    Note,
    /// <summary>Space closing a file.</summary>
    End
}

public readonly record struct Row(RowKind Kind, int File, int A, int B);

/// <summary>
/// The diff flattened into virtual list rows. Offsets has one entry per row plus the total height; FileRows
/// holds each file's header row and ChangeRows the first row of each block of changed lines. Built in updates,
/// never while rendering.
/// </summary>
public sealed record RowLayout(ImmutableArray<Row> Rows, ImmutableArray<float> Offsets, ImmutableArray<int> FileRows,
    ImmutableArray<int> HunkRows, ImmutableArray<int> ChangeRows, int MaxColumns)
{
    public const float FileHeight = 36;
    public const float HunkHeight = 24;
    public const float LineHeight = 20;
    public const float NoteHeight = 40;
    public const float EndHeight = 12;
    public static RowLayout Empty { get; } = new([], [0], [], [], [], 0);

    public int Count => Rows.Length;

    /// <summary>
    /// Lays out every file, or with <paramref name="only"/> that file alone and unfolded. Files left out keep an
    /// empty range in FileRows, so file indices stay those of the document.
    /// </summary>
    public static RowLayout Build(ImmutableArray<FileDiff> files, ViewMode mode, ImmutableSortedSet<string> collapsed, int only = -1)
    {
        var rows = ImmutableArray.CreateBuilder<Row>(files.Sum(f => f.Lines.Length + f.Hunks.Length + 3));
        var fileRows = ImmutableArray.CreateBuilder<int>(files.Length);
        var hunkRows = ImmutableArray.CreateBuilder<int>();
        int columns = 0;
        for (int f = 0; f < files.Length; f++)
        {
            var file = files[f];
            fileRows.Add(rows.Count);
            if (only >= 0 && f != only)
                continue;
            rows.Add(new(RowKind.File, f, -1, -1));
            if (only < 0 && collapsed.Contains(file.Path))
                continue;
            columns = Math.Max(columns, file.MaxColumns);
            if (file.Binary || file.Hunks.IsEmpty)
                rows.Add(new(RowKind.Note, f, -1, -1));
            for (int h = 0; h < file.Hunks.Length; h++)
            {
                hunkRows.Add(rows.Count);
                rows.Add(new(RowKind.Hunk, f, h, -1));
                int start = file.HunkStarts[h];
                int end = h + 1 < file.Hunks.Length ? file.HunkStarts[h + 1] : file.Lines.Length;
                if (mode == ViewMode.Unified)
                    for (int i = start; i < end; i++)
                        rows.Add(new(RowKind.Line, f, i, -1));
                else
                    Pairs(rows, file, f, start, end);
            }
            rows.Add(new(RowKind.End, f, -1, -1));
        }
        var offsets = ImmutableArray.CreateBuilder<float>(rows.Count + 1);
        float y = 0;
        offsets.Add(0);
        for (int i = 0; i < rows.Count; i++)
        {
            y += Height(rows[i].Kind);
            offsets.Add(y);
        }
        var changeRows = ImmutableArray.CreateBuilder<int>();
        for (int i = 0; i < rows.Count; i++)
            if (Changed(files, rows[i]) && (i == 0 || !Changed(files, rows[i - 1])))
                changeRows.Add(i);
        return new(rows.DrainToImmutable(), offsets.MoveToImmutable(), fileRows.MoveToImmutable(), hunkRows.DrainToImmutable(),
            changeRows.DrainToImmutable(), columns);
    }

    /// <summary>A row showing an added or removed line; a split row pairing context lines is unchanged.</summary>
    static bool Changed(ImmutableArray<FileDiff> files, Row row)
    {
        var lines = files[row.File].Lines;
        return row.Kind switch
        {
            RowKind.Line => lines[row.A].Kind != LineKind.Context,
            RowKind.Pair => row.A < 0 || row.B < 0 || lines[row.A].Kind != LineKind.Context || lines[row.B].Kind != LineKind.Context,
            _ => false
        };
    }

    /// <summary>Context lines share a row; a removed block sits beside the added block that replaced it.</summary>
    static void Pairs(ImmutableArray<Row>.Builder rows, FileDiff file, int f, int start, int end)
    {
        int i = start;
        while (i < end)
        {
            if (file.Lines[i].Kind == LineKind.Context)
            {
                rows.Add(new(RowKind.Pair, f, i, i));
                i++;
                continue;
            }
            int removed = i;
            while (i < end && file.Lines[i].Kind == LineKind.Removed)
                i++;
            int added = i;
            while (i < end && file.Lines[i].Kind == LineKind.Added)
                i++;
            int left = added - removed, right = i - added;
            for (int k = 0; k < Math.Max(left, right); k++)
                rows.Add(new(RowKind.Pair, f, k < left ? removed + k : -1, k < right ? added + k : -1));
        }
    }

    public static float Height(RowKind kind) => kind switch
    {
        RowKind.File => FileHeight,
        RowKind.Hunk => HunkHeight,
        RowKind.Note => NoteHeight,
        RowKind.End => EndHeight,
        _ => LineHeight
    };

    /// <summary>The header row pinned above <paramref name="row"/>.</summary>
    public int StickyHeader(int row) => row >= 0 && row < Rows.Length ? FileRows[Rows[row].File] : -1;

    /// <summary>Rows of the file <paramref name="file"/>, as [start, end).</summary>
    public (int Start, int End) FileRange(int file) => (FileRows[file], file + 1 < FileRows.Length ? FileRows[file + 1] : Rows.Length);

    /// <summary>The row showing line <paramref name="line"/> (an index into FileDiff.Lines) of a file, or its header.</summary>
    public int Find(int file, int line)
    {
        if (file < 0 || file >= FileRows.Length)
            return 0;
        var (start, end) = FileRange(file);
        if (line < 0)
            return start;
        int best = start;
        for (int r = start; r < end; r++)
        {
            var row = Rows[r];
            if (row.Kind is RowKind.Line or RowKind.Pair && (row.A == line || row.B == line))
                return r;
            if (row.Kind is RowKind.Line or RowKind.Pair && Math.Max(row.A, row.B) < line)
                best = r;
        }
        return best;
    }

    /// <summary>The line a row shows, preferring the new side, for keeping the cursor across layouts.</summary>
    public static int LineOf(Row row) => row.Kind switch
    {
        RowKind.Line => row.A,
        RowKind.Pair => row.B >= 0 ? row.B : row.A,
        _ => -1
    };
}
