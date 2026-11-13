using System.Collections.Immutable;

namespace TraceWaterfall;

public enum SpanKind
{
    Agent, Llm, Tool
}

/// <summary>Times are milliseconds from the run's start; costs and tokens belong to this call only.</summary>
public sealed record TraceSpan(string Id, string? Parent, string Name, string Detail, SpanKind Kind,
    int Start, int End, decimal Cost = 0, int TokensIn = 0, int TokensOut = 0,
    string Input = "", string Output = "", string? Error = null, string Icon = "");

public sealed record TraceModel(ImmutableArray<TraceSpan> Spans, int Now, bool Playing, bool Dark,
    ImmutableHashSet<string> Collapsed, string? OpenId = null, string? FocusId = null, bool RestoreFocus = false);

public readonly record struct SpanTotals(decimal Cost, int TokensIn, int TokensOut, int LlmCalls, int ToolCalls, int Errors);
