using Kumiko.iOS;
using Kumiko.UI;

internal sealed partial class CatalogView
{
    void Presentations()
    {
        var c = Ui.Context;
        c.NativeSeparator(key: "presentation-separator");
        c.Heading("Presentations", size: Typography.Title.Size, key: "presentation-title");
        using (c.Div(key: "presentation-row").Row().Wrap().FillWidth().Gap(Space.S2))
        {
            Ui.Button("Show alert", new CatalogMessage.Present("alert"), key: "show-alert");
            Ui.Button("Rename…", new CatalogMessage.Present("rename"), key: "show-rename");
            Ui.Button("Delete…", new CatalogMessage.Present("delete"), new(ButtonVariant.Danger), key: "show-delete");
            // The dialog shares the button's scope, so iPad popovers anchor to that button.
            if (Model.Presented == "delete")
                Ui.NativeConfirmationDialog(null, "Delete the sample item?", CatalogApp.DeleteActions, "show-delete",
                    id => new CatalogMessage.PresentationAction(id), new CatalogMessage.PresentationDismissed(), key: "delete-dialog");
        }
        c.Label($"{Model.Name} · last result: {Model.LastPresentation}", key: "presentation-result");
        using var menu = Ui.NativeContextMenu("Sample card actions", CatalogApp.ContextMenu,
            id => new CatalogMessage.ContextCommand(id), title: Model.Name, key: "context-card").FillWidth();
        using var card = c.Div(key: "card").FillWidth().Padding(Space.S3).Gap(Space.S1).Rounded(12)
            .Background(Color.Hex(0x8e8e93).Alpha(.16f));
        c.Text(Model.Favorite ? $"★ {Model.Name}" : Model.Name, Typography.Title, key: "card-title");
        c.Text("Touch and hold for native actions.", key: "card-hint");
    }

    void Alerts()
    {
        var dismissed = new CatalogMessage.PresentationDismissed();
        switch (Model.Presented)
        {
            case "alert":
                Ui.NativeAlert("Discard note changes?", "The notes editor returns to its original text.",
                    CatalogApp.DiscardActions, id => new CatalogMessage.PresentationAction(id), dismissed, key: "discard-alert");
                break;
            case "rename":
                Ui.NativeAlert("Rename item", "Enter a short name.", CatalogApp.RenameActions(Model.Draft),
                    id => new CatalogMessage.PresentationAction(id), dismissed, [new("name", Model.Draft, "Name")],
                    static (_, value) => new CatalogMessage.DraftChanged(value), key: "rename-alert");
                break;
            case "reset":
                Ui.NativeAlert("Reset the catalog?", "Every value returns to its default.", CatalogApp.ResetActions,
                    id => new CatalogMessage.PresentationAction(id), dismissed, key: "reset-alert");
                break;
        }
    }
}
