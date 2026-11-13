using System.Collections.Immutable;
using System.Globalization;

namespace TraceWaterfall;

static class TraceQueries
{
    internal static int Depth(TraceModel model, TraceSpan span) => span.Parent is { } parent
        ? 1 + Depth(model, model.Spans.First(s => s.Id == parent)) : 0;

    internal static bool DescendsFrom(TraceModel model, TraceSpan span, string parent) => span.Parent is { } id &&
        (id == parent || DescendsFrom(model, model.Spans.First(s => s.Id == id), parent));

    internal static bool Visible(TraceModel model, TraceSpan span) => span.Start <= model.Now &&
        (span.Parent is not { } parent || !model.Collapsed.Contains(parent) && Visible(model, model.Spans.First(s => s.Id == parent)));

    internal static ImmutableArray<TraceSpan> Rows(TraceModel model) => [.. model.Spans.Where(s => Visible(model, s))];
    internal static int Duration(TraceModel model, TraceSpan span) => Math.Max(0, Math.Min(model.Now, span.End) - span.Start);
    internal static float Progress(TraceModel model, TraceSpan span) => Math.Clamp((float)Duration(model, span) / (span.End - span.Start), 0, 1);
    internal static bool Failed(TraceModel model, TraceSpan span) => span.Error is not null && model.Now >= span.End;

    internal static SpanTotals Totals(TraceModel model, TraceSpan root)
    {
        var calls = model.Spans.Where(s => s.Start <= model.Now && (s.Id == root.Id || DescendsFrom(model, s, root.Id))).ToImmutableArray();
        return new(calls.Sum(s => s.Cost * (decimal)Progress(model, s)),
            (int)calls.Sum(s => s.TokensIn * Progress(model, s)), (int)calls.Sum(s => s.TokensOut * Progress(model, s)),
            calls.Count(s => s.Kind == SpanKind.Llm), calls.Count(s => s.Kind == SpanKind.Tool), calls.Count(s => Failed(model, s)));
    }

    internal static string? Slowest(TraceModel model) => model.Spans
        .Where(s => s.Kind != SpanKind.Agent && s.End <= model.Now).OrderByDescending(s => s.End - s.Start).FirstOrDefault()?.Id;

    internal static int Range(int now) => now <= 8000 ? 10000 : now <= 16000 ? 20000 : now <= 32000 ? 40000 : now <= 48000 ? 60000 : 120000;

    internal static string Time(int milliseconds) => milliseconds < 1000 ? $"{milliseconds}ms" : milliseconds < 60000
        ? (milliseconds / 1000d).ToString(milliseconds < 10000 ? "0.0's'" : "0's'", CultureInfo.InvariantCulture)
        : $"{milliseconds / 60000}m {(milliseconds % 60000) / 1000}s";
    internal static string Cost(decimal cost) => "$" + cost.ToString(cost < .01m ? "0.000" : "0.00", CultureInfo.InvariantCulture);
    internal static string Tokens(int count) => (count / 1000d).ToString("0.0'k'", CultureInfo.InvariantCulture);
}
