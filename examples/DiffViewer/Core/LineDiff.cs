using System.Collections.Immutable;
using System.Numerics;

namespace DiffViewer.Core;

/// <summary>
/// Line diff in the style of git's histogram algorithm: lines are interned once, regions are split at their
/// lowest-occurrence common run, regions without such an anchor fall back to Myers O(ND) with xdiff's line
/// discarding and cost limit, and the result is slid with git's compaction and indent heuristics.
/// A global work budget turns any remaining region into a whole-region replacement, so pathological inputs stay
/// bounded while the edit script remains valid.
/// </summary>
public static partial class LineDiff
{
    public static ImmutableArray<Hunk> Compute(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines, int context = 3,
        bool ignoreWhitespace = false, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);
        var session = new Session(oldLines, newLines, ignoreWhitespace, token);
        session.Run();
        return session.BuildHunks(Math.Max(0, context));
    }

    sealed partial class Session
    {
        readonly IReadOnlyList<string> oldLines;
        readonly IReadOnlyList<string> newLines;
        readonly bool ignoreWhitespace;
        readonly CancellationToken token;
        readonly int n;
        readonly int m;
        readonly int[] a;
        readonly int[] b;
        readonly bool[] changedOld;
        readonly bool[] changedNew;
        readonly long budget;
        int classCount;
        long work;

        public Session(IReadOnlyList<string> oldLines, IReadOnlyList<string> newLines, bool ignoreWhitespace, CancellationToken token)
        {
            this.oldLines = oldLines;
            this.newLines = newLines;
            this.ignoreWhitespace = ignoreWhitespace;
            this.token = token;
            n = oldLines.Count;
            m = newLines.Count;
            a = new int[n];
            b = new int[m];
            changedOld = new bool[n];
            changedNew = new bool[m];
            budget = 32_000_000L + 24L * (n + m);
        }

        public void Run()
        {
            Intern();
            Histogram();
            token.ThrowIfCancellationRequested();
            Compact(changedOld, a, oldLines, n, changedNew, m);
            Compact(changedNew, b, newLines, m, changedOld, n);
        }

        void Intern()
        {
            var total = n + m;
            var capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(16, total * 2));
            var mask = capacity - 1;
            var slots = new int[capacity];
            var hashes = new int[total];
            var representatives = new string[total];
            for (var i = 0; i < total; i++)
            {
                if ((i & 0xFFFF) == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                var line = (i < n ? oldLines[i] : newLines[i - n]) ?? string.Empty;
                var hash = ignoreWhitespace ? HashIgnoringWhitespace(line) : line.GetHashCode();
                var slot = hash & mask;
                var id = -1;
                while (slots[slot] != 0)
                {
                    var candidate = slots[slot] - 1;
                    if (hashes[candidate] == hash && LinesEqual(representatives[candidate], line))
                    {
                        id = candidate;
                        break;
                    }
                    slot = (slot + 1) & mask;
                }
                if (id < 0)
                {
                    id = classCount;
                    classCount++;
                    hashes[id] = hash;
                    representatives[id] = line;
                    slots[slot] = id + 1;
                }
                if (i < n)
                {
                    a[i] = id;
                }
                else
                {
                    b[i - n] = id;
                }
            }
        }

        bool LinesEqual(string x, string y) => ignoreWhitespace ? EqualsIgnoringWhitespace(x, y) : string.Equals(x, y, StringComparison.Ordinal);

        void MarkOld(int start, int end)
        {
            changedOld.AsSpan(start, end - start).Fill(true);
        }

        void MarkNew(int start, int end)
        {
            changedNew.AsSpan(start, end - start).Fill(true);
        }

        public ImmutableArray<Hunk> BuildHunks(int context)
        {
            var blocks = ChangeBlocks();
            if (blocks.Count == 0)
            {
                return [];
            }
            var hunks = ImmutableArray.CreateBuilder<Hunk>();
            var first = 0;
            while (first < blocks.Count)
            {
                var last = first;
                while (last + 1 < blocks.Count && blocks[last + 1].OldStart - blocks[last].OldEnd <= 2 * context)
                {
                    last++;
                }
                hunks.Add(BuildHunk(blocks, first, last, context));
                first = last + 1;
            }
            return hunks.DrainToImmutable();
        }

        List<Block> ChangeBlocks()
        {
            var blocks = new List<Block>();
            var i = 0;
            var j = 0;
            while (i < n || j < m)
            {
                if (i < n && j < m && !changedOld[i] && !changedNew[j])
                {
                    i++;
                    j++;
                    continue;
                }
                var oldStart = i;
                var newStart = j;
                while (i < n && changedOld[i])
                {
                    i++;
                }
                while (j < m && changedNew[j])
                {
                    j++;
                }
                if (i == oldStart && j == newStart)
                {
                    // One side ran out while the other still has unpaired unchanged lines; report them as changed.
                    i = n;
                    j = m;
                }
                blocks.Add(new Block(oldStart, i, newStart, j));
            }
            return blocks;
        }

        Hunk BuildHunk(List<Block> blocks, int first, int last, int context)
        {
            var head = blocks[first];
            var tail = blocks[last];
            var leading = Math.Min(context, Math.Min(head.OldStart, head.NewStart));
            var oldStart = head.OldStart - leading;
            var newStart = head.NewStart - leading;
            var oldLimit = last + 1 < blocks.Count ? blocks[last + 1].OldStart : n;
            var newLimit = last + 1 < blocks.Count ? blocks[last + 1].NewStart : m;
            var trailing = Math.Min(context, Math.Min(oldLimit - tail.OldEnd, newLimit - tail.NewEnd));
            var oldEnd = tail.OldEnd + trailing;
            var newEnd = tail.NewEnd + trailing;
            var lines = ImmutableArray.CreateBuilder<DiffLine>(EstimateLines(blocks, first, last, oldStart, oldEnd));
            var i = oldStart;
            var j = newStart;
            for (var k = first; k <= last; k++)
            {
                var block = blocks[k];
                while (i < block.OldStart)
                {
                    lines.Add(new DiffLine(LineKind.Context, i, j));
                    i++;
                    j++;
                }
                for (; i < block.OldEnd; i++)
                {
                    lines.Add(new DiffLine(LineKind.Removed, i, -1));
                }
                for (; j < block.NewEnd; j++)
                {
                    lines.Add(new DiffLine(LineKind.Added, -1, j));
                }
            }
            while (i < oldEnd)
            {
                lines.Add(new DiffLine(LineKind.Context, i, j));
                i++;
                j++;
            }
            return new Hunk(oldStart, oldEnd - oldStart, newStart, newEnd - newStart, lines.DrainToImmutable());
        }

        static int EstimateLines(List<Block> blocks, int first, int last, int oldStart, int oldEnd)
        {
            var added = 0;
            for (var k = first; k <= last; k++)
            {
                added += blocks[k].NewEnd - blocks[k].NewStart;
            }
            return oldEnd - oldStart + added;
        }
    }

    readonly record struct Block(int OldStart, int OldEnd, int NewStart, int NewEnd);

    static bool IsSpace(char c) => c <= ' ' ? c == ' ' || c >= '\t' && c <= '\r' : c >= '\u0085' && char.IsWhiteSpace(c);

    static int HashIgnoringWhitespace(string line)
    {
        var hash = 2166136261u;
        foreach (var c in line)
        {
            if (!IsSpace(c))
            {
                hash = (hash ^ c) * 16777619u;
            }
        }
        hash ^= hash >> 15;
        hash *= 0x2C1B3C6Du;
        hash ^= hash >> 12;
        return (int)hash;
    }

    static bool EqualsIgnoringWhitespace(string x, string y)
    {
        var i = 0;
        var j = 0;
        while (true)
        {
            while (i < x.Length && IsSpace(x[i]))
            {
                i++;
            }
            while (j < y.Length && IsSpace(y[j]))
            {
                j++;
            }
            if (i == x.Length || j == y.Length)
            {
                return i == x.Length && j == y.Length;
            }
            if (x[i] != y[j])
            {
                return false;
            }
            i++;
            j++;
        }
    }
}
