using Kumiko.Runtime;

internal abstract partial record NativeButtonMessage
{
    public sealed record Increment : NativeButtonMessage;
    public sealed record SetEnabled(bool Value) : NativeButtonMessage;
    public sealed record SetVisible(bool Value) : NativeButtonMessage;
    public sealed record Navigate(HistoryRequest Request) : NativeButtonMessage;
}
