using DiffViewer.Highlight;
using Kumiko.UI;

namespace DiffViewer;

/// <summary>
/// Exact colours for one appearance. Syntax tones follow GitHub's light and dark themes; light reds and purples are
/// one step darker so every token keeps 4.5:1 contrast on changed lines and word emphasis.
/// </summary>
internal sealed record Palette(
    Color Canvas,
    Color Surface,
    Color Raised,
    Color Border,
    Color Text,
    Color Muted,
    Color Faint,
    Color Accent,
    Color AccentSoft,
    Color Hover,
    Color AddedLine,
    Color AddedGutter,
    Color AddedWord,
    Color AddedText,
    Color RemovedLine,
    Color RemovedGutter,
    Color RemovedWord,
    Color RemovedText,
    Color HunkLine,
    Color HunkText,
    Color Match,
    Color MatchCurrent,
    Color Occurrence,
    Color Selection,
    Color Empty,
    Color[] Syntax)
{
    public static Palette Light
    {
        get;
    } = new(
        Canvas: Color.Hex(0xffffff),
        Surface: Color.Hex(0xf6f8fa),
        Raised: Color.Hex(0xffffff),
        Border: Color.Hex(0xd1d9e0),
        Text: Color.Hex(0x1f2328),
        Muted: Color.Hex(0x59636e),
        Faint: Color.Hex(0x818b98),
        Accent: Color.Hex(0x0969da),
        AccentSoft: Color.Hex(0xddf4ff),
        Hover: Color.Hex(0xeff2f5),
        AddedLine: Color.Hex(0xe6ffec),
        AddedGutter: Color.Hex(0xd1f8d9),
        AddedWord: Color.Hex(0xabf2bc),
        AddedText: Color.Hex(0x116329),
        RemovedLine: Color.Hex(0xffebe9),
        RemovedGutter: Color.Hex(0xffdcd7),
        RemovedWord: Color.Hex(0xffd6d3),
        RemovedText: Color.Hex(0xb31d28),
        HunkLine: Color.Hex(0xddf4ff),
        HunkText: Color.Hex(0x0550ae),
        Match: Color.Hex(0xfff8c5),
        MatchCurrent: Color.Hex(0xffe58f),
        Occurrence: Color.Hex(0xe4e2ff),
        Selection: Color.Hex(0xcfe0ff),
        Empty: Color.Hex(0xf6f8fa),
        Syntax: Tones(0x1f2328, 0x59636e, 0x0a3069, 0x0550ae, 0x0550ae, 0xb31d28, 0xb31d28, 0x0550ae, 0x953800, 0x6639ba, 0x0550ae,
            0xb31d28, 0x59636e, 0x116329, 0x6639ba, 0x116329, 0x0550ae, 0xb31d28));

    public static Palette Dark
    {
        get;
    } = new(
        Canvas: Color.Hex(0x0d1117),
        Surface: Color.Hex(0x151b23),
        Raised: Color.Hex(0x1a212b),
        Border: Color.Hex(0x30363d),
        Text: Color.Hex(0xe6edf3),
        Muted: Color.Hex(0x9198a1),
        Faint: Color.Hex(0x6e7681),
        Accent: Color.Hex(0x4493f8),
        AccentSoft: Color.Hex(0x152a45),
        Hover: Color.Hex(0x1c232c),
        AddedLine: Color.Hex(0x12261e),
        AddedGutter: Color.Hex(0x173524),
        AddedWord: Color.Hex(0x143521),
        AddedText: Color.Hex(0x3fb950),
        RemovedLine: Color.Hex(0x25171c),
        RemovedGutter: Color.Hex(0x3a1d22),
        RemovedWord: Color.Hex(0x4c1c22),
        RemovedText: Color.Hex(0xff7b72),
        HunkLine: Color.Hex(0x121d2f),
        HunkText: Color.Hex(0x8db1e8),
        Match: Color.Hex(0x231e0d),
        MatchCurrent: Color.Hex(0x36290a),
        Occurrence: Color.Hex(0x24213f),
        Selection: Color.Hex(0x17304f),
        Empty: Color.Hex(0x151b23),
        Syntax: Tones(0xe6edf3, 0x9198a1, 0xa5d6ff, 0x79c0ff, 0x79c0ff, 0xff7b72, 0xff7b72, 0x79c0ff, 0xffa657, 0xd2a8ff, 0x79c0ff,
            0xff7b72, 0xb1bac4, 0x7ee787, 0xd2a8ff, 0x7ee787, 0x79c0ff, 0xff7b72));

    /// <summary>Colours indexed by <see cref="TokenKind"/>, in declaration order.</summary>
    static Color[] Tones(params uint[] values) => [.. values.Select(Color.Hex)];

    public Color Token(TokenKind kind) => Syntax[(int)kind];
}
