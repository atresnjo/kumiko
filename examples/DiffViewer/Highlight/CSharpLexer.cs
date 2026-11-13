using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

/// <summary>
/// Single-pass C# lexer over line spans, without regular expressions or per-token allocation. Block comments,
/// verbatim and raw strings and interpolation holes carry across lines; regular strings end with their line.
/// Type and call positions are recognised from neighbouring tokens, as an editor without semantics would.
/// </summary>
sealed partial class CSharpLexer(TokenSink sink)
{
    enum Mode : byte
    {
        Code,
        Comment,
        Regular,
        Verbatim,
        Raw,
        Hole
    }

    /// <summary>An open construct. Quotes and Dollars size raw literals; Braces counts nesting inside a hole.</summary>
    struct Frame
    {
        public Mode Mode;
        public bool Interpolated;
        public int Quotes, Dollars, Braces;
    }

    /// <summary>The previous significant token, for attribute, type and call positions.</summary>
    enum Prev : byte
    {
        Start,
        Statement,
        Dot,
        Ident,
        TypeIntroducer,
        Value,
        Close,
        Operator
    }

    Frame[] stack = new Frame[16];
    int depth;
    Prev prev = Prev.Start;
    /// <summary>Open '&lt;' of type arguments; names inside them are types.</summary>
    int typeArguments;
    /// <summary>Inside an attribute's brackets, before its arguments.</summary>
    bool attribute;
    /// <summary>A type declaration or constraint is open; after its ':' names are base types.</summary>
    bool declaring, inheritance;

    ref Frame Top => ref stack[depth];

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Run(IReadOnlyList<string> lines)
    {
        stack[0] = new()
        {
            Mode = Mode.Code
        };
        for (var n = 0; n < lines.Count; n++)
        {
            var s = lines[n].AsSpan();
            var i = 0;
            if (depth == 0 && Directive(s))
            {
                sink.EndLine();
                continue;
            }
            while (i < s.Length)
                i = Top.Mode switch
                {
                    Mode.Comment => Comment(s, i),
                    Mode.Regular or Mode.Verbatim or Mode.Raw => Text(s, i),
                    _ => Code(s, i)
                };
            // A regular string cannot continue on the next line; recover rather than colour the rest of the file.
            while (depth > 0 && Top.Mode == Mode.Regular)
                depth--;
            typeArguments = 0;
            attribute = false;
            sink.EndLine();
        }
    }

    void Push(Frame frame)
    {
        if (++depth == stack.Length)
            Array.Resize(ref stack, stack.Length * 2);
        stack[depth] = frame;
    }

    static char At(ReadOnlySpan<char> s, int i) => (uint)i < (uint)s.Length ? s[i] : '\0';
    static bool IdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || c > 127 && char.IsLetter(c);
    static bool IdentPart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_' || c > 127 && char.IsLetterOrDigit(c);

    /// <summary>#if, #region and friends own their line; region names and pragma text read as comments.</summary>
    bool Directive(ReadOnlySpan<char> s)
    {
        var i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
        if (At(s, i) != '#')
            return false;
        var start = i++;
        while (i < s.Length && char.IsAsciiLetter(s[i]))
            i++;
        sink.Add(start, i - start, TokenKind.Keyword);
        var word = s[(start + 1)..i];
        if (TextDirective(word))
            sink.Add(i, s.Length - i, TokenKind.Comment);
        else
            for (var j = i; j < s.Length; j++)
            {
                if (!IdentStart(s[j]))
                    continue;
                var end = j;
                while (end < s.Length && IdentPart(s[end]))
                    end++;
                sink.Add(j, end - j, TokenKind.Constant);
                j = end;
            }
        return true;
    }

    int Comment(ReadOnlySpan<char> s, int i)
    {
        var end = s[i..].IndexOf("*/");
        var length = end < 0 ? s.Length - i : end + 2;
        sink.Add(i, length, TokenKind.Comment);
        if (end >= 0)
            depth--;
        return i + length;
    }

    /// <summary>String contents of the innermost literal: escapes, interpolation holes and the closing quotes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Text(ReadOnlySpan<char> s, int i)
    {
        ref var f = ref Top;
        var start = i;
        while (i < s.Length)
        {
            var c = s[i];
            if (f.Mode == Mode.Regular && c == '\\')
            {
                sink.Add(start, i - start, TokenKind.String);
                var length = Escape(s, i);
                sink.Add(i, length, TokenKind.Escape);
                i += length;
                start = i;
                continue;
            }
            if (c == '"')
            {
                if (f.Mode == Mode.Verbatim && At(s, i + 1) == '"')
                {
                    i += 2;
                    continue;
                }
                var quotes = f.Mode == Mode.Raw ? Run(s, i, '"') : 1;
                if (quotes >= (f.Mode == Mode.Raw ? f.Quotes : 1))
                {
                    var close = f.Mode == Mode.Raw ? f.Quotes : 1;
                    sink.Add(start, i + close - start, TokenKind.String);
                    depth--;
                    prev = Prev.Value;
                    return i + close;
                }
                i += quotes;
                continue;
            }
            if (f.Interpolated && c == '{')
            {
                var open = Run(s, i, '{');
                var need = f.Mode == Mode.Raw ? Math.Max(1, f.Dollars) : 1;
                if (f.Mode != Mode.Raw && open >= 2)
                {
                    i += 2;
                    continue;
                }
                if (open >= need)
                {
                    // Extra braces before a raw hole are literal text.
                    var literal = open - need;
                    sink.Add(start, i + literal - start, TokenKind.String);
                    sink.Add(i + literal, need, TokenKind.Interpolation);
                    Push(new()
                    {
                        Mode = Mode.Hole,
                        Dollars = need
                    });
                    prev = Prev.Start;
                    return i + open;
                }
            }
            i++;
        }
        sink.Add(start, i - start, TokenKind.String);
        return i;
    }

    static int Run(ReadOnlySpan<char> s, int i, char c)
    {
        var n = 0;
        while (i + n < s.Length && s[i + n] == c)
            n++;
        return n;
    }

    static int Escape(ReadOnlySpan<char> s, int i) => At(s, i + 1) switch
    {
        'u' => Math.Min(6, s.Length - i),
        'U' => Math.Min(10, s.Length - i),
        'x' => Hex(s, i + 2) + 2,
        '\0' => 1,
        _ => 2
    };

    static int Hex(ReadOnlySpan<char> s, int i)
    {
        var n = 0;
        while (n < 4 && char.IsAsciiHexDigit(At(s, i + n)))
            n++;
        return n;
    }
}
