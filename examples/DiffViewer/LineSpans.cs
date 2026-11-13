using System.Collections.Immutable;
using DiffViewer.Core;
using DiffViewer.Highlight;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>
/// What a line marks besides its colours: search matches (Current on the cursor's row), other occurrences of the
/// selected text, and the selection itself as a [Start, End) range of this line.
/// </summary>
internal readonly record struct Marks(string? Query, bool Current, string? Selected, int Start, int End);

/// <summary>
/// Merges syntax tokens, word emphasis, search matches and selected text of one line into ordered, non-overlapping
/// spans.
/// Runs for visible rows only; a line is classified per character on the stack and run-length encoded.
/// </summary>
internal static class LineSpans
{
    const byte None = 0;
    const byte Emphasized = 1;
    const byte Matched = 2;
    const byte Current = 3;
    const byte Occurrence = 4;
    const byte Selected = 5;

    public static TextSpan[]? Build(string text, Highlighting tokens, int line, ImmutableArray<Emphasis> emphasis, Color emphasisColor,
        Marks marks, Palette p)
    {
        int n = text.Length;
        if (n == 0)
            return null;
        Span<byte> fg = n <= 2048 ? stackalloc byte[n] : new byte[n];
        Span<byte> bg = n <= 2048 ? stackalloc byte[n] : new byte[n];
        fg.Clear();
        bg.Clear();
        bool any = false;
        if (line + 1 < tokens.LineStarts.Length)
        {
            for (int t = tokens.LineStarts[line]; t < tokens.LineStarts[line + 1]; t++)
            {
                int start = Math.Min(tokens.Starts[t], n), end = Math.Min(n, start + tokens.Lengths[t]);
                if (end <= start || tokens.Kinds[t] == TokenKind.Plain)
                    continue;
                fg[start..end].Fill((byte)tokens.Kinds[t]);
                any = true;
            }
        }
        if (!emphasis.IsDefaultOrEmpty)
            foreach (var range in emphasis)
            {
                int start = Math.Min(range.Start, n), end = Math.Min(n, range.Start + range.Length);
                if (end <= start)
                    continue;
                bg[start..end].Fill(Emphasized);
                any = true;
            }
        any |= Fill(bg, text, marks.Query, StringComparison.OrdinalIgnoreCase, marks.Current ? Current : Matched);
        any |= Fill(bg, text, marks.Selected, StringComparison.Ordinal, Occurrence);
        int from = Math.Min(marks.Start, n), to = Math.Min(marks.End, n);
        if (to > from)
        {
            bg[from..to].Fill(Selected);
            any = true;
        }
        if (!any)
            return null;
        int count = 0;
        for (int i = 0; i < n; i++)
            if ((fg[i] != 0 || bg[i] != 0) && (i == 0 || fg[i] != fg[i - 1] || bg[i] != bg[i - 1]))
                count++;
        var spans = new TextSpan[count];
        int s = 0;
        for (int i = 0; i < n;)
        {
            int j = i + 1;
            while (j < n && fg[j] == fg[i] && bg[j] == bg[i])
                j++;
            if (fg[i] != 0 || bg[i] != 0)
                spans[s++] = new(i, j, fg[i] == 0 ? null : p.Token((TokenKind)fg[i]), Background: bg[i] switch
                {
                    Emphasized => emphasisColor,
                    Matched => p.Match,
                    Current => p.MatchCurrent,
                    Occurrence => p.Occurrence,
                    Selected => p.Selection,
                    _ => null
                });
            i = j;
        }
        return spans;
    }

    /// <summary>Marks every non-overlapping occurrence of <paramref name="value"/>; false when there is none.</summary>
    static bool Fill(Span<byte> bg, string text, string? value, StringComparison comparison, byte mark)
    {
        if (value is not { Length: > 0 })
            return false;
        bool any = false;
        for (int at = text.IndexOf(value, comparison); at >= 0;
            at = at + value.Length < text.Length ? text.IndexOf(value, at + value.Length, comparison) : -1)
        {
            bg.Slice(at, value.Length).Fill(mark);
            any = true;
        }
        return any;
    }
}
