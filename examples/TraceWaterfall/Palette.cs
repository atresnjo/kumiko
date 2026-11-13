using Kumiko.Components;
using Kumiko.UI;

namespace TraceWaterfall;

sealed record Palette(bool Dark)
{
    internal Color Page => Color.Hex(Dark ? 0x141416u : 0xf8f9fau);
    internal Color Surface => Color.Hex(Dark ? 0x19191bu : 0xffffffu);
    internal Color Text => Color.Hex(Dark ? 0xf1f1f3u : 0x202124u);
    internal Color Muted => Color.Hex(Dark ? 0xa0a0a9u : 0x696a73u);
    internal Color Border => Color.Hex(Dark ? 0x303034u : 0xe7e8ebu);
    internal Color Grid => Color.Hex(Dark ? 0x222225u : 0xf1f2f4u);
    internal Color Hover => Color.Hex(Dark ? 0x242b36u : 0xf0f6ffu);
    internal Color Blue => Color.Hex(Dark ? 0x8ab8ffu : 0x2565b9u);
    internal Color Danger => Color.Hex(Dark ? 0xff9b95u : 0xb42318u);
    internal Color DangerFill => Color.Hex(Dark ? 0x3d2224u : 0xfdecebu);
    internal Color Slow => Color.Hex(Dark ? 0xffb16cu : 0x914509u);
    internal Color SlowFill => Color.Hex(Dark ? 0x3a2b21u : 0xfff1e3u);
    internal Color Tint(SpanKind kind) => kind switch
    {
        SpanKind.Llm => Color.Hex(Dark ? 0xb4a3f9u : 0x7963b7u),
        SpanKind.Tool => Color.Hex(Dark ? 0x6ac2d3u : 0x397f90u),
        _ => Muted
    };
    internal Color Fill(SpanKind kind) => kind switch
    {
        SpanKind.Llm => Color.Hex(Dark ? 0x2d2843u : 0xeeeafbu),
        SpanKind.Tool => Color.Hex(Dark ? 0x1e343cu : 0xe5f3f6u),
        _ => Color.Hex(Dark ? 0x29292cu : 0xf0f0f1u)
    };
    internal KitTheme Kit => (Dark ? KitTheme.Dark : KitTheme.Light) with
    {
        Background = Page,
        Foreground = Text,
        Card = Surface,
        CardForeground = Text,
        Popover = Surface,
        PopoverForeground = Text,
        Muted = Grid,
        MutedForeground = Muted,
        Border = Border,
        Accent = Hover,
        AccentForeground = Text,
        Ring = Blue,
        Radius = 8,
        FontSize = 13
    };
    internal const float Row = 32;
    internal const float Inset = 28;
    internal const float Caption = 12;
    internal const float Body = 14;
    internal const float Heading = 18;
    internal const float Metric = 23;
}
