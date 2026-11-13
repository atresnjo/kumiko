using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DiffViewer.Highlight;

/// <summary>Growable pooled token storage. Merges a token into the previous one when both touch and share a kind.</summary>
sealed class TokenSink
{
    readonly int[] lineStarts;
    readonly bool[]? keep;
    int[] starts;
    int[] lengths;
    TokenKind[] kinds;
    int count;
    int line;
    int lineFirst;

    /// <summary>Lines <paramref name="keep"/> leaves unmarked get no tokens; the lexer still reads them for its state.</summary>
    public TokenSink(int lines, int estimate, bool[]? keep = null)
    {
        lineStarts = new int[lines + 1];
        this.keep = keep;
        var capacity = Math.Max(64, estimate);
        starts = ArrayPool<int>.Shared.Rent(capacity);
        lengths = ArrayPool<int>.Shared.Rent(capacity);
        kinds = ArrayPool<TokenKind>.Shared.Rent(capacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Add(int start, int length, TokenKind kind)
    {
        if (length <= 0 || kind == TokenKind.Plain || keep is not null && !keep[line])
            return;
        var last = count - 1;
        if (last >= lineFirst && kinds[last] == kind && starts[last] + lengths[last] == start)
        {
            lengths[last] += length;
            return;
        }
        if (count == starts.Length)
            Grow();
        starts[count] = start;
        lengths[count] = length;
        kinds[count] = kind;
        count++;
    }

    public void EndLine()
    {
        line++;
        lineStarts[line] = count;
        lineFirst = count;
    }

    void Grow()
    {
        var size = starts.Length * 2;
        var nextStarts = ArrayPool<int>.Shared.Rent(size);
        var nextLengths = ArrayPool<int>.Shared.Rent(size);
        var nextKinds = ArrayPool<TokenKind>.Shared.Rent(size);
        starts.AsSpan(0, count).CopyTo(nextStarts);
        lengths.AsSpan(0, count).CopyTo(nextLengths);
        kinds.AsSpan(0, count).CopyTo(nextKinds);
        Return();
        starts = nextStarts;
        lengths = nextLengths;
        kinds = nextKinds;
    }

    void Return()
    {
        ArrayPool<int>.Shared.Return(starts);
        ArrayPool<int>.Shared.Return(lengths);
        ArrayPool<TokenKind>.Shared.Return(kinds);
    }

    /// <summary>Copies the tokens into exactly sized immutable arrays and returns the pooled buffers.</summary>
    public Highlighting Build()
    {
        while (line < lineStarts.Length - 1)
            EndLine();
        var finalStarts = starts.AsSpan(0, count).ToArray();
        var finalLengths = lengths.AsSpan(0, count).ToArray();
        var finalKinds = kinds.AsSpan(0, count).ToArray();
        Return();
        starts = [];
        lengths = [];
        kinds = [];
        return new Highlighting(ImmutableCollectionsMarshal.AsImmutableArray(lineStarts),
            ImmutableCollectionsMarshal.AsImmutableArray(finalStarts),
            ImmutableCollectionsMarshal.AsImmutableArray(finalLengths),
            ImmutableCollectionsMarshal.AsImmutableArray(finalKinds));
    }
}
