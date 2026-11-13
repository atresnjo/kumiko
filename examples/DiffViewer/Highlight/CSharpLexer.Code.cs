using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

sealed partial class CSharpLexer
{
    /// <summary>One token of code (at root or inside an interpolation hole).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Code(ReadOnlySpan<char> s, int i)
    {
        var c = s[i];
        if (char.IsWhiteSpace(c))
            return i + 1;
        var next = At(s, i + 1);
        if (c == '/' && next == '/')
        {
            sink.Add(i, s.Length - i, TokenKind.Comment);
            return s.Length;
        }
        if (c == '/' && next == '*')
        {
            Push(new()
            {
                Mode = Mode.Comment
            });
            return Comment(s, i);
        }
        if (c is '"' or '$' or '@' && StringStart(s, i) is var opened and > 0)
            return opened;
        if (c == '@' && IdentStart(next))
            return Identifier(s, i + 1, i);
        if (c == '\'')
            return Character(s, i);
        if (char.IsAsciiDigit(c) || c == '.' && char.IsAsciiDigit(next))
            return Number(s, i);
        if (IdentStart(c))
            return Identifier(s, i, i);
        return Punctuation(s, i, c, next);
    }

    /// <summary>Opens "...", @"...", $"...", $@"...", @$"..." or a raw """ literal; returns 0 when none starts here.</summary>
    int StringStart(ReadOnlySpan<char> s, int i)
    {
        var j = i;
        var dollars = 0;
        var verbatim = false;
        while (j < s.Length && (s[j] == '$' || s[j] == '@' && !verbatim))
        {
            if (s[j] == '$')
                dollars++;
            else
                verbatim = true;
            j++;
        }
        if (At(s, j) != '"')
            return 0;
        var quotes = Run(s, j, '"');
        if (quotes >= 3 && !verbatim)
        {
            sink.Add(i, j + quotes - i, TokenKind.String);
            Push(new()
            {
                Mode = Mode.Raw,
                Quotes = quotes,
                Dollars = dollars,
                Interpolated = dollars > 0
            });
            return j + quotes;
        }
        // "" is an empty regular string; a verbatim one still opens with a single quote.
        sink.Add(i, j + 1 - i, TokenKind.String);
        Push(new()
        {
            Mode = verbatim ? Mode.Verbatim : Mode.Regular,
            Interpolated = dollars > 0
        });
        return j + 1;
    }

    int Character(ReadOnlySpan<char> s, int i)
    {
        var j = i + 1;
        if (At(s, j) == '\\')
        {
            sink.Add(i, 1, TokenKind.String);
            var length = Escape(s, j);
            sink.Add(j, length, TokenKind.Escape);
            j += length;
            var close = At(s, j) == '\'' ? 1 : 0;
            sink.Add(j, close, TokenKind.String);
            prev = Prev.Value;
            return j + close;
        }
        var end = s[j..].IndexOf('\'');
        var stop = end < 0 || end > 2 ? Math.Min(s.Length, j + 1) : j + end + 1;
        sink.Add(i, stop - i, TokenKind.String);
        prev = Prev.Value;
        return stop;
    }

    int Number(ReadOnlySpan<char> s, int i)
    {
        var j = i;
        if (s[j] == '0' && At(s, j + 1) is 'x' or 'X' or 'b' or 'B')
            j += 2;
        while (j < s.Length && (char.IsAsciiHexDigit(s[j]) || s[j] == '_' || s[j] == '.' && char.IsAsciiDigit(At(s, j + 1)) ||
            s[j] is 'e' or 'E' && (char.IsAsciiDigit(At(s, j + 1)) || At(s, j + 1) is '+' or '-')))
            j += s[j] is 'e' or 'E' && At(s, j + 1) is '+' or '-' ? 2 : 1;
        while (j < s.Length && s[j] is 'u' or 'U' or 'l' or 'L' or 'f' or 'F' or 'd' or 'D' or 'm' or 'M')
            j++;
        sink.Add(i, j - i, TokenKind.Number);
        prev = Prev.Value;
        return j;
    }

    /// <summary>A name or keyword; <paramref name="start"/> includes a verbatim '@'.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Identifier(ReadOnlySpan<char> s, int i, int start)
    {
        var j = i;
        while (j < s.Length && IdentPart(s[j]))
            j++;
        var word = s[i..j];
        var after = Skip(s, j);
        var follow = At(s, after);
        var dotted = prev == Prev.Dot;
        var kind = start == i && !dotted ? Keyword(word, s, after, follow) : null;
        var introduced = prev == Prev.TypeIntroducer;
        if (kind is { } keyword)
        {
            sink.Add(start, j - start, keyword);
            if (word is "class" or "struct" or "interface" or "record" or "enum" or "where")
                declaring = true;
            prev = Classify(word) == Word.TypeIntroducer || word is "record" ? Prev.TypeIntroducer
                : keyword == TokenKind.Constant ? Prev.Value : Prev.Statement;
            return j;
        }
        TokenKind name;
        if (attribute)
            name = follow == ':' ? TokenKind.Keyword : TokenKind.Decorator;
        else if (inheritance && !dotted || introduced)
            name = TokenKind.Type;
        else if (follow == '(')
            name = TokenKind.Function;
        else if (follow == '<' && TypeArguments(s, after) is var close and > 0)
            name = At(s, Skip(s, close)) == '(' ? TokenKind.Function : TokenKind.Type;
        else if (dotted)
            name = TokenKind.Property;
        else if (typeArguments > 0)
            name = TokenKind.Type;
        else if (Declares(s, j))
            name = TokenKind.Type;
        else if (word.Length > 1 && word[0] == 'I' && char.IsAsciiLetterUpper(word[1]))
            name = TokenKind.Type;
        else
            name = TokenKind.Plain;
        sink.Add(start, j - start, name);
        prev = Prev.Ident;
        return j;
    }

    /// <summary>The token kind of a keyword in this position, or null when the word is a name here.</summary>
    static TokenKind? Keyword(ReadOnlySpan<char> word, ReadOnlySpan<char> s, int after, char follow) => Classify(word) switch
    {
        Word.Control => TokenKind.Control,
        Word.Constant => TokenKind.Constant,
        Word.BuiltinType => TokenKind.Type,
        Word.Keyword or Word.TypeIntroducer => TokenKind.Keyword,
        // var x, record Point(...), partial class: keywords before a name or keyword, names before '.', '=' or ','.
        Word.Contextual when IdentStart(follow) || follow == '@' => TokenKind.Keyword,
        Word.Contextual when word is "with" && follow == '{' => TokenKind.Keyword,
        Word.Accessor when follow is ';' or '{' || follow == '=' && At(s, after + 1) == '>' => TokenKind.Keyword,
        _ => null
    };

    /// <summary>`Name other`, `Name? other`, `Name[] other`: the name declares the type of what follows.</summary>
    /// <summary>`Name other`, `Name? other`, `Name[] other`: the name declares the type of what follows.</summary>
    static bool Declares(ReadOnlySpan<char> s, int end)
    {
        var j = end;
        while (At(s, j) == '?' || At(s, j) == '[' && At(s, j + 1) == ']')
            j += At(s, j) == '?' ? 1 : 2;
        if (At(s, j) != ' ')
            return false;
        j = Skip(s, j);
        if (!IdentStart(At(s, j)))
            return false;
        var k = j;
        while (k < s.Length && IdentPart(s[k]))
            k++;
        // `x is null`, `item in items`: operator-like keywords after a value are not declarations.
        return s[j..k] is not ("is" or "as" or "in" or "when" or "and" or "or" or "not" or "with" or "switch" or "into" or "on" or "equals");
    }

    /// <summary>The index after a balanced type-argument list starting at '&lt;', or 0 when it is a comparison.</summary>
    static int TypeArguments(ReadOnlySpan<char> s, int i)
    {
        var level = 0;
        for (var j = i; j < s.Length; j++)
        {
            var c = s[j];
            if (c == '<')
                level++;
            else if (c == '>' && --level == 0)
                return j + 1;
            else if (!(IdentPart(c) || c is ',' or ' ' or '.' or '?' or '[' or ']' or '(' or ')' or ':'))
                return 0;
        }
        return 0;
    }

    static int Skip(ReadOnlySpan<char> s, int i)
    {
        while (i < s.Length && s[i] == ' ')
            i++;
        return i;
    }

    int Punctuation(ReadOnlySpan<char> s, int i, char c, char next)
    {
        switch (c)
        {
            case '.':
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Dot;
                return i + 1;
            case '?' when next == '.':
                sink.Add(i, 2, TokenKind.Operator);
                prev = Prev.Dot;
                return i + 2;
            case '[':
                // [Attribute] begins a declaration line or follows another attribute or statement.
                attribute = prev is Prev.Start or Prev.Statement && IdentStart(At(s, Skip(s, i + 1)));
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Operator;
                return i + 1;
            case ']':
                prev = attribute ? Prev.Statement : Prev.Close;
                attribute = false;
                sink.Add(i, 1, TokenKind.Punctuation);
                return i + 1;
            case '(':
                attribute = false;
                inheritance = false;
                var operand = prev == Prev.TypeIntroducer;
                Open(s, i, TokenKind.Punctuation);
                // typeof(Name), sizeof(Name): the operand is a type.
                if (operand)
                    prev = Prev.TypeIntroducer;
                return i + 1;
            case '{':
                declaring = inheritance = false;
                if (depth > 0 && Top.Mode == Mode.Hole)
                    Top.Braces++;
                Open(s, i, TokenKind.Punctuation);
                prev = Prev.Statement;
                return i + 1;
            case '}':
                if (depth > 0 && Top.Mode == Mode.Hole && Top.Braces == 0)
                {
                    var close = Math.Min(Run(s, i, '}'), Math.Max(1, Top.Dollars));
                    sink.Add(i, close, TokenKind.Interpolation);
                    depth--;
                    return i + close;
                }
                if (depth > 0 && Top.Mode == Mode.Hole)
                    Top.Braces--;
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Statement;
                return i + 1;
            case ';':
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Statement;
                typeArguments = 0;
                declaring = inheritance = false;
                return i + 1;
            case ':' when declaring && typeArguments == 0:
                // class A : Base, IFace and where T : IFace list types.
                inheritance = true;
                sink.Add(i, 1, TokenKind.Operator);
                prev = Prev.Operator;
                return i + 1;
            case ')' or ',':
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = c == ')' ? Prev.Close : Prev.Operator;
                return i + 1;
            case '<' when prev == Prev.Ident && TypeArguments(s, i) > 0:
                typeArguments++;
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Operator;
                return i + 1;
            case '>' when typeArguments > 0:
                typeArguments--;
                sink.Add(i, 1, TokenKind.Punctuation);
                prev = Prev.Close;
                return i + 1;
        }
        var j = i + 1;
        while (j < s.Length && s[j] is '=' or '>' or '<' or '&' or '|' or '+' or '-' or '*' or '/' or '%' or '!' or '?' or '^' or '~' or ':' &&
            j - i < 4 && !(s[j] == '/' && At(s, j + 1) is '/' or '*'))
            j++;
        sink.Add(i, j - i, TokenKind.Operator);
        prev = Prev.Operator;
        inheritance = false;
        return j;
    }

    void Open(ReadOnlySpan<char> s, int i, TokenKind kind)
    {
        sink.Add(i, 1, kind);
        prev = Prev.Start;
    }
}
