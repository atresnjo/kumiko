using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal sealed partial class CatalogView(CatalogModel model) : Kumiko.Runtime.Screen<CatalogModel, CatalogMessage>(model)
{
    protected override void Render()
    {
        var c = Ui.Context;
        using var page = c.Div(key: "catalog").SizeFull();
        Ui.NativeNavigationBar("UIKit components", CatalogApp.Navigation, id => new CatalogMessage.Command(id), key: "navigation");
        using (c.Div(key: "scroll").FillWidth().Flex().Scroll())
        using (c.Div(key: "content").FillWidth().Padding(Space.S4).Gap(Space.S3))
        {
            c.Text("System controls, native behavior", Typography.Title, key: "title");
            c.Text("Every control below is a real UIKit view. Changes update the Model.",
                Typography.Body with
                {
                    Wrap = true
                }, key: "intro");
            if (Model.Visible)
                Inputs();
            c.NativeSeparator(key: "separator");
            c.Heading("Status and content", size: Typography.Title.Size, key: "status-title");
            c.Label($"Volume: {Model.Volume:P0}", key: "progress-label");
            c.NativeProgress("Volume level", Model.Volume, key: "progress");
            using (c.Div(key: "status-row").Row().FillWidth().ItemsCenter().Gap(Space.S3))
            {
                c.NativeSymbol("checkmark.seal.fill", accessibleName: "Native UIKit", key: "symbol");
                c.NativeActivityIndicator("Background activity", Model.Busy, key: "activity");
                c.Text(Model.Busy ? "Working" : "Ready", key: "activity-label");
            }
            Ui.NativePageControl("Preview page", 4, Model.Page, value => new CatalogMessage.PageChanged(value), key: "pages");
            Ui.NativeMenu("Actions", CatalogApp.Menu, id => new CatalogMessage.Command(id), key: "menu");
            c.Label($"Last action: {Model.LastCommand}", key: "last-command");
            Presentations();
        }
        Ui.NativeToolbar("Catalog actions", CatalogApp.Toolbar, id => new CatalogMessage.Command(id), key: "toolbar");
        if (Model.Details)
        {
            using var drawer = Ui.NativeDrawer("About this catalog", new CatalogMessage.Close(), key: "details");
            c.Text("UIKit owns control appearance, feedback and accessibility. Kumiko owns layout and Model updates.",
                Typography.Body with
                {
                    Wrap = true
                }, key: "about");
            Ui.Button("Reset catalog…", new CatalogMessage.Present("reset"), key: "show-reset");
        }
        // Alerts follow every sheet so UIKit presents them on top.
        Alerts();
    }

    void Inputs()
    {
        var c = Ui.Context;
        var disabled = !Model.Enabled;
        c.Heading("Selection and input", size: Typography.Title.Size, key: "input-title");
        using (c.Div(key: "switch-row").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            using (c.Div(key: "switch-label").Flex())
                c.Text("Notifications", key: "text");
            Ui.NativeSwitch("Notifications", Model.Notifications, value => new CatalogMessage.NotificationsChanged(value), disabled, key: "switch");
        }
        Ui.NativeSlider("Volume", Model.Volume, value => new CatalogMessage.VolumeChanged(value), disabled: disabled, key: "slider");
        using (c.Div(key: "stepper-row").Row().FillWidth().ItemsCenter().Gap(Space.S3))
        {
            using (c.Div(key: "stepper-label").Flex())
                c.Text($"Quantity: {Model.Quantity}", key: "text");
            Ui.NativeStepper("Quantity", Model.Quantity, value => new CatalogMessage.QuantityChanged(value),
                maximum: 10, disabled: disabled, key: "stepper");
        }
        Ui.NativeSegmentedControl("Period", Model.Reversed ? CatalogApp.Segments.Reverse().ToImmutableArray() : CatalogApp.Segments,
            Model.Segment, id => new CatalogMessage.SegmentChanged(id), disabled, key: "segments");
        c.Label("Category", key: "picker-label");
        Ui.NativePicker("Category", Model.Reversed ? CatalogApp.Choices.Reverse().ToImmutableArray() : CatalogApp.Choices,
            Model.Choice, id => new CatalogMessage.ChoiceChanged(id), disabled, key: "picker");
        c.Label("Date", key: "date-label");
        Ui.NativeDatePicker("Date", Model.Date, value => new CatalogMessage.DateChanged(value), disabled: disabled, key: "date");
        Ui.NativeSearchField("Search catalog", Model.Search, value => new CatalogMessage.SearchChanged(value),
            disabled: disabled, key: "search");
        c.Label("Notes", key: "notes-label");
        Ui.NativeTextEditor("Notes", Model.Notes, value => new CatalogMessage.NotesChanged(value), disabled: disabled, key: "editor");
    }
}
