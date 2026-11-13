using System.Diagnostics;
using DiffViewer.Highlight;

namespace DiffViewer.E2E;

public static partial class LexerChecks
{
    static readonly string[] Fragments =
    [
        "`", "${", "}", "{", "/*", "*/", "//", "/", "\"", "'", "\\", "<", ">", "</", "/>", "<>", "(", ")", "[", "]",
        "x", "Foo", "const", "type", "default", "return", "@dec", "#p", "0x", "1_", "10n", ".5", "e+", "?.", "??=", ">>>=",
        "=>", "&amp;", "&", "ü", "日本", "\t", " ", "  ", "<T,>", "<div a=\"", "a.b", "?:", ":", "?", ";", "\u00a0",
        "\U0001F600", "\uD83D", "=", "<T extends", "import", "from", "as", "* ", "..."
    ];

    static readonly Language[] AllLanguages =
        [Language.TypeScript, Language.Tsx, Language.JavaScript, Language.Jsx, Language.Json, Language.Plain];

    /// <summary>Token ranges are ordered, non-overlapping, non-empty, inside their line and never Plain.</summary>
    static string? Violation(Highlighting tokens, IReadOnlyList<string> lines)
    {
        if (tokens.LineStarts.Length != lines.Count + 1 || tokens.LineStarts[0] != 0)
            return "line table size";
        if (tokens.LineStarts[^1] != tokens.Kinds.Length || tokens.Starts.Length != tokens.Kinds.Length ||
            tokens.Lengths.Length != tokens.Kinds.Length)
            return "token table size";
        for (var line = 0; line < lines.Count; line++)
        {
            var end = 0;
            if (tokens.LineStarts[line + 1] < tokens.LineStarts[line])
                return $"line {line} starts before the previous line";
            for (var t = tokens.LineStarts[line]; t < tokens.LineStarts[line + 1]; t++)
            {
                var start = tokens.Starts[t];
                var length = tokens.Lengths[t];
                if (start < end || length <= 0 || start + length > lines[line].Length || tokens.Kinds[t] == TokenKind.Plain)
                    return $"line {line} token {t - tokens.LineStarts[line]} [{start}, +{length}) {tokens.Kinds[t]}: {lines[line]}";
                end = start + length;
            }
        }
        return null;
    }

    static List<string> Corpus()
    {
        var random = new Random(20261105);
        var lines = new List<string>();
        foreach (var sample in new[] { LexerSamples.ReactComponent, LexerSamples.DecoratedClass, LexerSamples.TypeUtilities })
            lines.AddRange(LexerSamples.Lines(sample));
        var samples = lines.ToArray();
        for (var round = 0; round < 60; round++)
        {
            foreach (var line in samples)
            {
                var chars = line.ToCharArray().ToList();
                var edits = random.Next(1, 4);
                for (var e = 0; e < edits && chars.Count > 0; e++)
                {
                    var at = random.Next(chars.Count);
                    if (random.Next(2) == 0)
                        chars.RemoveAt(at);
                    else
                        chars.InsertRange(at, Fragments[random.Next(Fragments.Length)]);
                }
                lines.Add(new string([.. chars]));
            }
        }
        var builder = new System.Text.StringBuilder();
        for (var n = 0; n < 20_000; n++)
        {
            builder.Clear();
            var count = random.Next(0, 16);
            for (var f = 0; f < count; f++)
                builder.Append(random.Next(5) == 0 ? (char)random.Next(32, 127) : Fragments[random.Next(Fragments.Length)]);
            lines.Add(builder.ToString());
        }
        return lines;
    }

    static void Invariants(Action<bool, string> check)
    {
        var corpus = Corpus();
        foreach (var language in AllLanguages)
        {
            string? violation;
            try
            {
                var first = Highlighter.Tokenize(language, corpus);
                violation = Violation(first, corpus);
                var second = Highlighter.Tokenize(language, corpus);
                if (violation is null && !(first.Starts.SequenceEqual(second.Starts) && first.Kinds.SequenceEqual(second.Kinds)))
                    violation = "tokenizing twice differs";
            }
            catch (Exception error)
            {
                violation = error.GetType().Name + ": " + error.Message;
            }
            check(violation is null, $"lexer {language} tokens over {corpus.Count} fuzzed lines are ordered and in range: {violation}");
        }
        foreach (var language in AllLanguages)
        {
            var empty = Highlighter.Tokenize(language, []);
            var blank = Highlighter.Tokenize(language, ["", ""]);
            check(empty.Lines == 0 && blank.Lines == 2 && blank.Kinds.Length == 0, $"lexer {language} handles empty input");
        }
    }

    /// <summary>A realistic ~2 MB TSX file, built from distinct string copies of the samples.</summary>
    static List<string> LargeFile(out long chars)
    {
        var sample = new List<string>();
        foreach (var text in new[] { LexerSamples.ReactComponent, LexerSamples.DecoratedClass, LexerSamples.TypeUtilities })
            sample.AddRange(LexerSamples.Lines(text));
        var lines = new List<string>();
        chars = 0;
        while (chars < 2L * 1024 * 1024)
        {
            foreach (var line in sample)
            {
                lines.Add(new string(line.AsSpan()));
                chars += line.Length + 1;
            }
        }
        return lines;
    }

    static void Timing(Action<bool, string> check, Action<string, double> timing)
    {
        var lines = LargeFile(out var chars);
        var warm = Highlighter.Tokenize(Language.Tsx, lines);
        check(Violation(warm, lines) is null, $"lexer 2 MB file tokens are valid ({lines.Count} lines, {warm.Kinds.Length} tokens)");
        var samples = new double[5];
        for (var run = 0; run < samples.Length; run++)
        {
            var clock = Stopwatch.StartNew();
            var tokens = Highlighter.Tokenize(Language.Tsx, lines);
            samples[run] = clock.Elapsed.TotalMilliseconds;
            GC.KeepAlive(tokens);
        }
        Array.Sort(samples);
        var median = samples[samples.Length / 2];
        timing("lexer-2mb-ms", median);
        check(median < 50, $"lexer tokenizes {chars / 1024} KiB in {median:F1} ms (median of 5 after warm-up), budget 50 ms");
    }

    static void Languages(Action<bool, string> check)
    {
        (string Path, Language Expected)[] cases =
        [
            ("src/app.ts", Language.TypeScript), ("lib/index.d.ts", Language.TypeScript), ("a/B.MTS", Language.TypeScript),
            ("x.cts", Language.TypeScript), ("View.tsx", Language.Tsx), ("C:\\w\\Page.TSX", Language.Tsx),
            ("main.js", Language.JavaScript), ("m.mjs", Language.JavaScript), ("c.cjs", Language.JavaScript),
            ("App.jsx", Language.Jsx), ("package.json", Language.Json), (".eslintrc.JSON", Language.Json),
            ("Makefile", Language.Plain), ("notes.md", Language.Plain), ("dir.ts/readme", Language.Plain),
            ("archive.tsx.bak", Language.Plain), (".ts", Language.Plain)
        ];
        foreach (var (path, expected) in cases)
        {
            var actual = DiffViewer.Highlight.Languages.FromPath(path);
            check(actual == expected, $"language of {path} is {actual}, expected {expected}");
        }
    }
}
