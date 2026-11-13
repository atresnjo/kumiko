using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

enum WordClass : byte
{
    Identifier,
    Declaration,
    Contextual,
    Control,
    Constant,
    Builtin
}

sealed partial class ScriptLexer
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static WordClass Classify(ReadOnlySpan<char> word) => word switch
    {
        "const" or "let" or "var" or "function" or "class" or "interface" or "enum" or "import" or "export" or "default" or
            "extends" => WordClass.Declaration,
        "type" or "namespace" or "module" or "declare" or "from" or "as" or "implements" or "public" or "private" or
            "protected" or "readonly" or "static" or "abstract" or "async" or "get" or "set" or "keyof" or "infer" or "is" or
            "asserts" or "satisfies" or "unique" or "accessor" or "override" or "of" => WordClass.Contextual,
        "if" or "else" or "for" or "while" or "do" or "switch" or "case" or "break" or "continue" or "return" or "throw" or
            "try" or "catch" or "finally" or "new" or "delete" or "typeof" or "instanceof" or "in" or "void" or "await" or
            "yield" or "with" or "debugger" => WordClass.Control,
        "true" or "false" or "null" or "undefined" or "this" or "super" or "NaN" or "Infinity" => WordClass.Constant,
        "string" or "number" or "boolean" or "any" or "unknown" or "never" or "object" or "symbol" or "bigint" => WordClass.Builtin,
        _ => WordClass.Identifier
    };

    /// <summary>Contextual keywords act as keywords only when something keyword-like follows them on the line.</summary>
    static bool ContextualFollows(ReadOnlySpan<char> word, char next)
    {
        if (next == '\0' || IsIdentStart(next))
            return true;
        if (next is '{' or '[' or '*' or '\'' or '"' or '`' or '#' or '@')
            return true;
        return next is '(' or '<' && word is "async";
    }

    int Word(ReadOnlySpan<char> s, int i)
    {
        var j = IdentEnd(s, i + 1);
        var kind = Identify(s, i, j);
        Emit(i, j - i, kind);
        return j;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    TokenKind Identify(ReadOnlySpan<char> s, int i, int j)
    {
        var word = s[i..j];
        var next = SkipSpaces(s, j);
        var nc = At(s, next);
        var generic = typed && angle == 0 && At(s, j) == '<' ? ScanGeneric(s, j) : -1;
        if (generic > 0)
            genericAt = j;
        var call = nc == '(' || nc == '`' && next == j || generic > 0 && At(s, SkipSpaces(s, generic)) == '(' ||
            nc == '?' && At(s, next + 1) == '.' && At(s, next + 2) == '(';
        var before = prev;
        var afterDot = before == Prev.Dot;
        var chained = expectType;
        expectType = false;
        typeChain = false;
        if (afterDot && !chained)
        {
            prev = Prev.Ident;
            return call ? TokenKind.Function : TokenKind.Property;
        }
        if (Top is Frame.Object or Frame.TypeBody or Frame.Module && ternary == 0 && !afterDot &&
            (nc == ':' || nc == '?' && At(s, next + 1) == ':'))
        {
            prev = Prev.Ident;
            return call ? TokenKind.Function : TokenKind.Property;
        }
        if (nc == '(' && Top is Frame.Object or Frame.TypeBody &&
            before is Prev.BlockOpen or Prev.Semicolon or Prev.BlockClose or Prev.Open or Prev.Keyword)
        {
            prev = Prev.Ident;
            return TokenKind.Function;
        }
        if (!afterDot)
        {
            var keyword = Keyword(word, nc);
            if (keyword != TokenKind.Plain)
                return keyword;
        }
        prev = Prev.Ident;
        if (Top == Frame.TypeBody && IsMemberName(s, before, next, nc))
            return TokenKind.Property;
        if (expectFunction)
        {
            expectFunction = false;
            return TokenKind.Function;
        }
        if (chained && IsWord(s, next, "is"))
            return TokenKind.Plain;
        if (chained || angle > 0)
        {
            typeChain = true;
            return TokenKind.Type;
        }
        if (call)
            return TokenKind.Function;
        if ((uint)(word[0] - 'A') <= 'Z' - 'A')
            return TokenKind.Type;
        return TokenKind.Plain;
    }

    static bool IsWord(ReadOnlySpan<char> s, int at, ReadOnlySpan<char> word) =>
        s[at..].StartsWith(word) && !IsIdentPart(At(s, at + word.Length));

    /// <summary>A class field or enum member: a name at member start followed by an initializer or separator.</summary>
    static bool IsMemberName(ReadOnlySpan<char> s, Prev before, int next, char nc)
    {
        if (before is not (Prev.BlockOpen or Prev.Semicolon or Prev.BlockClose or Prev.Keyword or Prev.CloseParen or Prev.Ident or
            Prev.Open))
            return false;
        if (nc == '=')
            return At(s, next + 1) is not ('=' or '>');
        return nc is ';' or ',' or '}' or '\0' || nc == '!' && At(s, next + 1) == ':';
    }

    /// <summary>Classifies a keyword and updates the parse hints it implies, or returns Plain for an identifier.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    TokenKind Keyword(ReadOnlySpan<char> word, char next)
    {
        if (word.Length < 2 || word.Length > 10)
            return TokenKind.Plain;
        switch (Classify(word))
        {
            case WordClass.Control:
                typeAlias &= word is not "return";
                prev = word is "else" or "do" or "try" or "finally" ? Prev.KeywordBlock : Prev.Keyword;
                expectType = word is "new" or "instanceof";
                return TokenKind.Control;
            case WordClass.Constant:
                prev = Prev.Value;
                return TokenKind.Constant;
            case WordClass.Builtin:
                prev = Prev.Ident;
                typeChain = true;
                return TokenKind.Type;
            case WordClass.Declaration:
                prev = word is "import" or "export" ? Prev.KeywordModule : Prev.Keyword;
                expectFunction = word is "function";
                expectType = word is "class" or "interface" or "enum" or "extends";
                bodyPending |= word is "class" or "interface" or "enum";
                declaring = word is "const" or "let" or "var";
                typeAlias &= word is "extends" or "default";
                return TokenKind.Keyword;
            case WordClass.Contextual:
                if (!ContextualFollows(word, next))
                    return TokenKind.Plain;
                ContextualHints(word);
                return word is "of" ? TokenKind.Control : TokenKind.Keyword;
            default:
                return TokenKind.Plain;
        }
    }

    void ContextualHints(ReadOnlySpan<char> word)
    {
        var module = prev == Prev.KeywordModule;
        var star = prev == Prev.Star;
        prev = Prev.Keyword;
        if (word is "type" && module)
        {
            prev = Prev.KeywordModule;
            return;
        }
        if (word is "as")
        {
            expectType = typed && !star && Top != Frame.Module;
            return;
        }
        typeAlias |= typed && word is "type";
        expectType = typed && word is "type" or "namespace" or "module" or "implements" or "keyof" or "infer" or "is" or
            "satisfies" or "unique";
    }
}
