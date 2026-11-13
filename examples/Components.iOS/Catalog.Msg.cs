internal abstract partial record CatalogMessage
{
    public sealed record NotificationsChanged(bool Value) : CatalogMessage;
    public sealed record VolumeChanged(float Value) : CatalogMessage;
    public sealed record QuantityChanged(double Value) : CatalogMessage;
    public sealed record SegmentChanged(string Id) : CatalogMessage;
    public sealed record ChoiceChanged(string Id) : CatalogMessage;
    public sealed record SearchChanged(string Value) : CatalogMessage;
    public sealed record NotesChanged(string Value) : CatalogMessage;
    public sealed record DateChanged(DateTimeOffset Value) : CatalogMessage;
    public sealed record PageChanged(int Value) : CatalogMessage;
    public sealed record Command(string Id) : CatalogMessage;
    public sealed record Close : CatalogMessage;
    public sealed record Present(string Id) : CatalogMessage;
    public sealed record PresentationAction(string Id) : CatalogMessage;
    public sealed record PresentationDismissed : CatalogMessage;
    public sealed record DraftChanged(string Value) : CatalogMessage;
    public sealed record ContextCommand(string Id) : CatalogMessage;
}
