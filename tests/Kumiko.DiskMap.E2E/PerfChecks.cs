using System.Diagnostics;
using Kumiko.UI;

namespace DiskMap.E2E;

/// <summary>
/// Frame cost on the demo tree at Retina scale: build, layout, paint and the accessibility snapshot of each frame.
/// A 120 Hz refresh leaves 8.3 ms; 95% of frames must take under 8 ms.
/// </summary>
internal static class PerfChecks
{
    const double Budget = 8;

    public static void Run(Harness h)
    {
        var (program, _) = Harness.Demo();
        using var _p = program;
        using var t = new Tester(program, new(1360, 860, Scale: 2, Theme: Theme.DarkTheme));
        t.Settle(3);
        // Warm up the JIT; NativeAOT runs have nothing to warm.
        for (int i = 0; i < 200; i++)
            t.MoveTo(40 + i * 5 % 1000, 160 + i * 3 % 600);
        Measure(h, t, "hover-sweep", 900, i => t.MoveTo(30 + i * 7 % 1000, 150 + i * 13 % 640));
        Measure(h, t, "zoom-in-out", 240, i =>
        {
            if (i % 30 == 0)
                program.Send(i % 60 == 0 ? new Msg.Open([".cargo", "targets"]) : new Msg.Crumb(0));
            t.Frame(1 / 120f);
        });
        Measure(h, t, "metric-switch", 240, i =>
        {
            if (i % 20 == 0)
                program.Send(new Msg.SetMetric((DiskMap.Core.Metric)(i / 20 % 3)));
            t.Frame(1 / 120f);
        });
        Measure(h, t, "list-steps", 300, i => t.Key(i % 40 < 20 ? Key.Down : Key.Up));
    }

    static void Measure(Harness h, Tester t, string name, int frames, Action<int> frame)
    {
        var times = new double[frames];
        int nodes = 0;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++)
        {
            var start = Stopwatch.GetTimestamp();
            frame(i);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            nodes = Math.Max(nodes, t.Context.Stats.Nodes);
        }
        double perFrame = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)frames;
        Array.Sort(times);
        double p95 = times[(int)(frames * .95)];
        h.Timing($"{name}-p50-ms", times[frames / 2]);
        h.Timing($"{name}-p95-ms", p95);
        h.Timing($"{name}-max-ms", times[^1]);
        h.Timing($"{name}-kb-per-frame", perFrame / 1024);
        h.Timing($"{name}-nodes", nodes);
        h.Check(p95 < Budget, $"{name}: 95% of frames take under {Budget} ms ({p95:F2} ms)");
        h.Check(nodes < 3500, $"{name}: a frame builds only what the tiles need ({nodes} nodes)");
    }
}
