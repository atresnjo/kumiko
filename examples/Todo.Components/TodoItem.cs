using Kumiko.Runtime;
using Kumiko.UI;

namespace Todo.Components;

/// <summary>One task. <see cref="Draft"/> is the title being edited, or null when the row is not editing.</summary>
public sealed record TodoItemModel(string Id, string Title, bool Done = false, string? Draft = null);

public abstract partial record TodoItemMessage
{
    public sealed record Toggled(bool Done) : TodoItemMessage;
    public sealed record ClickedEdit : TodoItemMessage;
    public sealed record ChangedDraft(string Text) : TodoItemMessage;
    public sealed record SubmittedEdit : TodoItemMessage;
    public sealed record ClickedCancel : TodoItemMessage;
    public sealed record ClickedDelete : TodoItemMessage;
}

/// <summary>What an item tells the list that owns it. Only the list can remove the item.</summary>
public abstract record TodoItemOutMessage
{
    public sealed record Deleted : TodoItemOutMessage;
}

/// <summary>A task row that can be ticked, renamed in place and deleted.</summary>
public sealed partial class TodoItem : IKumikoComponent<TodoItemModel, TodoItemMessage, TodoItemOutMessage>
{
    public static TodoItemModel Init(string id, string title, bool done = false) => new(id, title, done);

    public static Outcome<TodoItemModel, TodoItemMessage, TodoItemOutMessage> Update(TodoItemModel item, TodoItemMessage message) => message switch
    {
        TodoItemMessage.Toggled toggled => new(item with { Done = toggled.Done }),
        TodoItemMessage.ClickedEdit => new(item with { Draft = item.Title }),
        TodoItemMessage.ChangedDraft changed => new(item with { Draft = changed.Text }),
        TodoItemMessage.SubmittedEdit when !string.IsNullOrWhiteSpace(item.Draft) => new(item with { Title = item.Draft.Trim(), Draft = null }),
        TodoItemMessage.SubmittedEdit => new(item),
        TodoItemMessage.ClickedCancel => new(item with { Draft = null }),
        TodoItemMessage.ClickedDelete => new(item, outMessage: new TodoItemOutMessage.Deleted()),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

}

public sealed record TodoItemProps(TodoItemModel Value, float Width);
public sealed class TodoItemView(TodoItemProps props) : View<TodoItemProps, TodoItemMessage>(props)
{
    protected override void Render()
    {
        var ui = Ui;
        var item = Model.Value;
        var width = Model.Width;
        var c = ui.Context;
        using var row = c.Div(key: "item").Row().Width(width).Height(44).ItemsCenter().Gap(Space.S2).Px(Space.S2)
            .Rounded(Radius.Md).Semantics(Role.ListItem, item.Title);
        if (item.Draft is { } draft)
        {
            using (var form = ui.Form("edit", new TodoItemMessage.SubmittedEdit(), label: "Edit task"))
            {
                form.Layout.Row().Flex().ItemsCenter().Gap(Space.S2);
                ui.TextField("title", "Title", draft, static text => new TodoItemMessage.ChangedDraft(text),
                    new(Placeholder: "Title", Width: Math.Max(80, width - 168)));
                ui.Submit("save", "Save", new(ButtonVariant.Primary));
            }
            ui.Button("Cancel", new TodoItemMessage.ClickedCancel(), new(ButtonVariant.Ghost), key: "cancel");
            return;
        }
        using (c.Div(key: "check").Flex())
            ui.Checkbox(item.Title, item.Done, static done => new TodoItemMessage.Toggled(done), key: "done");
        ui.Button("Edit", new TodoItemMessage.ClickedEdit(), new(ButtonVariant.Ghost), key: "edit");
        ui.Button("Delete", new TodoItemMessage.ClickedDelete(), new(ButtonVariant.Ghost), key: "delete");
    }
}
