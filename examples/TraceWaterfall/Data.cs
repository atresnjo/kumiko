using System.Collections.Immutable;
using Kumiko.Icons;

namespace TraceWaterfall;

/// <summary>Illustrative trace matching the public demo's story. No tools or model APIs are called.</summary>
static class Data
{
    internal static ImmutableArray<TraceSpan> Spans =>
    [
        new("root", null, "Orchestrator", "Opus 5.5", SpanKind.Agent, 0, 92000,
            Input: "Launch the new pricing page by Friday", Output: "Pricing page ready for review. Checkout tests pass after coupon repair.", Icon: Hugeicons.AiBrain01),
        new("plan", "root", "Plan the launch", "Opus 5.5", SpanKind.Llm, 900, 7800, .12m, 8200, 2700,
            "Break the launch into research, writing, implementation and review.", "Run research, writing and implementation in parallel.", Icon: Hugeicons.AiBrain01),
        new("research", "root", "Researcher", "Sonnet 5.5", SpanKind.Agent, 8200, 38000,
            Input: "Compare competitor pricing and current plan usage.", Output: "Research brief completed.", Icon: Hugeicons.AiBrain01),
        new("linear", "research", "web_search", "Linear pricing", SpanKind.Tool, 9100, 14500, .005m,
            Input: "Linear pricing plans", Output: "Free, Basic, Business and Enterprise plans.", Icon: Hugeicons.Search01),
        new("notion", "research", "web_search", "Notion pricing", SpanKind.Tool, 9400, 16600, .005m,
            Input: "Notion pricing plans", Output: "Free, Plus, Business and Enterprise plans.", Icon: Hugeicons.Search01),
        new("usage", "research", "Query plan usage", "Postgres", SpanKind.Tool, 9800, 13300,
            Input: "SELECT plan, count(*) FROM demo_accounts GROUP BY plan", Output: "Illustrative usage: Free 68%, Pro 26%, Business 6%.", Icon: Hugeicons.Database),
        new("reviews", "research", "Read G2 reviews", "120 pages", SpanKind.Tool, 15100, 27900, .02m,
            Input: "Summarize recurring pricing feedback.", Output: "Clear seat pricing and predictable usage limits matter most.", Icon: Hugeicons.Globe02),
        new("summary", "research", "Summarize findings", "Gemini 3.8 Flash", SpanKind.Llm, 28500, 37800, .02m, 18400, 1600,
            "Produce a concise research brief.", "Lead with clear plan limits and a self-serve upgrade path.", Icon: Hugeicons.Sparkles),
        new("writer", "root", "Writer", "Sonnet 5.5", SpanKind.Agent, 11500, 48500,
            Input: "Draft the new pricing page in our brand voice.", Output: "Draft saved to the launch workspace.", Icon: Hugeicons.AiBrain01),
        new("voice", "writer", "Read brand voice", "Notion", SpanKind.Tool, 12100, 15700,
            Input: "Read the brand voice guide.", Output: "Direct, clear, friendly. Explain limits without jargon.", Icon: Hugeicons.FileCode),
        new("frames", "writer", "Read pricing frames", "Figma", SpanKind.Tool, 12400, 17500,
            Input: "Read the approved desktop pricing frames.", Output: "Three plan columns, monthly/annual toggle, comparison table.", Icon: Hugeicons.CodeSquare),
        new("copy", "writer", "Draft plan copy", "Sonnet 5.5", SpanKind.Llm, 18100, 41500, .09m, 23000, 5400,
            "Write plan descriptions, limits and upgrade copy.", "Pricing copy ready: clear tiers, transparent limits and a helpful FAQ.", Icon: Hugeicons.AiBrain01),
        new("save", "writer", "Save draft", "Notion", SpanKind.Tool, 42200, 45800,
            Input: "Save pricing-copy.md to the demo workspace.", Output: "Draft saved.", Icon: Hugeicons.FileCode),
        new("coder", "root", "Coder", "GPT-6 Sol", SpanKind.Agent, 15700, 78500,
            Input: "Implement pricing and verify checkout.", Output: "Implementation and coupon repair verified.", Icon: Hugeicons.Code),
        new("branch", "coder", "Create branch", "GitHub", SpanKind.Tool, 16300, 18400,
            Input: "Create demo/pricing-page", Output: "Illustrative branch created. No repository was modified.", Icon: Hugeicons.Github),
        new("page", "coder", "Write pricing page", "GPT-6 Sol", SpanKind.Llm, 18700, 38600, .16m, 26000, 11200,
            "Implement the pricing frames and seat-based checkout.", "Pricing page and checkout implementation prepared.", Icon: Hugeicons.Code),
        new("prices", "coder", "Create prices", "Stripe", SpanKind.Tool, 39100, 42100,
            Input: "Create sandbox prices for Pro and Business.", Output: "Sandbox price definitions prepared.", Icon: Hugeicons.CodeSquare),
        new("tests", "coder", "Run checkout tests", "GitHub", SpanKind.Tool, 43000, 51400,
            Input: "pnpm test checkout", Error: "2 of 14 checkout tests fail — coupon codes aren't applied to seat prices", Icon: Hugeicons.Github),
        new("fix", "coder", "Fix coupon handling", "GPT-6 Sol", SpanKind.Llm, 52200, 60000, .07m, 8400, 3200,
            "Apply coupons to seat prices and preserve billing intervals.", "Coupon handling corrected. Original failed call retained in the trace.", Icon: Hugeicons.Code),
        new("retest", "coder", "Re-run checkout tests", "GitHub", SpanKind.Tool, 61400, 69800,
            Input: "pnpm test checkout", Output: "14 of 14 checkout tests pass.", Icon: Hugeicons.Github),
        new("review", "root", "Review launch", "Opus 5.5", SpanKind.Llm, 79000, 90200, .08m, 12200, 3400,
            "Review research, copy and the verified implementation.", "Ready for human review. No deployment performed.", Icon: Hugeicons.AiBrain01)
    ];
}
