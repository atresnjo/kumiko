using Kumiko.Runtime;

internal sealed partial class CatalogApp : IKumiko<CatalogModel, CatalogMessage>
{
    public static Kumiko.Runtime.NavigationRoot<CatalogMessage> Root(CatalogModel model) => Kumiko.Runtime.NavigationRoot<CatalogMessage>.Create(
        Kumiko.Runtime.NavigationHistory<string>.Start("root"), "app", _ => new CatalogView(model).Map<CatalogMessage>(static message => message),
        static _ => throw new InvalidOperationException("This application has one root screen."));
    public static CatalogModel Init => new(Date: new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public static (CatalogModel, Cmd<CatalogMessage>) Update(CatalogModel model, CatalogMessage message)
    {
        var next = message switch
        {
            CatalogMessage.NotificationsChanged change => model with { Notifications = change.Value },
            CatalogMessage.VolumeChanged change => model with { Volume = change.Value },
            CatalogMessage.QuantityChanged change => model with { Quantity = change.Value },
            CatalogMessage.SegmentChanged change => model with { Segment = change.Id },
            CatalogMessage.ChoiceChanged change => model with { Choice = change.Id },
            CatalogMessage.SearchChanged change => model with { Search = change.Value },
            CatalogMessage.NotesChanged change => model with { Notes = change.Value },
            CatalogMessage.DateChanged change => model with { Date = change.Value },
            CatalogMessage.PageChanged change => model with { Page = change.Value },
            CatalogMessage.Close => model with { Details = false },
            CatalogMessage.Present present => model with
            {
                Presented = present.Id,
                Draft = present.Id == "rename" ? model.Name : model.Draft
            },
            CatalogMessage.PresentationAction chosen => Presented(model, chosen.Id),
            CatalogMessage.PresentationDismissed => model with
            {
                Presented = null,
                Dismissals = model.Dismissals + 1,
                LastPresentation = "dismissed"
            },
            CatalogMessage.DraftChanged change => model with { Draft = change.Value },
            CatalogMessage.ContextCommand menu => menu.Id == "favorite" ? model with { Favorite = !model.Favorite } : model,
            CatalogMessage.Command command => command.Id switch
            {
                "reset" => Init,
                "reverse" => model with { Reversed = !model.Reversed },
                "enable" => model with { Enabled = !model.Enabled },
                "visible" => model with { Visible = !model.Visible },
                "busy" => model with { Busy = !model.Busy },
                "details" => model with { Details = true, Presented = null },
                _ => model
            },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        return (next with
        {
            Edits = model.Edits + 1,
            LastCommand = message is CatalogMessage.Command action ? action.Id :
                message is CatalogMessage.ContextCommand context ? context.Id : model.LastCommand
        }, Cmd<CatalogMessage>.None);
    }

    // Every action ends its presentation; the reset alert also closes the sheet beneath it.
    static CatalogModel Presented(CatalogModel model, string id)
    {
        var next = model with
        {
            Presented = null,
            LastPresentation = id
        };
        return id switch
        {
            "discard" => next with { Notes = Init.Notes },
            "rename" => next with { Name = model.Draft.Trim() },
            "reset" => Init with { LastPresentation = id, Edits = model.Edits },
            _ => next
        };
    }
}
