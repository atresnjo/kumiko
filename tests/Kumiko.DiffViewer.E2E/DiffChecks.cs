using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using DiffViewer.Core;

namespace DiffViewer.E2E;

/// <summary>Behaviour checks for the line diff, hunk grouping and word emphasis ("Diffing" in ACCEPTANCE.md).</summary>
public static class DiffChecks
{
    public static void Run(Action<bool, string> check, Action<string, double> timing)
    {
        Basics(check);
        Randomized(check);
        ContextMerging(check);
        Whitespace(check);
        Readability(check);
        Git(check);
        Words(check);
        Pairs(check);
        Timings(check, timing);
    }

    static void Basics(Action<bool, string> check)
    {
        string[] empty = [];
        string[] one = ["only"];
        var many = Enumerable.Range(0, 1000).Select(i => $"line {i % 37}").ToArray();
        check(LineDiff.Compute(empty, empty).IsEmpty && LineDiff.Compute(one, one).IsEmpty && LineDiff.Compute(many, [.. many]).IsEmpty,
            "identical inputs (empty, one line, 1000 repeated lines) produce no hunks");
        var added = LineDiff.Compute(empty, ["a", "b"]);
        check(added.Length == 1 && Header(added[0]) == (0, 0, 0, 2) &&
            added[0].Lines.SequenceEqual([new DiffLine(LineKind.Added, -1, 0), new DiffLine(LineKind.Added, -1, 1)]),
            "an empty old side gives one hunk -0,0 +1,2 of added lines");
        var removed = LineDiff.Compute(["a", "b"], empty);
        check(removed.Length == 1 && Header(removed[0]) == (0, 2, 0, 0) && removed[0].Lines.All(l => l.Kind == LineKind.Removed && l.New == -1),
            "an empty new side gives one hunk of removed lines");
        var replaced = LineDiff.Compute(["a"], ["b"]);
        check(replaced.Length == 1 && Header(replaced[0]) == (0, 1, 0, 1) &&
            replaced[0].Lines.SequenceEqual([new DiffLine(LineKind.Removed, 0, -1), new DiffLine(LineKind.Added, -1, 0)]),
            "a one-line file replaced reports line 1 on both sides, removal first");
        var appended = LineDiff.Compute(["a", "b", "c", "d", "e"], ["a", "b", "c", "d", "e", "f"]);
        check(appended.Length == 1 && Header(appended[0]) == (2, 3, 2, 4) && appended[0].Lines[^1] == new DiffLine(LineKind.Added, -1, 5),
            "an appended line keeps three lines of leading context with exact indices");
        var prepended = LineDiff.Compute(["a", "b"], ["z", "a", "b"], context: 1);
        check(prepended.Length == 1 && Header(prepended[0]) == (0, 1, 0, 2) && prepended[0].Lines[1] == new DiffLine(LineKind.Context, 0, 1),
            "a prepended line pairs old line 0 with new line 1");
        var cancelled = false;
        try
        {
            LineDiff.Compute(many, ["x", .. many], token: new CancellationToken(true));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        check(cancelled, "a cancelled token stops the diff with OperationCanceledException");
    }

    static void Randomized(Action<bool, string> check)
    {
        var rng = new Random(20261105);
        var failures = 0;
        var first = "";
        for (var iteration = 0; iteration < 4000; iteration++)
        {
            int[] alphabets = [1, 2, 3, 8, 40, 100000];
            var alphabet = alphabets[rng.Next(alphabets.Length)];
            var whitespace = rng.Next(5) == 0;
            var oldLines = RandomLines(rng, rng.Next(4) == 0 ? rng.Next(3) : rng.Next(60), alphabet, whitespace);
            var newLines = rng.Next(5) == 0 ? RandomLines(rng, rng.Next(60), alphabet, whitespace) : Edit(rng, oldLines, alphabet, whitespace);
            var context = rng.Next(5);
            var hunks = LineDiff.Compute(oldLines, newLines, context, whitespace);
            var error = Validate(oldLines, newLines, hunks, context, whitespace);
            if (error is not null && failures++ == 0)
            {
                first = $"case {iteration}: {error}";
            }
        }
        for (var iteration = 0; iteration < 40; iteration++)
        {
            var oldLines = RandomLines(rng, 3000, 2 + iteration % 6, false);
            var newLines = iteration % 2 == 0 ? Edit(rng, oldLines, 6, false) : RandomLines(rng, 2500, 3, false);
            var error = Validate(oldLines, newLines, LineDiff.Compute(oldLines, newLines), 3, false);
            if (error is not null && failures++ == 0)
            {
                first = $"large case {iteration}: {error}";
            }
        }
        check(failures == 0, $"4040 seeded random pairs reproduce the new side with consistent headers and context {first}".TrimEnd());
    }

    static void ContextMerging(Action<bool, string> check)
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        var ok = true;
        foreach (var context in new[] { 0, 1, 3, 5 })
        {
            var merged = Change(lines, 10, 10 + 2 * context + 1);
            var split = Change(lines, 10, 10 + 2 * context + 2);
            var one = LineDiff.Compute(lines, merged, context);
            var two = LineDiff.Compute(lines, split, context);
            ok &= one.Length == 1 && two.Length == 2 && two[0].OldStart + two[0].OldCount <= two[1].OldStart;
            ok &= one[0].Lines.Count(l => l.Kind == LineKind.Context) == Math.Min(10, context) * 2 + 2 * context;
            ok &= Validate(lines, merged, one, context, false) is null && Validate(lines, split, two, context, false) is null;
        }
        check(ok, "changes separated by 2×context unchanged lines share a hunk; one more line splits them without repeating context");
    }

    static void Whitespace(Action<bool, string> check)
    {
        string[] oldLines = ["int  x = 1;", "\tfoo();", "bar", "end "];
        string[] newLines = ["int x=1;", "    foo();", "bar", "end"];
        check(LineDiff.Compute(oldLines, newLines, ignoreWhitespace: true).IsEmpty && LineDiff.Compute(oldLines, newLines).Length == 1,
            "ignoreWhitespace hides whitespace-only edits that are otherwise reported");
        string[] mixed = ["int x=1;", "    foo();", "baz", "end"];
        var hunks = LineDiff.Compute(oldLines, mixed, context: 0, ignoreWhitespace: true);
        check(hunks.Length == 1 && Header(hunks[0]) == (2, 1, 2, 1), "ignoreWhitespace still reports the one real change at its exact line");
    }

    static void Readability(Action<bool, string> check)
    {
        string[] oldLines = ["func a() {", "  x", "}", "", "func c() {", "  z", "}"];
        string[] newLines = ["func a() {", "  x", "}", "", "func b() {", "  y", "}", "", "func c() {", "  z", "}"];
        var hunks = LineDiff.Compute(oldLines, newLines);
        var added = hunks.SelectMany(h => h.Lines).Where(l => l.Kind == LineKind.Added).Select(l => newLines[l.New]).ToArray();
        check(added.SequenceEqual(["func b() {", "  y", "}", ""]), "the indent heuristic slides an inserted function to start at its header");
        string[] before = ["a", "b", "c"];
        string[] after = ["a", "x", "y", "c"];
        var lines = LineDiff.Compute(before, after)[0].Lines.Select(l => l.Kind).ToArray();
        check(lines.SequenceEqual([LineKind.Context, LineKind.Removed, LineKind.Added, LineKind.Added, LineKind.Context]),
            "a replaced line lists its deletion before its additions");
    }

    static void Git(Action<bool, string> check)
    {
        var directory = Directory.CreateTempSubdirectory("kumiko-diff-");
        try
        {
            var rng = new Random(77);
            var config = Path.Combine(directory.FullName, "empty.gitconfig");
            File.WriteAllText(config, "");
            var compared = 0;
            var equal = 0;
            var unique = 0;
            var uniqueEqual = 0;
            var notWorse = 0;
            for (var k = 0; k < 10; k++)
            {
                var isUnique = k < 6;
                var size = 40 + k * 400;
                var alphabet = isUnique ? 1_000_000 : 3 + k;
                var oldLines = RandomLines(rng, size, alphabet, false);
                var newLines = isUnique ? FreshEdits(rng, oldLines, 3 + k * 5) : Edit(rng, oldLines, alphabet, false);
                var oldPath = Path.Combine(directory.FullName, $"old{k}.txt");
                var newPath = Path.Combine(directory.FullName, $"new{k}.txt");
                File.WriteAllText(oldPath, oldLines.Length == 0 ? "" : string.Join('\n', oldLines) + "\n");
                File.WriteAllText(newPath, newLines.Length == 0 ? "" : string.Join('\n', newLines) + "\n");
                var git = GitNumstat(oldPath, newPath, config);
                if (git is null)
                {
                    check(true, "git cross-check skipped: git is unavailable");
                    return;
                }
                var hunks = LineDiff.Compute(oldLines, newLines);
                var mine = (Added: hunks.Sum(h => h.Lines.Count(l => l.Kind == LineKind.Added)),
                    Removed: hunks.Sum(h => h.Lines.Count(l => l.Kind == LineKind.Removed)));
                compared++;
                equal += mine == git.Value ? 1 : 0;
                notWorse += mine.Added + mine.Removed <= git.Value.Added + git.Value.Removed ? 1 : 0;
                unique += isUnique ? 1 : 0;
                uniqueEqual += isUnique && mine == git.Value ? 1 : 0;
            }
            check(uniqueEqual == unique, $"added/removed totals equal git diff --histogram --numstat on {unique} unambiguous pairs");
            check(notWorse == compared, $"edit totals are never larger than git's on {compared} pairs ({equal} identical)");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    static (int Added, int Removed)? GitNumstat(string oldPath, string newPath, string config)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "diff", "--no-index", "--numstat", "--histogram", "--", oldPath, newPath })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = config;
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (output.Length == 0)
            {
                return (0, 0);
            }
            var fields = output.Split('\t');
            return (int.Parse(fields[0], CultureInfo.InvariantCulture), int.Parse(fields[1], CultureInfo.InvariantCulture));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    static void Words(Action<bool, string> check)
    {
        var identifier = WordDiff.Compute("var total = computeSum(items, 1);", "var total = computeTotal(items, 1);");
        check(identifier.Old.SequenceEqual([new Emphasis(12, 10)]) && identifier.New.SequenceEqual([new Emphasis(12, 12)]),
            "a single changed identifier is the only emphasis on both sides");
        var spacing = WordDiff.Compute("a = b;", "a  = b;");
        check(spacing.Old.SequenceEqual([new Emphasis(1, 1)]) && spacing.New.SequenceEqual([new Emphasis(1, 2)]),
            "a whitespace-only change emphasises just the whitespace run");
        var different = WordDiff.Compute("return cache.lookup(key);", "throw new Error(message)");
        check(different.Old.IsEmpty && different.New.IsEmpty, "wildly different lines get no word emphasis");
        var same = WordDiff.Compute("unchanged line", "unchanged line");
        check(same.Old.IsEmpty && same.New.IsEmpty, "an unchanged line gets no emphasis");
        var appended = WordDiff.Compute("let value = compute(input);", "let value = compute(input); // cached");
        check(appended.Old.IsEmpty && appended.New.SequenceEqual([new Emphasis(27, 10)]), "an insertion at the end is one range on the new side");
        var two = WordDiff.Compute("draw(left, top, width)", "draw(right, top, height)");
        check(two.Old.Length == 2 && two.New.Length == 2 && two.Old[0] == new Emphasis(5, 4) && two.New[1] == new Emphasis(17, 6),
            "separate changed arguments stay separate ranges");
        var rng = new Random(5);
        var ordered = true;
        for (var k = 0; k < 2000; k++)
        {
            var x = RandomText(rng);
            var y = rng.Next(3) == 0 ? RandomText(rng) : Mutate(rng, x);
            var (o, n) = WordDiff.Compute(x, y);
            ordered &= InBounds(o, x.Length) && InBounds(n, y.Length) && (x != y || o.IsEmpty && n.IsEmpty);
        }
        check(ordered, "2000 random pairs give ordered, non-overlapping, non-empty ranges inside each line");
    }

    static void Pairs(Action<bool, string> check)
    {
        string[] oldLines = ["foo(a);", "completely unrelated text here", "alpha one", "beta two"];
        string[] newLines = ["foo(b);", "gamma", "alpha one!", "beta two!"];
        Span<int> partners = stackalloc int[2];
        WordDiff.Pair(Removed(0, 2), Added(0, 1), oldLines, newLines, partners);
        check(partners.SequenceEqual([0, -1]), "a removed line pairs with its similar replacement and an unrelated one stays unpaired");
        WordDiff.Pair(Removed(2, 2), Added(1, 3), oldLines, newLines, partners);
        check(partners.SequenceEqual([1, 2]), "pairing skips an inserted unrelated line and keeps order");
        var big = Enumerable.Range(0, 100).Select(i => $"value {i} = compute({i});").ToArray();
        var bigNew = big.Select(l => l.Replace("compute", "evaluate", StringComparison.Ordinal)).ToArray();
        var bigPartners = new int[100];
        WordDiff.Pair(Removed(0, 100), Added(0, 100), big, bigNew, bigPartners);
        check(bigPartners.Select((p, i) => p == i).All(x => x), "blocks beyond 64 lines pair positionally");
        var rng = new Random(9);
        var monotonic = true;
        for (var k = 0; k < 300; k++)
        {
            var left = Enumerable.Range(0, rng.Next(1, 12)).Select(_ => RandomText(rng)).ToArray();
            var right = Enumerable.Range(0, rng.Next(1, 12)).Select(_ => rng.Next(2) == 0 ? Mutate(rng, left[rng.Next(left.Length)]) : RandomText(rng)).ToArray();
            var result = new int[left.Length];
            WordDiff.Pair(Removed(0, left.Length), Added(0, right.Length), left, right, result);
            var last = -1;
            foreach (var p in result)
            {
                monotonic &= p == -1 || p > last && p < right.Length;
                last = p == -1 ? last : p;
            }
        }
        check(monotonic, "random blocks always pair monotonically with distinct added lines");
    }

    static void Timings(Action<bool, string> check, Action<string, double> timing)
    {
        var rng = new Random(42);
        var source = Source(rng, 100_000, "a");
        var edited = FreshEdits(rng, source, 1000);
        var typical = Measure(() => LineDiff.Compute(source, edited), 7, out var hunks);
        timing("diff 100k lines with 1000 scattered edits (median ms)", typical);
        check(typical < 30 && Validate(source, edited, hunks, 3, false) is null, $"a 100k-line file with 1000 edits diffs in {typical:F1} ms (< 30 ms)");
        var unrelatedOld = Source(rng, 200_000, "b");
        var unrelatedNew = Source(rng, 200_000, "c");
        var unrelated = Measure(() => LineDiff.Compute(unrelatedOld, unrelatedNew), 3, out hunks);
        timing("diff unrelated 200k vs 200k source-like lines (median ms)", unrelated);
        check(unrelated < 1000 && Validate(unrelatedOld, unrelatedNew, hunks, 3, false) is null,
            $"unrelated 200k-line files diff in {unrelated:F1} ms (< 1000 ms)");
        var sameOld = Enumerable.Repeat("        same();", 100_000).ToArray();
        var sameNew = FreshEdits(rng, sameOld, 12);
        var repeated = Measure(() => LineDiff.Compute(sameOld, sameNew), 3, out hunks);
        timing("diff 100k identical lines with 12 edits (median ms)", repeated);
        check(repeated < 1000 && Validate(sameOld, sameNew, hunks, 3, false) is null, $"100k identical lines with edits diff in {repeated:F1} ms (< 1000 ms)");
        var noisyOld = RandomLines(rng, 200_000, 4, false);
        var noisyNew = RandomLines(rng, 200_000, 4, false);
        var noisy = Measure(() => LineDiff.Compute(noisyOld, noisyNew), 3, out hunks);
        timing("diff 200k vs 200k lines over a 4-line alphabet (median ms)", noisy);
        check(noisy < 1000 && Validate(noisyOld, noisyNew, hunks, 3, false) is null, $"200k lines of a 4-line alphabet diff in {noisy:F1} ms (< 1000 ms)");
        var words = Measure(() =>
        {
            for (var k = 0; k < 10_000; k++)
            {
                WordDiff.Compute(source[k], source[k].Replace("Compute", "Evaluate", StringComparison.Ordinal));
            }
            return ImmutableArray<Hunk>.Empty;
        }, 3, out _);
        timing("word emphasis for 10k line pairs (median ms)", words);
    }

    static double Measure(Func<ImmutableArray<Hunk>> run, int repeats, out ImmutableArray<Hunk> result)
    {
        result = run();
        var samples = new double[repeats];
        for (var k = 0; k < repeats; k++)
        {
            var started = Stopwatch.GetTimestamp();
            result = run();
            samples[k] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        Array.Sort(samples);
        return samples[repeats / 2];
    }

    /// <summary>Applies the hunks to the old lines and checks every index, header and context rule. Null when valid.</summary>
    static string? Validate(string[] oldLines, string[] newLines, ImmutableArray<Hunk> hunks, int context, bool ignoreWhitespace)
    {
        var result = new List<string>(newLines.Length);
        var copied = 0;
        var lastChangeEnd = int.MinValue / 2;
        foreach (var hunk in hunks)
        {
            if (hunk.OldStart < copied || hunk.NewStart != result.Count + hunk.OldStart - copied)
            {
                return $"hunk at -{hunk.OldStart} +{hunk.NewStart} overlaps or misplaces its start";
            }
            for (; copied < hunk.OldStart; copied++)
            {
                result.Add(oldLines[copied]);
            }
            var i = hunk.OldStart;
            var j = hunk.NewStart;
            var firstChange = -1;
            var lastChange = -1;
            var run = 0;
            var previous = LineKind.Context;
            foreach (var line in hunk.Lines)
            {
                var valid = line.Kind switch
                {
                    LineKind.Context => line.Old == i && line.New == j && i < oldLines.Length && j < newLines.Length &&
                        Same(oldLines[i], newLines[j], ignoreWhitespace),
                    LineKind.Removed => line.Old == i && line.New == -1 && i < oldLines.Length && previous != LineKind.Added,
                    _ => line.New == j && line.Old == -1 && j < newLines.Length,
                };
                if (!valid)
                {
                    return $"line {line} in hunk -{hunk.OldStart},{hunk.OldCount} is out of sequence";
                }
                if (line.Kind == LineKind.Context)
                {
                    result.Add(oldLines[i]);
                    run++;
                    i++;
                    j++;
                }
                else
                {
                    if (firstChange >= 0 && run > 2 * context)
                    {
                        return $"hunk -{hunk.OldStart} keeps {run} context lines between changes";
                    }
                    firstChange = firstChange < 0 ? i : firstChange;
                    run = 0;
                    if (line.Kind == LineKind.Removed)
                    {
                        i++;
                    }
                    else
                    {
                        result.Add(newLines[j]);
                        j++;
                    }
                    lastChange = i;
                }
                previous = line.Kind;
            }
            if (firstChange < 0 || i - hunk.OldStart != hunk.OldCount || j - hunk.NewStart != hunk.NewCount)
            {
                return $"hunk -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} disagrees with its lines";
            }
            if (firstChange - hunk.OldStart != Math.Min(context, firstChange) || run != Math.Min(context, oldLines.Length - lastChange))
            {
                return $"hunk -{hunk.OldStart},{hunk.OldCount} has the wrong amount of context";
            }
            if (firstChange - lastChangeEnd <= 2 * context)
            {
                return $"hunk -{hunk.OldStart} should have merged with the previous hunk";
            }
            lastChangeEnd = lastChange;
            copied = i;
        }
        for (; copied < oldLines.Length; copied++)
        {
            result.Add(oldLines[copied]);
        }
        if (result.Count != newLines.Length)
        {
            return $"applying the script gives {result.Count} lines instead of {newLines.Length}";
        }
        for (var k = 0; k < result.Count; k++)
        {
            if (!Same(result[k], newLines[k], ignoreWhitespace))
            {
                return $"applying the script differs at new line {k}";
            }
        }
        return null;
    }

    static bool Same(string x, string y, bool ignoreWhitespace) => ignoreWhitespace
        ? string.Concat(x.Where(c => !char.IsWhiteSpace(c))) == string.Concat(y.Where(c => !char.IsWhiteSpace(c)))
        : x == y;

    static (int, int, int, int) Header(Hunk hunk) => (hunk.OldStart, hunk.OldCount, hunk.NewStart, hunk.NewCount);

    static bool InBounds(ImmutableArray<Emphasis> ranges, int length)
    {
        var end = 0;
        foreach (var range in ranges)
        {
            if (range.Length <= 0 || range.Start < end || range.Start + range.Length > length)
            {
                return false;
            }
            end = range.Start + range.Length;
        }
        return true;
    }

    static DiffLine[] Removed(int start, int count) => [.. Enumerable.Range(start, count).Select(i => new DiffLine(LineKind.Removed, i, -1))];

    static DiffLine[] Added(int start, int count) => [.. Enumerable.Range(start, count).Select(i => new DiffLine(LineKind.Added, -1, i))];

    static string[] Change(string[] lines, params int[] indices)
    {
        var copy = (string[])lines.Clone();
        foreach (var index in indices)
        {
            copy[index] = $"changed {index}";
        }
        return copy;
    }

    static string[] RandomLines(Random rng, int count, int alphabet, bool whitespace)
    {
        var lines = new string[count];
        for (var k = 0; k < count; k++)
        {
            lines[k] = Line(rng, alphabet, whitespace);
        }
        return lines;
    }

    static string Line(Random rng, int alphabet, bool whitespace)
    {
        var text = alphabet > 1000 ? $"unique {rng.Next():x8}{rng.Next():x8}" : $"w{rng.Next(alphabet)}";
        if (!whitespace)
        {
            return text;
        }
        return rng.Next(3) switch
        {
            0 => " " + text,
            1 => text.Replace("w", "w ", StringComparison.Ordinal),
            _ => text,
        };
    }

    static string[] Edit(Random rng, string[] lines, int alphabet, bool whitespace)
    {
        var result = new List<string>(lines);
        var edits = rng.Next(7);
        for (var k = 0; k < edits; k++)
        {
            var at = rng.Next(result.Count + 1);
            var kind = result.Count == 0 ? 0 : rng.Next(3);
            if (kind == 0)
            {
                result.Insert(at, Line(rng, alphabet, whitespace));
            }
            else if (kind == 1)
            {
                result.RemoveAt(Math.Min(at, result.Count - 1));
            }
            else
            {
                result[Math.Min(at, result.Count - 1)] = Line(rng, alphabet, whitespace);
            }
        }
        return [.. result];
    }

    /// <summary>Edits that introduce only new unique lines, so the minimal script is unambiguous.</summary>
    static string[] FreshEdits(Random rng, string[] lines, int edits)
    {
        var result = new List<string>(lines);
        for (var k = 0; k < edits; k++)
        {
            var at = rng.Next(Math.Max(1, result.Count));
            var fresh = $"fresh {k} {rng.Next():x8}";
            var kind = result.Count == 0 ? 0 : rng.Next(5);
            if (kind == 0)
            {
                result.Insert(at, fresh);
            }
            else if (kind == 1)
            {
                result.RemoveAt(at);
            }
            else
            {
                result[at] = fresh;
            }
        }
        return [.. result];
    }

    static string[] Source(Random rng, int count, string tag)
    {
        var lines = new string[count];
        for (var k = 0; k < count; k++)
        {
            lines[k] = (k % 9) switch
            {
                0 => "",
                1 => $"    public void {tag}Method{rng.Next():x}()",
                2 => "    {",
                8 => "    }",
                _ => $"        var {tag}{rng.Next():x} = Compute({k}, {rng.Next(1000)}) + offset;",
            };
        }
        return lines;
    }

    static string RandomText(Random rng)
    {
        string[] words = ["foo", "bar", "x", "1", "(", ")", " ", "  ", ".", "=", "value", "return", ";", "\t", "é", "名前"];
        var count = rng.Next(0, 12);
        return string.Concat(Enumerable.Range(0, count).Select(_ => words[rng.Next(words.Length)]));
    }

    static string Mutate(Random rng, string text)
    {
        if (text.Length == 0)
        {
            return "foo";
        }
        var at = rng.Next(text.Length);
        return rng.Next(3) switch
        {
            0 => text.Insert(at, "bar"),
            1 => text.Remove(at, 1),
            _ => text[..at] + "q" + text[(at + 1)..],
        };
    }
}
