using System.Runtime.CompilerServices;

namespace DiffViewer.Highlight;

/// <summary>JSON (and JSONC comments): keys are properties, other strings are strings.</summary>
static class JsonLexer
{
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void Run(IReadOnlyList<string> lines, TokenSink sink)
    {
        var comment = false;
        for (var n = 0; n < lines.Count; n++)
        {
            var s = lines[n].AsSpan();
            var i = 0;
            while (i < s.Length)
            {
                if (comment)
                {
                    var end = s[i..].IndexOf("*/");
                    var length = end < 0 ? s.Length - i : end + 2;
                    sink.Add(i, length, TokenKind.Comment);
                    comment = end < 0;
                    i += length;
                    continue;
                }
                i = Step(s, i, sink, ref comment);
            }
            sink.EndLine();
        }
    }

    static char At(ReadOnlySpan<char> s, int i) => (uint)i < (uint)s.Length ? s[i] : '\0';

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int Step(ReadOnlySpan<char> s, int i, TokenSink sink, ref bool comment)
    {
        var c = s[i];
        switch (c)
        {
            case ' ' or '\t':
                return i + 1;
            case '"':
                return String(s, i, sink);
            case '{' or '}' or '[' or ']' or ',' or ':':
                sink.Add(i, 1, TokenKind.Punctuation);
                return i + 1;
            case '/' when At(s, i + 1) == '/':
                sink.Add(i, s.Length - i, TokenKind.Comment);
                return s.Length;
            case '/' when At(s, i + 1) == '*':
                sink.Add(i, 2, TokenKind.Comment);
                comment = true;
                return i + 2;
        }
        if (c == '-' || (uint)(c - '0') <= 9)
            return Number(s, i, sink);
        if (!char.IsAsciiLetter(c))
            return i + 1;
        var j = i + 1;
        while (j < s.Length && char.IsAsciiLetter(s[j]))
            j++;
        if (s[i..j] is "true" or "false" or "null")
            sink.Add(i, j - i, TokenKind.Constant);
        return j;
    }

    static int Number(ReadOnlySpan<char> s, int i, TokenSink sink)
    {
        var j = i + 1;
        while (j < s.Length && (char.IsAsciiDigit(s[j]) || s[j] is '.' or 'e' or 'E' or '+' or '-'))
            j++;
        sink.Add(i, j - i, TokenKind.Number);
        return j;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static int String(ReadOnlySpan<char> s, int i, TokenSink sink)
    {
        var end = i + 1;
        while (end < s.Length && s[end] != '"')
            end += s[end] == '\\' ? 2 : 1;
        end = Math.Min(end + 1, s.Length);
        var after = end;
        while (after < s.Length && s[after] is ' ' or '\t')
            after++;
        var kind = At(s, after) == ':' ? TokenKind.Property : TokenKind.String;
        var segment = i;
        for (var j = i + 1; j < end - 1; j++)
        {
            if (s[j] != '\\')
                continue;
            sink.Add(segment, j - segment, kind);
            var length = Math.Min(At(s, j + 1) == 'u' ? 6 : 2, end - j);
            sink.Add(j, length, TokenKind.Escape);
            j += length - 1;
            segment = j + 1;
        }
        sink.Add(segment, end - segment, kind);
        return end;
    }
}
