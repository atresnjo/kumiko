using System.Text.Json;
using System.Text.Json.Serialization;
using CoreAnimation;
using Foundation;
using UIKit;

internal sealed class FramePacingProbe : IDisposable
{
    internal sealed record Sample(string Phase, double Time, double Timestamp, double IntervalMs,
        double ArrivalGapMs, double BudgetMs, double CallbackLatenessMs, double WorkMs, double PresentedX, double PresentedY);
    readonly List<Sample> samples = [];
    readonly CADisplayLink link;
    double lastTimestamp;
    double lastArrival;
    Exception? error;
    internal string Phase { get; set; } = "warmup";
    internal Action<double>? Drive
    {
        get; set;
    }
    internal UIView? Card
    {
        get; set;
    }

    internal FramePacingProbe()
    {
        link = CADisplayLink.Create(Tick);
        link.PreferredFrameRateRange = CAFrameRateRange.Create(60, 60, 60);
        link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
    }

    void Tick()
    {
        try
        {
            var now = CAAnimation.CurrentMediaTime();
            var timestamp = link.Timestamp;
            var period = link.TargetTimestamp - timestamp;
            var elapsed = lastTimestamp > 0 ? (timestamp - lastTimestamp) * 1000 : 0;
            var gap = lastArrival > 0 ? (now - lastArrival) * 1000 : 0;
            lastTimestamp = timestamp;
            lastArrival = now;
            var shown = Card?.Layer.PresentationLayer?.Position ?? Card?.Layer.Position ?? default;
            var start = CAAnimation.CurrentMediaTime();
            Drive?.Invoke(now);
            var work = (CAAnimation.CurrentMediaTime() - start) * 1000;
            samples.Add(new(Phase, now, timestamp, elapsed, gap, period * 1000,
                Math.Max(0, now - timestamp) * 1000, work, shown.X, shown.Y));
        }
        catch (Exception exception)
        {
            error = exception;
            link.Paused = true;
        }
    }

    internal void Save(string output, int refreshRate, int nativeViewCount, string prefix = "slack", string? workload = null)
    {
        link.Invalidate();
        if (error is not null)
            throw new InvalidOperationException("Frame-pacing callback failed.", error);
        var groups = samples.Where(sample => sample.Phase != "warmup" && sample.IntervalMs > 0)
            .GroupBy(sample => sample.Phase).Select(group =>
            {
                var values = group.ToArray();
                var intervals = values.Select(sample => sample.IntervalMs).Order().ToArray();
                var work = values.Select(sample => sample.WorkMs).Order().ToArray();
                var missed = values.Sum(sample => Math.Max(0, (int)Math.Round(sample.IntervalMs / (1000.0 / 60)) - 1));
                return new PhaseResult(group.Key, values.Length, 1000 / intervals.Average(), Percentile(intervals, .5),
                    Percentile(intervals, .95), Percentile(intervals, .99), intervals.Max(), missed,
                    values.Count(sample => sample.IntervalMs > 25), values.Count(sample => sample.ArrivalGapMs > 50),
                    Percentile(work, .95), work.Max());
            }).ToArray();
        Directory.CreateDirectory(output);
        workload ??= Environment.GetEnvironmentVariable("KUMIKO_SLACK_PACED") == "1"
            ? "250 ms display-link-paced swipe approach" : "instant begin/end gesture burst";
        var report = new PacingReport("Release iOS simulator", workload, 60, refreshRate, nativeViewCount, groups,
            "CADisplayLink callback/frame pacing and controlled native pan input; not an on-device GPU presentation FPS certification.");
        File.WriteAllText(Path.Combine(output, prefix + "-frame-pacing.json"), JsonSerializer.Serialize(report, PacingJson.Default.PacingReport));
        using var csv = new StreamWriter(Path.Combine(output, prefix + "-frame-pacing.csv"));
        csv.WriteLine("phase,time,timestamp,interval_ms,arrival_gap_ms,budget_ms,callback_lateness_ms,drive_work_ms,presentation_x,presentation_y");
        foreach (var sample in samples)
            csv.WriteLine(FormattableString.Invariant($"{sample.Phase},{sample.Time:F6},{sample.Timestamp:F6},{sample.IntervalMs:F4},{sample.ArrivalGapMs:F4},{sample.BudgetMs:F4},{sample.CallbackLatenessMs:F4},{sample.WorkMs:F4},{sample.PresentedX:F4},{sample.PresentedY:F4}"));
    }

    static double Percentile(double[] sorted, double quantile) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * quantile) - 1)];

    public void Dispose()
    {
        link.Invalidate();
        link.Dispose();
    }
}

internal sealed record PhaseResult(string Phase, int Samples, double CallbackFps, double MedianIntervalMs,
    double P95IntervalMs, double P99IntervalMs, double MaxIntervalMs, int Missed60HzSlots, int GapsOver25Ms,
    int ArrivalGapsOver50Ms, double P95DriveWorkMs, double MaxDriveWorkMs);
internal sealed record PacingReport(string Environment, string Workload, int TargetFps, int ReportedRefreshRate, int NativeViewCount,
    PhaseResult[] Phases, string MeasurementScope);

[JsonSerializable(typeof(PacingReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class PacingJson : JsonSerializerContext;
