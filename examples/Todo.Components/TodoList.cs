using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Todo.Components;

public enum TodoFilter
{
    All,
    Active,
    Done
}

/// <summary>A titled list. Item IDs are unique within the list; <see cref="NextId"/> numbers new ones.</summary>
public sealed record TodoListModel(string Title, ImmutableArray<TodoItemModel> Items, string Draft = "", int NextId = 1,
    TodoFilter Filter = TodoFilter.All);

public abstract partial record TodoListMessage
{
    public sealed record ChangedDraft(string Text) : TodoListMessage;
    public sealed record SubmittedNew : TodoListMessage;
    public sealed record ChangedFilter(TodoFilter Filter) : TodoListMessage;
    public sealed record ClickedClearDone : TodoListMessage;
    /// <summary>A message from the item with <paramref name="Id"/>.</summary>
    public sealed record GotItemMessage(string Id, TodoItemMessage Message) : TodoListMessage;
}

/// <summary>A list of <see cref="TodoItem"/> rows with a field for new tasks, filters and a done count.</summary>
public sealed partial class TodoList : IKumikoComponent<TodoListModel, TodoListMessage>
{
    public static TodoListModel Init(string title, params ReadOnlySpan<(string Title, bool Done)> items)
    {
        var builder = ImmutableArray.CreateBuilder<TodoItemModel>(items.Length);
        foreach (var (task, done) in items)
            builder.Add(TodoItem.Init($"task-{builder.Count + 1}", task, done));
        return new(title, builder.MoveToImmutable(), NextId: items.Length + 1);
    }

    public static (TodoListModel, Cmd<TodoListMessage>) Update(TodoListModel list, TodoListMessage message) => message switch
    {
        TodoListMessage.ChangedDraft changed => (list with { Draft = changed.Text }, Cmd<TodoListMessage>.None),
        TodoListMessage.SubmittedNew when !string.IsNullOrWhiteSpace(list.Draft) => (list with
        {
            Items = list.Items.Add(TodoItem.Init($"task-{list.NextId}", list.Draft.Trim())),
            Draft = "",
            NextId = list.NextId + 1
        }, Cmd<TodoListMessage>.None),
        TodoListMessage.SubmittedNew => (list, Cmd<TodoListMessage>.None),
        TodoListMessage.ChangedFilter changed => (list with { Filter = changed.Filter }, Cmd<TodoListMessage>.None),
        TodoListMessage.ClickedClearDone => (list with { Items = list.Items.RemoveAll(static item => item.Done) }, Cmd<TodoListMessage>.None),
        TodoListMessage.GotItemMessage got => UpdateItem(list, got),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    static (TodoListModel, Cmd<TodoListMessage>) UpdateItem(TodoListModel list, TodoListMessage.GotItemMessage got)
    {
        for (var index = 0; index < list.Items.Length; index++)
        {
            if (list.Items[index].Id == got.Id)
                return UpdateItem(list, index, got.Message);
        }
        // The item was deleted while its message was queued.
        return (list, Cmd<TodoListMessage>.None);
    }

    static (TodoListModel, Cmd<TodoListMessage>) UpdateItem(TodoListModel list, int index, TodoItemMessage message)
    {
        var id = list.Items[index].Id;
        return TodoItem.Update(list.Items[index], message).Fold(
            item => list with { Items = list.Items.SetItem(index, item) },
            child => FromItem(id, child),
            (next, _) => (next with { Items = next.Items.RemoveAt(index) }, Cmd<TodoListMessage>.None));
    }

    static TodoListMessage FromItem(string id, TodoItemMessage message) => new TodoListMessage.GotItemMessage(id, message);
}

public sealed record TodoListProps(TodoListModel Value, float Width);
public sealed class TodoListView(TodoListProps props) : View<TodoListProps, TodoListMessage>(props)
{
    static readonly ImmutableArray<TodoFilter> Filters = [TodoFilter.All, TodoFilter.Active, TodoFilter.Done];
    protected override void Render()
    {
        var ui = Ui;
        var list = Model.Value;
        var width = Model.Width;
        var c = ui.Context;
        var inner = width - 2 * Space.S4;
        using var card = c.Div(key: "list").Width(width).Padding(Space.S4).Gap(Space.S3).Rounded(Radius.Lg)
            .Background(c.Theme.Surface).Border(c.Theme.Border).Semantics(Role.Group, list.Title);
        c.Text(list.Title, Typography.Title, key: "title");
        using (var form = ui.Form("new", new TodoListMessage.SubmittedNew(), label: "Add task"))
        {
            form.Layout.Row().ItemsCenter().Gap(Space.S2);
            ui.TextField("draft", "New task", list.Draft, static text => new TodoListMessage.ChangedDraft(text),
                new(Placeholder: "New task", Width: inner - 72));
            ui.Submit("add", "Add", new(ButtonVariant.Primary));
        }
        using (c.Div(key: "filters").Row().Gap(Space.S1))
            foreach (var filter in Filters)
                ui.Button(filter.ToString(), new TodoListMessage.ChangedFilter(filter),
                    new(filter == list.Filter ? ButtonVariant.Secondary : ButtonVariant.Ghost), key: filter.ToString());
        using (c.Div(key: "items").FillWidth().Gap(Space.S1).Semantics(Role.List, "Tasks"))
        {
            foreach (var item in list.Items)
                if (Shows(list.Filter, item))
                    ui.Child(new TodoItemView(new(item, inner)), item.Id, message => new TodoListMessage.GotItemMessage(item.Id, message));
            if (!list.Items.Any(item => Shows(list.Filter, item)))
                c.Text(list.Filter == TodoFilter.All ? "Nothing to do." : $"No {list.Filter.ToString().ToLowerInvariant()} tasks.",
                    new(Color: c.Theme.Muted), key: "empty");
        }
        var done = list.Items.Count(static item => item.Done);
        // A fixed height keeps the card still when Clear done appears.
        using (c.Div(key: "footer").Row().FillWidth().Height(36).ItemsCenter())
        {
            c.Text($"{done} of {list.Items.Length} done", new(Color: c.Theme.Muted), key: "count");
            c.Spacer();
            if (done > 0)
                ui.Button("Clear done", new TodoListMessage.ClickedClearDone(), new(ButtonVariant.Ghost), key: "clear");
        }
    }

    static bool Shows(TodoFilter filter, TodoItemModel item) => filter switch
    {
        TodoFilter.Active => !item.Done,
        TodoFilter.Done => item.Done,
        _ => true
    };
}
