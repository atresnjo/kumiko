using System.Buffers;
using System.Collections.Immutable;

namespace DiffViewer.Core;

/// <summary>
/// Intra-line emphasis: lines are split into words (letter, digit and underscore runs), whitespace runs and single
/// other characters; the token sequences are diffed with Myers and changed tokens become emphasis ranges.
/// </summary>
public static class WordDiff
{
    /// <summary>Below this share of unchanged non-space characters a pair gets no emphasis at all.</summary>
    const double MinSimilarity = 0.4;
    const int MaxEdits = 192;
    const int MaxPairBlock = 64;

    public static (ImmutableArray<Emphasis> Old, ImmutableArray<Emphasis> New) Compute(string oldLine, string newLine)
    {
        ArgumentNullException.ThrowIfNull(oldLine);
        ArgumentNullException.ThrowIfNull(newLine);
        if (string.Equals(oldLine, newLine, StringComparison.Ordinal))
        {
            return ([], []);
        }
        var oldTokens = Tokens.Create(oldLine);
        var newTokens = Tokens.Create(newLine);
        try
        {
            if (!Match(oldTokens, newTokens))
            {
                return ([], []);
            }
            var same = 0;
            for (var t = 0; t < oldTokens.Count; t++)
            {
                if (!oldTokens.Changed[t])
                {
                    same += oldTokens.SolidLength(t);
                }
            }
            var longest = Math.Max(oldTokens.Solid, newTokens.Solid);
            if (longest > 0 && same < MinSimilarity * longest)
            {
                return ([], []);
            }
            return (oldTokens.Ranges(), newTokens.Ranges());
        }
        finally
        {
            oldTokens.Return();
            newTokens.Return();
        }
    }

    /// <summary>
    /// Pairs each removed line with a similar added line (index into <paramref name="added"/>, or -1), keeping the
    /// pairs in order. Blocks up to 64 lines on both sides maximise total similarity; larger ones pair positionally.
    /// </summary>
    public static void Pair(ReadOnlySpan<DiffLine> removed, ReadOnlySpan<DiffLine> added, IReadOnlyList<string> oldLines,
        IReadOnlyList<string> newLines, Span<int> addedPartnerOfRemoved)
    {
        ArgumentNullException.ThrowIfNull(oldLines);
        ArgumentNullException.ThrowIfNull(newLines);
        if (addedPartnerOfRemoved.Length < removed.Length)
        {
            throw new ArgumentException("The partner span must hold one entry per removed line.", nameof(addedPartnerOfRemoved));
        }
        addedPartnerOfRemoved[..removed.Length].Fill(-1);
        if (removed.Length == 0 || added.Length == 0)
        {
            return;
        }
        if (removed.Length > MaxPairBlock || added.Length > MaxPairBlock)
        {
            var count = Math.Min(removed.Length, added.Length);
            for (var k = 0; k < count; k++)
            {
                var similarity = Similarity(Signature.Create(oldLines[removed[k].Old]), Signature.Create(newLines[added[k].New]));
                addedPartnerOfRemoved[k] = similarity >= MinSimilarity ? k : -1;
            }
            return;
        }
        var rows = removed.Length;
        var columns = added.Length;
        var left = new Signature[rows];
        var right = new Signature[columns];
        for (var r = 0; r < rows; r++)
        {
            left[r] = Signature.Create(oldLines[removed[r].Old]);
        }
        for (var c = 0; c < columns; c++)
        {
            right[c] = Signature.Create(newLines[added[c].New]);
        }
        var width = columns + 1;
        Span<float> weight = stackalloc float[rows * columns];
        Span<float> score = stackalloc float[(rows + 1) * width];
        score.Clear();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var similarity = (float)Similarity(left[r], right[c]);
                weight[r * columns + c] = similarity >= MinSimilarity ? similarity : -1;
            }
        }
        for (var r = 1; r <= rows; r++)
        {
            for (var c = 1; c <= columns; c++)
            {
                var best = Math.Max(score[(r - 1) * width + c], score[r * width + c - 1]);
                var w = weight[(r - 1) * columns + c - 1];
                if (w > 0)
                {
                    best = Math.Max(best, score[(r - 1) * width + c - 1] + w);
                }
                score[r * width + c] = best;
            }
        }
        var i = rows;
        var j = columns;
        while (i > 0 && j > 0)
        {
            var here = score[i * width + j];
            var w = weight[(i - 1) * columns + j - 1];
            if (w > 0 && here == score[(i - 1) * width + j - 1] + w)
            {
                addedPartnerOfRemoved[i - 1] = j - 1;
                i--;
                j--;
            }
            else if (here == score[(i - 1) * width + j])
            {
                i--;
            }
            else
            {
                j--;
            }
        }
    }

    static double Similarity(Signature x, Signature y)
    {
        var total = x.Total + y.Total;
        if (total == 0)
        {
            return 1;
        }
        var common = 0;
        var i = 0;
        var j = 0;
        while (i < x.Hashes.Length && j < y.Hashes.Length)
        {
            var hx = x.Hashes[i];
            var hy = y.Hashes[j];
            if (hx == hy)
            {
                common += Math.Min(x.Lengths[i], y.Lengths[j]);
                i++;
                j++;
            }
            else if (hx < hy)
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return 2.0 * common / total;
    }

    /// <summary>Marks unchanged tokens by Myers' greedy forward search. False when the lines need too many edits.</summary>
    static bool Match(Tokens x, Tokens y)
    {
        var start = 0;
        while (start < x.Count && start < y.Count && x.Same(start, y, start))
        {
            x.Changed[start] = false;
            y.Changed[start] = false;
            start++;
        }
        var endX = x.Count;
        var endY = y.Count;
        while (endX > start && endY > start && x.Same(endX - 1, y, endY - 1))
        {
            endX--;
            endY--;
            x.Changed[endX] = false;
            y.Changed[endY] = false;
        }
        var nx = endX - start;
        var ny = endY - start;
        if (nx == 0 || ny == 0)
        {
            return true;
        }
        var limit = Math.Min(nx + ny, MaxEdits);
        var offset = limit + 1;
        var v = ArrayPool<int>.Shared.Rent(2 * limit + 3);
        var trace = ArrayPool<int>.Shared.Rent((limit + 1) * (limit + 1));
        try
        {
            v[offset + 1] = 0;
            for (var d = 0; d <= limit; d++)
            {
                for (var k = -d; k <= d; k += 2)
                {
                    var down = k == -d || k != d && v[offset + k - 1] < v[offset + k + 1];
                    var px = down ? v[offset + k + 1] : v[offset + k - 1] + 1;
                    var py = px - k;
                    while (px < nx && py < ny && x.Same(start + px, y, start + py))
                    {
                        px++;
                        py++;
                    }
                    v[offset + k] = px;
                    trace[d * d + k + d] = px;
                    if (px >= nx && py >= ny)
                    {
                        Backtrack(x, y, start, trace, d, k, nx);
                        return true;
                    }
                }
            }
            return false;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(v);
            ArrayPool<int>.Shared.Return(trace);
        }
    }

    static void Backtrack(Tokens x, Tokens y, int start, int[] trace, int d, int k, int endX)
    {
        var cx = endX;
        while (d > 0)
        {
            var pd = d - 1;
            var row = pd * pd + pd;
            var down = k == -d || k != d && trace[row + k - 1] < trace[row + k + 1];
            var prevK = down ? k + 1 : k - 1;
            var prevX = trace[row + prevK];
            var midX = down ? prevX : prevX + 1;
            MarkSnake(x, y, start, midX, cx, k);
            cx = prevX;
            k = prevK;
            d = pd;
        }
        MarkSnake(x, y, start, 0, cx, 0);
    }

    static void MarkSnake(Tokens x, Tokens y, int start, int from, int to, int k)
    {
        for (var sx = from; sx < to; sx++)
        {
            x.Changed[start + sx] = false;
            y.Changed[start + sx - k] = false;
        }
    }

    /// <summary>Token boundaries, hashes and change flags of one line in pooled arrays.</summary>
    sealed class Tokens(string text, int[] starts, int[] hashes, bool[] changed, int count, int solid)
    {
        public readonly string Text = text;
        public readonly int[] Starts = starts;
        public readonly int[] Hashes = hashes;
        public readonly bool[] Changed = changed;
        public readonly int Count = count;
        public readonly int Solid = solid;

        public static Tokens Create(string text)
        {
            var starts = ArrayPool<int>.Shared.Rent(text.Length + 1);
            var hashes = ArrayPool<int>.Shared.Rent(text.Length + 1);
            var count = 0;
            var solid = 0;
            var i = 0;
            while (i < text.Length)
            {
                var end = TokenEnd(text, i);
                starts[count] = i;
                hashes[count] = string.GetHashCode(text.AsSpan(i, end - i));
                if (!char.IsWhiteSpace(text[i]))
                {
                    solid += end - i;
                }
                count++;
                i = end;
            }
            starts[count] = text.Length;
            var changed = ArrayPool<bool>.Shared.Rent(count + 1);
            changed.AsSpan(0, count).Fill(true);
            return new Tokens(text, starts, hashes, changed, count, solid);
        }

        public int Length(int t) => Starts[t + 1] - Starts[t];

        public int SolidLength(int t) => char.IsWhiteSpace(Text[Starts[t]]) ? 0 : Length(t);

        public bool Same(int t, Tokens other, int u) => Hashes[t] == other.Hashes[u] &&
            Text.AsSpan(Starts[t], Length(t)).SequenceEqual(other.Text.AsSpan(other.Starts[u], other.Length(u)));

        /// <summary>Changed token runs as character ranges; runs separated only by whitespace are merged.</summary>
        public ImmutableArray<Emphasis> Ranges()
        {
            var ranges = ImmutableArray.CreateBuilder<Emphasis>();
            var t = 0;
            while (t < Count)
            {
                if (!Changed[t])
                {
                    t++;
                    continue;
                }
                var first = t;
                var last = t;
                t++;
                while (t < Count)
                {
                    if (Changed[t])
                    {
                        last = t;
                        t++;
                        continue;
                    }
                    var gap = t;
                    while (gap < Count && !Changed[gap] && char.IsWhiteSpace(Text[Starts[gap]]))
                    {
                        gap++;
                    }
                    if (gap < Count && gap > t && Changed[gap])
                    {
                        t = gap;
                        continue;
                    }
                    break;
                }
                ranges.Add(new Emphasis(Starts[first], Starts[last + 1] - Starts[first]));
            }
            return ranges.DrainToImmutable();
        }

        public void Return()
        {
            ArrayPool<int>.Shared.Return(Starts);
            ArrayPool<int>.Shared.Return(Hashes);
            ArrayPool<bool>.Shared.Return(Changed);
        }

        public static int TokenEnd(string text, int i)
        {
            var c = text[i];
            if (IsWord(c))
            {
                var j = i + 1;
                while (j < text.Length && IsWord(text[j]))
                {
                    j++;
                }
                return j;
            }
            if (char.IsWhiteSpace(c))
            {
                var j = i + 1;
                while (j < text.Length && char.IsWhiteSpace(text[j]))
                {
                    j++;
                }
                return j;
            }
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                return i + 2;
            }
            return i + 1;
        }

        static bool IsWord(char c) => c < 128 ? char.IsAsciiLetterOrDigit(c) || c == '_' : char.IsLetterOrDigit(c);
    }

    /// <summary>Sorted hashes and lengths of a line's non-space tokens, for cheap similarity estimates.</summary>
    readonly record struct Signature(int[] Hashes, int[] Lengths, int Total)
    {
        public static Signature Create(string? text)
        {
            text ??= string.Empty;
            var pairs = new List<(int Hash, int Length)>();
            var total = 0;
            var i = 0;
            while (i < text.Length)
            {
                var end = Tokens.TokenEnd(text, i);
                if (!char.IsWhiteSpace(text[i]))
                {
                    pairs.Add((string.GetHashCode(text.AsSpan(i, end - i)), end - i));
                    total += end - i;
                }
                i = end;
            }
            pairs.Sort();
            var hashes = new int[pairs.Count];
            var lengths = new int[pairs.Count];
            for (var k = 0; k < pairs.Count; k++)
            {
                hashes[k] = pairs[k].Hash;
                lengths[k] = pairs[k].Length;
            }
            return new Signature(hashes, lengths, total);
        }
    }
}
