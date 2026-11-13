using System.Diagnostics;
using DiffViewer.Highlight;

namespace DiffViewer.E2E;

public static partial class LexerChecks
{
    const string CSharpSample = """"
        using System.Collections.Immutable;
        namespace Demo.App;
        /// <summary>Docs</summary>
        [Obsolete("use Next")]
        public sealed partial record Point(int X, int Y) : IShape, IEquatable<Point>
        {
            public static readonly Point Origin = new(0, 0);
            public string Name { get; init; } = "point";
            public double Length => Math.Sqrt(X * X + Y * Y);
            public List<Point> Neighbours() => [this with { X = X + 1 }];
            public T Convert<T>(Func<Point, T> map) where T : notnull => map(this);
        }
        #region Helpers
        var text = $"Point {Origin.Name} at {X:N2} {{braces}} \t";
        var path = @"C:\temp\""quoted""";
        var raw = """
            raw "quotes" stay text
            """;
        var json = $$"""{"x": {{X}}}""";
        /* block
           comment */ var after = 'c' + '\n';
        var hex = 0xFF_FFul; var real = 1.5e-3m;
        if (value is not null && item is Point p) return;
        var @class = typeof(Dictionary<string, int>);
        #endregion
        var record = new Thing(); record.Name = $@"{record.Name}
        second line {Count} ""end""";
        int count = items.Count; var var = 1;
        """";

    static void CSharp(IsCheck Is, TokenCheck Token, Action<bool, string> check, Action<string, double> timing)
    {
        var c = new Lexed(Language.CSharp, CSharpSample);
        Is(c, 0, "using", TokenKind.Keyword, "C# using");
        Is(c, 1, "namespace", TokenKind.Keyword, "C# namespace");
        Token(c, 2, "/// <summary>Docs</summary>", TokenKind.Comment, "C# documentation comment");
        Is(c, 3, "Obsolete", TokenKind.Decorator, "C# attribute name");
        Is(c, 3, "\"use Next\"", TokenKind.String, "C# attribute argument");
        Is(c, 4, "partial", TokenKind.Keyword, "C# contextual partial");
        Is(c, 4, "record", TokenKind.Keyword, "C# contextual record");
        Is(c, 4, "Point", TokenKind.Type, "C# declared record name");
        Is(c, 4, "int", TokenKind.Type, "C# built-in type");
        Is(c, 4, "IShape", TokenKind.Type, "C# base list");
        Is(c, 4, "IEquatable", TokenKind.Type, "C# generic base");
        Is(c, 4, "Point", TokenKind.Type, "C# type argument", 1);
        Is(c, 6, "Point", TokenKind.Type, "C# field type");
        Is(c, 6, "new", TokenKind.Keyword, "C# target-typed new");
        Is(c, 7, "get", TokenKind.Keyword, "C# get accessor");
        Is(c, 7, "init", TokenKind.Keyword, "C# init accessor");
        Is(c, 7, "\"point\"", TokenKind.String, "C# string");
        Is(c, 8, "Sqrt", TokenKind.Function, "C# method call");
        Is(c, 9, "List", TokenKind.Type, "C# generic return type");
        Is(c, 9, "Neighbours", TokenKind.Function, "C# method declaration");
        Is(c, 9, "this", TokenKind.Constant, "C# this");
        Is(c, 9, "with", TokenKind.Keyword, "C# with expression");
        Is(c, 10, "Convert", TokenKind.Function, "C# generic method declaration");
        Is(c, 10, "Func", TokenKind.Type, "C# generic parameter type");
        Is(c, 10, "where", TokenKind.Keyword, "C# constraint clause");
        Is(c, 10, "map(", TokenKind.Function, "C# delegate invocation");
        Is(c, 12, "#region", TokenKind.Keyword, "C# region directive");
        Is(c, 12, "Helpers", TokenKind.Comment, "C# region name");
        Is(c, 13, "var", TokenKind.Keyword, "C# var");
        Is(c, 13, "$\"Point ", TokenKind.String, "C# interpolated string start");
        Is(c, 13, "{Origin", TokenKind.Interpolation, "C# interpolation hole");
        Is(c, 13, "Name", TokenKind.Property, "C# member inside a hole");
        Is(c, 13, "{{braces}}", TokenKind.String, "C# escaped braces");
        Is(c, 13, "\\t", TokenKind.Escape, "C# escape sequence");
        Token(c, 14, "@\"C:\\temp\\\"\"quoted\"\"\"", TokenKind.String, "C# verbatim string with doubled quotes");
        Is(c, 16, "raw \"quotes\" stay text", TokenKind.String, "C# raw literal across lines");
        Is(c, 17, ";", TokenKind.Punctuation, "C# code after a raw literal");
        Is(c, 18, "\"x\"", TokenKind.String, "C# single braces in $$ raw literals are text");
        Is(c, 18, "{{X", TokenKind.Interpolation, "C# $$ raw literal hole");
        Is(c, 18, "X}}", TokenKind.Plain, "C# code inside a $$ hole");
        Is(c, 20, "comment */", TokenKind.Comment, "C# block comment across lines");
        Is(c, 20, "var", TokenKind.Keyword, "C# code after a block comment");
        Is(c, 20, "\\n", TokenKind.Escape, "C# character escape");
        Token(c, 21, "0xFF_FFul", TokenKind.Number, "C# hex literal with separator and suffix");
        Token(c, 21, "1.5e-3m", TokenKind.Number, "C# decimal exponent literal");
        Is(c, 22, "if", TokenKind.Control, "C# if");
        Is(c, 22, "not", TokenKind.Keyword, "C# not pattern");
        Is(c, 22, "null", TokenKind.Constant, "C# null");
        Is(c, 22, "Point", TokenKind.Type, "C# type pattern");
        Is(c, 22, "value", TokenKind.Plain, "C# value is a name");
        Is(c, 23, "@class", TokenKind.Plain, "C# verbatim identifier");
        Is(c, 23, "typeof", TokenKind.Keyword, "C# typeof");
        Is(c, 23, "Dictionary", TokenKind.Type, "C# typeof operand");
        Is(c, 24, "#endregion", TokenKind.Keyword, "C# endregion");
        Is(c, 25, "record", TokenKind.Plain, "C# record as a variable");
        Is(c, 25, "Name", TokenKind.Property, "C# property assignment");
        Is(c, 25, "{record", TokenKind.Interpolation, "C# verbatim interpolated hole");
        Is(c, 26, "second line", TokenKind.String, "C# verbatim interpolated string across lines");
        Is(c, 26, "{Count", TokenKind.Interpolation, "C# hole on a continued line");
        Is(c, 27, "int", TokenKind.Type, "C# code after a multi-line string");
        Is(c, 27, "var", TokenKind.Plain, "C# var as a name", 1);
        check(Highlight.Languages.FromPath("src/App.CS") == Language.CSharp && Highlight.Languages.FromPath("build.csx") == Language.CSharp, "C# extensions select C#");

        var corpus = new List<string>();
        var random = new Random(11);
        var lines = LexerSamples.Lines(CSharpSample);
        while (corpus.Count < 20_000)
        {
            var line = lines[random.Next(lines.Length)];
            int a = random.Next(line.Length + 1), b = random.Next(a, line.Length + 1);
            corpus.Add(random.Next(3) == 0 ? line[a..b] + line[..a] : line);
        }
        var fuzzed = Highlighter.Tokenize(Language.CSharp, corpus);
        var ordered = true;
        for (var n = 0; n < corpus.Count && ordered; n++)
            for (int t = fuzzed.LineStarts[n], end = 0; t < fuzzed.LineStarts[n + 1]; t++)
            {
                ordered &= fuzzed.Starts[t] >= end && fuzzed.Lengths[t] > 0 && fuzzed.Starts[t] + fuzzed.Lengths[t] <= corpus[n].Length;
                end = fuzzed.Starts[t] + fuzzed.Lengths[t];
            }
        check(ordered, "C# tokens stay ordered, disjoint and inside their line over fuzzed input");

        var big = new List<string>();
        while (big.Sum(l => l.Length + 1) < 2_000_000)
            big.AddRange(lines);
        Highlighter.Tokenize(Language.CSharp, big);
        var times = new double[5];
        for (var i = 0; i < times.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            Highlighter.Tokenize(Language.CSharp, big);
            times[i] = clock.Elapsed.TotalMilliseconds;
        }
        Array.Sort(times);
        timing("lexer-csharp-2mb-ms", times[2]);
        check(times[2] < 50, $"tokenizing 2 MB of C# takes under 50 ms ({times[2]:F1} ms)");
    }
}
