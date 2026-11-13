using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;

internal sealed record ComponentsProps(ImmutableArray<NativeListItem> Items, string? SelectedId);

internal sealed class ComponentsView(ComponentsProps props) : Screen<ComponentsProps, ComponentsMessage>(props)
{
    protected override void Render()
    {
        var c = Ui.Context;
        using var page = c.Div(key: "page").SizeFull().Padding(Space.S4).Gap(Space.S4);
        c.Heading("My list", key: "heading");
        c.Text("A small UIKit example. Add an item or select one to open its drawer.",
            Typography.Body with
            {
                Wrap = true
            }, key: "intro");
        Ui.Button("Add item", new ComponentsMessage.Add(), new(ButtonVariant.Primary), key: "add");
        if (Model.Items.IsEmpty)
            c.Text("Your list is empty. Add an item to get started.", Typography.Body with
            {
                Wrap = true
            }, key: "empty");
        Ui.NativeList("Items", Model.Items, Model.SelectedId, id => new ComponentsMessage.Select(id), key: "items");
        c.Label($"{Model.Items.Length} items · Demo data", key: "count");
        if (Model.Items.FirstOrDefault(item => item.Id == Model.SelectedId) is { } selected)
        {
            using var drawer = Ui.NativeDrawer("Item details", new ComponentsMessage.Close(), key: "details");
            c.Text(selected.Title, Typography.Title with
            {
                Wrap = true
            }, key: "item-title");
            c.Text(selected.Detail ?? "No details.", Typography.Body with
            {
                Wrap = true
            }, key: "detail");
            c.Spacer();
            Ui.Button("Remove item", new ComponentsMessage.RemoveSelected(), new(ButtonVariant.Danger), key: "remove");
        }
    }
}
