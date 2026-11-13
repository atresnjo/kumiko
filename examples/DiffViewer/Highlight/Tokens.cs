using System.Collections.Immutable;

namespace DiffViewer.Highlight;

/// <summary>Syntax classes the palette colours. Plain text between tokens uses the default foreground.</summary>
public enum TokenKind : byte
{
    Plain,
    Comment,
    String,
    Escape,
    Number,
    Keyword,
    Control,
    Constant,
    Type,
    Function,
    Property,
    Operator,
    Punctuation,
    Regex,
    Decorator,
    Tag,
    Attribute,
    Interpolation
}

/// <summary>
/// Tokens of one text grouped by line. Line i owns tokens [LineStarts[i], LineStarts[i + 1]); each token covers
/// Lengths[t] UTF-16 units from column Starts[t] of that line. Tokens are ordered, non-overlapping and inside the
/// line. Plain text is omitted.
/// </summary>
public sealed record Highlighting(ImmutableArray<int> LineStarts, ImmutableArray<int> Starts, ImmutableArray<int> Lengths,
    ImmutableArray<TokenKind> Kinds)
{
    public static Highlighting None(int lines) => new([.. new int[lines + 1]], [], [], []);
    public int Lines => LineStarts.Length - 1;
}
