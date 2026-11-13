using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

sealed partial class ScriptLexer
{
    static bool IsTagPart(char c) => IsIdentPart(c) || c is '.' or '-' or ':';

    static int TagEnd(ReadOnlySpan<char> s, int i)
    {
        while (i < s.Length && IsTagPart(s[i]))
            i++;
        return i;
    }

    /// <summary>A '&lt;' in expression position starts JSX unless TSX reads it as a generic arrow (&lt;T,&gt; or &lt;T extends U&gt;).</summary>
    bool IsJsxStart(ReadOnlySpan<char> s, int i)
    {
        var n = At(s, i + 1);
        if (n == '>')
            return true;
        if (!IsIdentStart(n))
            return false;
        if (!typed)
            return true;
        var end = IdentEnd(s, i + 1);
        var next = SkipSpaces(s, end);
        if (At(s, next) == ',')
            return false;
        return !(next > end && s[next..].StartsWith("extends") && !IsIdentPart(At(s, next + 7)));
    }

    int OpenTag(ReadOnlySpan<char> s, int i)
    {
        Emit(i, 1, TokenKind.Punctuation);
        if (At(s, i + 1) == '>')
        {
            Emit(i + 1, 1, TokenKind.Punctuation);
            Push(Frame.JsxChildren);
            return i + 2;
        }
        var end = TagEnd(s, i + 1);
        Emit(i + 1, end - i - 1, TokenKind.Tag);
        Push(Frame.JsxTag);
        var arguments = typed && At(s, end) == '<' ? ScanGeneric(s, end) : -1;
        return arguments > 0 ? TypeArguments(s, end, arguments) : end;
    }

    /// <summary>Type arguments on a TSX element (&lt;Select&lt;Option&gt; ...&gt;): names are types, the rest punctuation.</summary>
    int TypeArguments(ReadOnlySpan<char> s, int i, int end)
    {
        while (i < end)
        {
            var c = s[i];
            if (IsIdentStart(c))
            {
                var word = IdentEnd(s, i);
                Emit(i, word - i, TokenKind.Type);
                i = word;
                continue;
            }
            if (c != ' ' && c != '\t')
                Emit(i, 1, TokenKind.Punctuation);
            i++;
        }
        return end;
    }

    /// <summary>After an element closes: back in script it is a value, so a following '/' divides.</summary>
    void AfterElement()
    {
        if (!IsScript(Top))
            return;
        prev = Prev.Value;
        expectType = false;
        typeChain = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int TagStep(ReadOnlySpan<char> s, int i)
    {
        var c = s[i];
        if (c == ' ' || c == '\t')
            return SkipSpaces(s, i);
        if (Top == Frame.JsxClose)
        {
            if (c == '>')
            {
                Emit(i, 1, TokenKind.Punctuation);
                Pop();
                if (Top == Frame.JsxChildren)
                    Pop();
                AfterElement();
                return i + 1;
            }
            if (!IsIdentStart(c))
                return i + 1;
            var end = TagEnd(s, i);
            Emit(i, end - i, TokenKind.Tag);
            return end;
        }
        switch (c)
        {
            case '/' when At(s, i + 1) == '>':
                Emit(i, 2, TokenKind.Punctuation);
                Pop();
                AfterElement();
                return i + 2;
            case '/' when At(s, i + 1) == '/':
                Emit(i, s.Length - i, TokenKind.Comment);
                return s.Length;
            case '/' when At(s, i + 1) == '*':
                return OpenComment(s, i);
            case '>':
                Emit(i, 1, TokenKind.Punctuation);
                Pop();
                Push(Frame.JsxChildren);
                return i + 1;
            case '{':
                Emit(i, 1, TokenKind.Punctuation);
                Push(Frame.JsxExpr);
                prev = Prev.Open;
                return i + 1;
            case '=':
                Emit(i, 1, TokenKind.Operator);
                return i + 1;
            case '"' or '\'':
                Emit(i, 1, TokenKind.String);
                Push(c == '"' ? Frame.JsxDouble : Frame.JsxSingle);
                return JsxStringStep(s, i + 1);
            default:
                if (!IsIdentStart(c))
                    return i + 1;
                var end = TagEnd(s, i);
                Emit(i, end - i, TokenKind.Attribute);
                return end;
        }
    }

    /// <summary>JSX attribute strings have no escapes and may span lines.</summary>
    int JsxStringStep(ReadOnlySpan<char> s, int i)
    {
        var quote = Top == Frame.JsxDouble ? '"' : '\'';
        var close = s[i..].IndexOf(quote);
        if (close < 0)
        {
            Emit(i, s.Length - i, TokenKind.String);
            return s.Length;
        }
        Emit(i, close + 1, TokenKind.String);
        Pop();
        return i + close + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int ChildrenStep(ReadOnlySpan<char> s, int i)
    {
        var found = s[i..].IndexOfAny('<', '{', '&');
        if (found < 0)
            return s.Length;
        i += found;
        var c = s[i];
        if (c == '{')
        {
            Emit(i, 1, TokenKind.Punctuation);
            Push(Frame.JsxExpr);
            prev = Prev.Open;
            return i + 1;
        }
        if (c == '&')
            return Entity(s, i);
        var n = At(s, i + 1);
        if (n == '/')
        {
            Emit(i, 2, TokenKind.Punctuation);
            var next = SkipSpaces(s, i + 2);
            if (At(s, next) != '>')
            {
                Push(Frame.JsxClose);
                return i + 2;
            }
            Emit(next, 1, TokenKind.Punctuation);
            Pop();
            AfterElement();
            return next + 1;
        }
        if (n == '>' || IsIdentStart(n))
            return OpenTag(s, i);
        return i + 1;
    }

    int Entity(ReadOnlySpan<char> s, int i)
    {
        var j = i + 1;
        if (At(s, j) == '#')
            j++;
        var start = j;
        while (j < s.Length && j < start + 10 && char.IsAsciiLetterOrDigit(s[j]))
            j++;
        if (j == start || At(s, j) != ';')
            return i + 1;
        Emit(i, j + 1 - i, TokenKind.Escape);
        return j + 1;
    }
}
