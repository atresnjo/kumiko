using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

public static class Highlighter
{
    /// <summary>
    /// Tokenizes a whole file given as lines without terminators. State carries from line to line; when
    /// <paramref name="keep"/> is given, only the tokens of lines it marks are stored.
    /// </summary>
    public static Highlighting Tokenize(Language language, IReadOnlyList<string> lines, bool[]? keep = null)
    {
        if (language == Language.Plain || lines.Count == 0)
            return Highlighting.None(lines.Count);
        var chars = 0L;
        for (var i = 0; i < lines.Count; i++)
            if (keep is null || keep[i])
                chars += lines[i].Length;
        var sink = new TokenSink(lines.Count, (int)Math.Min(chars / 3, 1 << 26), keep);
        if (language == Language.Json)
            JsonLexer.Run(lines, sink);
        else if (language == Language.CSharp)
            new CSharpLexer(sink).Run(lines);
        else
            new ScriptLexer(language, sink).Run(lines);
        return sink.Build();
    }
}

/// <summary>What is open at the lexer position. Frames up to JsxExpr lex script; the others own their text.</summary>
enum Frame : byte
{
    Root,
    Paren,
    Bracket,
    Block,
    Object,
    TypeBody,
    Module,
    Interp,
    JsxExpr,
    Template,
    Comment,
    SingleString,
    DoubleString,
    JsxTag,
    JsxClose,
    JsxChildren,
    JsxSingle,
    JsxDouble
}

/// <summary>The previous significant script token, enough to tell regex from division and blocks from objects.</summary>
enum Prev : byte
{
    Start,
    Value,
    Ident,
    CloseParen,
    TypeClose,
    Dot,
    Arrow,
    Operator,
    Star,
    Open,
    Colon,
    Question,
    Semicolon,
    BlockOpen,
    BlockClose,
    KeywordBlock,
    KeywordModule,
    Keyword
}

/// <summary>Single-pass TypeScript/JavaScript/JSX lexer over line spans. No per-token allocation.</summary>
sealed partial class ScriptLexer(Language language, TokenSink sink)
{
    readonly bool typed = language is Language.TypeScript or Language.Tsx;
    readonly bool jsx = language is Language.Tsx or Language.Jsx;
    Frame[] stack = new Frame[32];
    int[] saved = new int[32];
    int depth;
    Prev prev = Prev.Start;
    int ternary;
    bool expectType;
    bool typeChain;
    bool expectFunction;
    bool bodyPending;
    bool typeAlias;
    bool declaring;
    int angle;
    int genericAt = -1;

    Frame Top => stack[depth];
    static bool IsScript(Frame frame) => frame <= Frame.JsxExpr;

    bool RegexOk => prev is not (Prev.Value or Prev.Ident or Prev.CloseParen or Prev.TypeClose or Prev.Dot);

    static bool IsValue(Prev value) => value is Prev.Value or Prev.Ident or Prev.CloseParen or Prev.TypeClose;

    public void Run(IReadOnlyList<string> lines)
    {
        for (var n = 0; n < lines.Count; n++)
        {
            var text = lines[n].AsSpan();
            if (n == 0 && text.StartsWith("#!"))
                sink.Add(0, text.Length, TokenKind.Comment);
            else
                Line(text);
            sink.EndLine();
        }
    }

    void Push(Frame frame)
    {
        if (depth + 1 == stack.Length)
        {
            Array.Resize(ref stack, stack.Length * 2);
            Array.Resize(ref saved, saved.Length * 2);
        }
        depth++;
        stack[depth] = frame;
        saved[depth] = ternary;
        ternary = 0;
    }

    void Pop()
    {
        if (depth == 0)
            return;
        ternary = saved[depth];
        depth--;
    }

    void Emit(int start, int length, TokenKind kind) => sink.Add(start, length, kind);

    static char At(ReadOnlySpan<char> s, int i) => (uint)i < (uint)s.Length ? s[i] : '\0';

    static int SkipSpaces(ReadOnlySpan<char> s, int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t'))
            i++;
        return i;
    }

    static bool IsDigit(char c) => (uint)(c - '0') <= 9;

    static bool IsIdentStart(char c)
    {
        if (c < 128)
            return (uint)((c | 0x20) - 'a') <= 'z' - 'a' || c == '_' || c == '$';
        return char.IsLetter(c);
    }

    static bool IsIdentPart(char c)
    {
        if (c < 128)
            return (uint)((c | 0x20) - 'a') <= 'z' - 'a' || (uint)(c - '0') <= 9 || c == '_' || c == '$';
        return char.IsLetterOrDigit(c);
    }

    static int IdentEnd(ReadOnlySpan<char> s, int i)
    {
        while (i < s.Length && IsIdentPart(s[i]))
            i++;
        return i;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void Line(ReadOnlySpan<char> s)
    {
        angle = 0;
        genericAt = -1;
        var i = 0;
        if (Top is Frame.SingleString or Frame.DoubleString)
        {
            var quote = Top == Frame.SingleString ? '\'' : '"';
            Pop();
            i = ScanString(s, 0, 0, quote);
        }
        while (i < s.Length)
        {
            i = Top switch
            {
                Frame.Template => TemplateStep(s, i),
                Frame.Comment => CommentStep(s, i),
                Frame.JsxTag or Frame.JsxClose => TagStep(s, i),
                Frame.JsxChildren => ChildrenStep(s, i),
                Frame.JsxSingle or Frame.JsxDouble => JsxStringStep(s, i),
                _ => ScriptStep(s, i)
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int ScriptStep(ReadOnlySpan<char> s, int i)
    {
        var c = s[i];
        if (c == ' ' || c == '\t')
            return SkipSpaces(s, i);
        if (IsIdentStart(c))
            return Word(s, i);
        if (IsDigit(c) || c == '.' && IsDigit(At(s, i + 1)))
            return Number(s, i);
        if (c == '/' && At(s, i + 1) == '/')
        {
            Emit(i, s.Length - i, TokenKind.Comment);
            return s.Length;
        }
        if (c == '/' && At(s, i + 1) == '*')
            return OpenComment(s, i);
        var typePosition = expectType;
        if (c == '.' || (c == '|' || c == '&') && At(s, i + 1) != c && At(s, i + 1) != '=')
        {
            expectType = typeChain;
            typeChain = false;
        }
        else if (c != '[' && c != ']')
        {
            expectType = false;
            typeChain = false;
        }
        switch (c)
        {
            case '"':
            case '\'':
                return ScanString(s, i, i + 1, c);
            case '`':
                Emit(i, 1, TokenKind.String);
                Push(Frame.Template);
                return i + 1;
            case '(':
                expectFunction = false;
                Push(Frame.Paren);
                return Punctuation(i, Prev.Open);
            case ')':
                if (Top == Frame.Paren)
                    Pop();
                return Punctuation(i, Prev.CloseParen);
            case '[':
                Push(Frame.Bracket);
                return Punctuation(i, Prev.Open);
            case ']':
                if (Top == Frame.Bracket)
                    Pop();
                return Punctuation(i, Prev.Value);
            case '{':
                return OpenBrace(i);
            case '}':
                return CloseBrace(i);
            case ',':
                return Punctuation(i, Prev.Open);
            case ';':
                ternary = 0;
                bodyPending = false;
                typeAlias = false;
                declaring = false;
                return Punctuation(i, Prev.Semicolon);
            case ':':
                return Colon(i);
            case '@':
                return Decorator(s, i);
            case '#':
                return PrivateName(s, i);
            case '.':
                if (At(s, i + 1) == '.' && At(s, i + 2) == '.')
                {
                    Emit(i, 3, TokenKind.Operator);
                    prev = Prev.Operator;
                    return i + 3;
                }
                return Punctuation(i, Prev.Dot);
            case '/':
                if (RegexOk)
                {
                    var end = ScanRegex(s, i);
                    if (end > 0)
                    {
                        Emit(i, end - i, TokenKind.Regex);
                        prev = Prev.Value;
                        return end;
                    }
                }
                return Operator(s, i);
            case '<':
                return Less(s, i, typePosition);
            case '>':
                if (angle > 0)
                {
                    angle--;
                    Emit(i, 1, TokenKind.Punctuation);
                    prev = Prev.TypeClose;
                    typeChain = true;
                    return i + 1;
                }
                return Operator(s, i);
            case '=' or '!' or '+' or '-' or '*' or '%' or '&' or '|' or '^' or '~' or '?':
                return Operator(s, i);
            default:
                return i + 1;
        }
    }

    int Punctuation(int i, Prev next)
    {
        Emit(i, 1, TokenKind.Punctuation);
        prev = next;
        return i + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int OpenBrace(int i)
    {
        var body = bodyPending && prev is Prev.Ident or Prev.TypeClose or Prev.CloseParen or Prev.Keyword or Prev.Value;
        var frame = prev switch
        {
            _ when body => Frame.TypeBody,
            Prev.Start or Prev.Semicolon or Prev.BlockOpen or Prev.BlockClose or Prev.CloseParen or Prev.Arrow or Prev.KeywordBlock or
                Prev.Ident or Prev.TypeClose => Frame.Block,
            Prev.KeywordModule => Frame.Module,
            _ => Frame.Object
        };
        if (body)
            bodyPending = false;
        if (frame != Frame.Object)
            declaring = false;
        Push(frame);
        return Punctuation(i, Prev.BlockOpen);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int CloseBrace(int i)
    {
        while (Top is Frame.Paren or Frame.Bracket)
            Pop();
        var top = Top;
        if (top == Frame.Interp)
        {
            Pop();
            Emit(i, 1, TokenKind.Interpolation);
            return i + 1;
        }
        if (top == Frame.JsxExpr)
        {
            Pop();
            Emit(i, 1, TokenKind.Punctuation);
            return i + 1;
        }
        if (top != Frame.Root)
            Pop();
        return Punctuation(i, top == Frame.Object ? Prev.Value : Prev.BlockClose);
    }

    int Colon(int i)
    {
        if (ternary > 0)
        {
            ternary--;
            Emit(i, 1, TokenKind.Operator);
            prev = Prev.Colon;
            return i + 1;
        }
        var declared = declaring && Top is not (Frame.Object or Frame.Bracket);
        if (typed && (Top is Frame.Paren or Frame.TypeBody || prev == Prev.CloseParen || angle > 0 || declared))
            expectType = true;
        return Punctuation(i, Prev.Colon);
    }

    int Less(ReadOnlySpan<char> s, int i, bool typePosition)
    {
        if (angle > 0 || genericAt == i)
        {
            genericAt = -1;
            angle++;
            return Punctuation(i, Prev.Open);
        }
        if (RegexOk)
        {
            if (jsx && !typePosition && !typeAlias && Top != Frame.TypeBody && IsJsxStart(s, i))
                return OpenTag(s, i);
            if (typed && ScanGeneric(s, i) > 0)
            {
                angle++;
                return Punctuation(i, Prev.Open);
            }
        }
        return Operator(s, i);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Operator(ReadOnlySpan<char> s, int i)
    {
        var length = OperatorLength(s, i);
        var c = s[i];
        Emit(i, length, TokenKind.Operator);
        if (length == 1 && c == '?')
        {
            if (At(s, i + 1) is ':' or ',' or ')')
            {
                prev = Prev.Operator;
            }
            else
            {
                ternary++;
                prev = Prev.Question;
            }
        }
        else if (length == 2 && c == '?' && s[i + 1] == '.')
        {
            prev = Prev.Dot;
        }
        else if (length == 1 && c == '=')
        {
            declaring = false;
            prev = Prev.Operator;
        }
        else if (length == 2 && c == '=' && s[i + 1] == '>')
        {
            prev = Prev.Arrow;
        }
        else if (length == 2 && (c == '+' || c == '-') && s[i + 1] == c || length == 1 && c == '!')
        {
            prev = IsValue(prev) ? Prev.Value : Prev.Operator;
        }
        else
        {
            prev = length == 1 && c == '*' ? Prev.Star : Prev.Operator;
        }
        return i + length;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int OperatorLength(ReadOnlySpan<char> s, int i)
    {
        var c = s[i];
        var n = At(s, i + 1);
        var m = At(s, i + 2);
        switch (c)
        {
            case '=':
                if (n == '>')
                    return 2;
                return n == '=' ? m == '=' ? 3 : 2 : 1;
            case '!':
                return n == '=' ? m == '=' ? 3 : 2 : 1;
            case '<':
                if (n == '<')
                    return m == '=' ? 3 : 2;
                return n == '=' ? 2 : 1;
            case '>':
                if (n == '>' && m == '>')
                    return At(s, i + 3) == '=' ? 4 : 3;
                if (n == '>')
                    return m == '=' ? 3 : 2;
                return n == '=' ? 2 : 1;
            case '&' or '|' or '*':
                if (n == c)
                    return m == '=' ? 3 : 2;
                return n == '=' ? 2 : 1;
            case '?':
                if (n == '?')
                    return m == '=' ? 3 : 2;
                return n == '.' && !IsDigit(m) ? 2 : 1;
            case '+' or '-':
                return n == c || n == '=' ? 2 : 1;
            case '%' or '^' or '/':
                return n == '=' ? 2 : 1;
            default:
                return 1;
        }
    }

    int Decorator(ReadOnlySpan<char> s, int i)
    {
        if (!IsIdentStart(At(s, i + 1)))
            return i + 1;
        var j = IdentEnd(s, i + 1);
        while (At(s, j) == '.' && IsIdentStart(At(s, j + 1)))
            j = IdentEnd(s, j + 1);
        Emit(i, j - i, TokenKind.Decorator);
        prev = Prev.Ident;
        return j;
    }

    int PrivateName(ReadOnlySpan<char> s, int i)
    {
        if (!IsIdentStart(At(s, i + 1)))
            return i + 1;
        var j = IdentEnd(s, i + 1);
        var call = At(s, SkipSpaces(s, j)) == '(';
        Emit(i, j - i, call ? TokenKind.Function : TokenKind.Property);
        prev = Prev.Ident;
        return j;
    }
}
