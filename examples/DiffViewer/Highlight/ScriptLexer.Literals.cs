using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

sealed partial class ScriptLexer
{
    /// <summary>Lexes a quoted string from start (the quote, or 0 on a continued line) up to its closing quote or line end.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int ScanString(ReadOnlySpan<char> s, int start, int body, char quote)
    {
        prev = Prev.Value;
        var segment = start;
        var j = body;
        while (j < s.Length)
        {
            var found = s[j..].IndexOfAny(quote, '\\');
            if (found < 0)
                break;
            j += found;
            if (s[j] == quote)
            {
                Emit(segment, j + 1 - segment, TokenKind.String);
                return j + 1;
            }
            if (j + 1 == s.Length)
            {
                Emit(segment, s.Length - segment, TokenKind.String);
                Push(quote == '\'' ? Frame.SingleString : Frame.DoubleString);
                return s.Length;
            }
            Emit(segment, j - segment, TokenKind.String);
            var escape = EscapeLength(s, j);
            Emit(j, escape, TokenKind.Escape);
            j += escape;
            segment = j;
        }
        Emit(segment, s.Length - segment, TokenKind.String);
        return s.Length;
    }

    static int EscapeLength(ReadOnlySpan<char> s, int j)
    {
        var n = At(s, j + 1);
        var end = j + 2;
        if (n == 'x')
        {
            while (end < s.Length && end < j + 4 && char.IsAsciiHexDigit(s[end]))
                end++;
        }
        else if (n == 'u' && At(s, j + 2) == '{')
        {
            var close = end + 1;
            while (close < s.Length && close < j + 12 && char.IsAsciiHexDigit(s[close]))
                close++;
            end = At(s, close) == '}' ? close + 1 : end;
        }
        else if (n == 'u')
        {
            while (end < s.Length && end < j + 6 && char.IsAsciiHexDigit(s[end]))
                end++;
        }
        else if (n is >= '0' and <= '7')
        {
            while (end < s.Length && end < j + 4 && s[end] is >= '0' and <= '7')
                end++;
        }
        return Math.Min(end, s.Length) - j;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int TemplateStep(ReadOnlySpan<char> s, int i)
    {
        var segment = i;
        var j = i;
        while (j < s.Length)
        {
            var found = s[j..].IndexOfAny('`', '\\', '$');
            if (found < 0)
                break;
            j += found;
            var c = s[j];
            if (c == '`')
            {
                Emit(segment, j + 1 - segment, TokenKind.String);
                Pop();
                prev = Prev.Value;
                return j + 1;
            }
            if (c == '\\')
            {
                Emit(segment, j - segment, TokenKind.String);
                var escape = EscapeLength(s, j);
                Emit(j, escape, TokenKind.Escape);
                j += escape;
                segment = j;
                continue;
            }
            if (At(s, j + 1) == '{')
            {
                Emit(segment, j - segment, TokenKind.String);
                Emit(j, 2, TokenKind.Interpolation);
                Push(Frame.Interp);
                prev = Prev.Open;
                return j + 2;
            }
            j++;
        }
        Emit(segment, s.Length - segment, TokenKind.String);
        return s.Length;
    }

    int OpenComment(ReadOnlySpan<char> s, int i)
    {
        Emit(i, 2, TokenKind.Comment);
        Push(Frame.Comment);
        return CommentStep(s, i + 2);
    }

    int CommentStep(ReadOnlySpan<char> s, int i)
    {
        var end = s[i..].IndexOf("*/");
        if (end < 0)
        {
            Emit(i, s.Length - i, TokenKind.Comment);
            return s.Length;
        }
        Emit(i, end + 2, TokenKind.Comment);
        Pop();
        return i + end + 2;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Number(ReadOnlySpan<char> s, int i)
    {
        var j = i;
        var radix = At(s, j + 1) | 0x20;
        if (s[j] == '0' && radix is 'x' or 'b' or 'o')
        {
            j += 2;
            while (j < s.Length && (char.IsAsciiHexDigit(s[j]) || s[j] == '_'))
                j++;
        }
        else
        {
            while (j < s.Length && (IsDigit(s[j]) || s[j] == '_'))
                j++;
            if (At(s, j) == '.' && At(s, j + 1) != '.')
            {
                j++;
                while (j < s.Length && (IsDigit(s[j]) || s[j] == '_'))
                    j++;
            }
            var sign = At(s, j + 1) is '+' or '-' ? 1 : 0;
            if ((At(s, j) | 0x20) == 'e' && IsDigit(At(s, j + 1 + sign)))
            {
                j += 2 + sign;
                while (j < s.Length && (IsDigit(s[j]) || s[j] == '_'))
                    j++;
            }
        }
        if (At(s, j) == 'n')
            j++;
        Emit(i, j - i, TokenKind.Number);
        prev = Prev.Value;
        return j;
    }

    /// <summary>Returns the end of a regex literal starting at i, or -1 when the line ends first.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int ScanRegex(ReadOnlySpan<char> s, int i)
    {
        var j = i + 1;
        var inClass = false;
        while (j < s.Length)
        {
            var c = s[j];
            if (c == '\\')
            {
                j += 2;
                continue;
            }
            if (c == '[')
                inClass = true;
            else if (c == ']')
                inClass = false;
            else if (c == '/' && !inClass)
                break;
            j++;
        }
        if (j >= s.Length)
            return -1;
        return IdentEnd(s, j + 1);
    }

    /// <summary>
    /// Looks ahead from a '&lt;' for type arguments closed on the same line. Returns the index after the matching '&gt;',
    /// or -1 when the text reads like a comparison.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int ScanGeneric(ReadOnlySpan<char> s, int i)
    {
        var level = 0;
        var braces = 0;
        var limit = Math.Min(s.Length, i + 400);
        for (var j = i; j < limit; j++)
        {
            var c = s[j];
            if (IsIdentPart(c) || c is ' ' or '\t' or ',' or '.' or '[' or ']' or '?' or ':' or '(' or ')')
                continue;
            switch (c)
            {
                case '<':
                    level++;
                    break;
                case '>':
                    level--;
                    if (level == 0)
                        return j + 1;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ';':
                    if (braces <= 0)
                        return -1;
                    break;
                case '|' or '&':
                    if (At(s, j + 1) == c)
                        return -1;
                    break;
                case '=':
                    if (At(s, j + 1) == '>')
                        j++;
                    else if (At(s, j + 1) == '=')
                        return -1;
                    break;
                case '\'' or '"' or '`':
                    var close = s[(j + 1)..].IndexOf(c);
                    if (close < 0)
                        return -1;
                    j += close + 1;
                    break;
                case '-':
                    if (!IsDigit(At(s, j + 1)))
                        return -1;
                    break;
                default:
                    return -1;
            }
        }
        return -1;
    }
}
