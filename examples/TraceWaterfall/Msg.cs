namespace TraceWaterfall;

public abstract partial record TraceMsg
{
    public sealed record Tick : TraceMsg;
    public sealed record TogglePlaying : TraceMsg;
    public sealed record Restart : TraceMsg;
    public sealed record Complete : TraceMsg;
    public sealed record ToggleTheme : TraceMsg;
    public sealed record ToggleBranch(string Id) : TraceMsg;
    public sealed record Open(string Id) : TraceMsg;
    public sealed record Close : TraceMsg;
    public sealed record FocusRestored : TraceMsg;
}
