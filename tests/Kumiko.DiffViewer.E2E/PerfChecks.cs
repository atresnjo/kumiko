using System.Diagnostics;
using DiffViewer.Core;
using DiffViewer.Sources;
using Kumiko.UI;

namespace DiffViewer.E2E;

/// <summary>
/// Frame cost while scrolling a 100k-line TypeScript diff: build, layout, paint and the accessibility snapshot of
/// every frame, at a Retina scale. A frame must stay well inside a 120 Hz refresh (8.3 ms) at the 95th percentile.
/// </summary>
internal static class PerfChecks
{
    const int Files = 40;
    const int LinesPerFile = 2500;
    const double Budget = 8;

    public static void Run(Harness h)
    {
        var root = Path.Combine(Path.GetTempPath(), "kumiko-diff-e2e", "perf-" + Environment.ProcessId);
        var (left, right) = (Path.Combine(root, "old"), Path.Combine(root, "new"));
        try
        {
            Generate(left, right);
            var request = new DiffRequest(SourceKind.Paths, left, null, right);
            Loader.Load(request, false, CancellationToken.None).GetAwaiter().GetResult();
            var clock = Stopwatch.StartNew();
            using var program = Harness.Open(request);
            h.Timing("load-100k-lines-ms", clock.Elapsed.TotalMilliseconds);
            var model = program.Model;
            int lines = model.Document.Files.Sum(f => f.Lines.Length);
            h.Check(model.Document.Files.Length == Files && lines > 100_000, $"the benchmark diff shows over 100k lines ({lines})");
            h.Check(clock.Elapsed.TotalMilliseconds < 1500, "loading, diffing and highlighting 200k source lines takes under 1.5 s");
            using var tester = new Tester(program, new(1280, 820, Scale: 2));
            tester.Context.ReduceMotion = true;
            tester.MoveTo(800, 400);
            // Warm up the JIT; NativeAOT runs have nothing to warm.
            for (int i = 0; i < 300; i++)
                tester.Scroll(i % 2 == 0 ? 36 : -36);
            Scroll(h, tester, "unified-trackpad", 36, 900);
            Scroll(h, tester, "unified-fling", 480, 300);
            program.Send(new Msg.SetMode(ViewMode.Split));
            tester.Settle();
            Scroll(h, tester, "split-trackpad", 36, 900);
            Scroll(h, tester, "split-fling", 480, 300);
            Keys(h, tester, program);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>Scrolls by <paramref name="delta"/> per frame and checks frame times and per-frame allocation.</summary>
    static void Scroll(Harness h, Tester tester, string name, float delta, int frames)
    {
        for (int i = 0; i < 30; i++)
            tester.Scroll(delta);
        var times = new double[frames];
        var nodes = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int collections = GC.CollectionCount(0);
        for (int i = 0; i < frames; i++)
        {
            var start = Stopwatch.GetTimestamp();
            tester.Scroll(delta);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            nodes = Math.Max(nodes, tester.Context.Stats.Nodes);
        }
        double perFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)frames;
        Array.Sort(times);
        double p50 = times[frames / 2], p95 = times[(int)(frames * .95)], p99 = times[(int)(frames * .99)];
        h.Timing($"{name}-p50-ms", p50);
        h.Timing($"{name}-p95-ms", p95);
        h.Timing($"{name}-p99-ms", p99);
        h.Timing($"{name}-max-ms", times[^1]);
        h.Timing($"{name}-kb-per-frame", perFrame / 1024);
        h.Timing($"{name}-gen0-collections", GC.CollectionCount(0) - collections);
        h.Timing($"{name}-nodes", nodes);
        h.Check(p95 < Budget, $"{name}: 95% of frames take under {Budget} ms ({p95:F2} ms)");
        h.Check(nodes < 2500, $"{name}: a frame builds only the viewport ({nodes} nodes)");
    }

    /// <summary>Holding j or Space: each repeat moves the cursor and reveals it in one frame.</summary>
    static void Keys(Harness h, Tester tester, Kumiko.Runtime.Program<Model, Msg> program)
    {
        program.Send(new Msg.Top());
        tester.Settle();
        var times = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            var start = Stopwatch.GetTimestamp();
            tester.Keystroke(i % 10 == 9 ? ' ' : 'j');
            times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        times.Sort();
        var p95 = times[(int)(times.Count * .95)];
        h.Timing("held-keys-p95-ms", p95);
        h.Check(p95 < Budget, $"held movement keys: 95% of frames take under {Budget} ms ({p95:F2} ms)");
    }

    /// <summary>Realistic TypeScript/TSX files; the new side edits, inserts and deletes lines throughout.</summary>
    static void Generate(string left, string right)
    {
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        var sample = (LexerSamples.ReactComponent + "\n" + LexerSamples.DecoratedClass + "\n" + LexerSamples.TypeUtilities).Split('\n');
        var random = new Random(7);
        for (int f = 0; f < Files; f++)
        {
            var before = new List<string>(LinesPerFile);
            for (int i = 0; i < LinesPerFile; i++)
                before.Add(sample[(i + f * 17) % sample.Length]);
            var after = new List<string>(LinesPerFile + 200);
            for (int i = 0; i < before.Count; i++)
            {
                var roll = random.Next(100);
                if (roll < 6)
                    after.Add(before[i].Replace("const", "let").Replace("string", "number") + " // changed");
                else if (roll < 8)
                    continue;
                else
                {
                    after.Add(before[i]);
                    if (roll < 10)
                        after.Add($"  const added{f}_{i} = await fetchValue<number>({i}, {{ retry: true }});");
                }
            }
            var name = $"src/module{f:D2}/component{f:D2}.tsx";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(left, name))!);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(right, name))!);
            File.WriteAllLines(Path.Combine(left, name), before);
            File.WriteAllLines(Path.Combine(right, name), after);
        }
    }
}
