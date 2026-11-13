using System.Collections.Immutable;
using Kumiko.iOS;
using Kumiko.Runtime;
using Kumiko.UI;
using Todo.Components;

namespace Todo;

/// <summary>The lists, their page path, and the Back request waiting for confirmation, if any.</summary>
public sealed record TodoScreenProps(ImmutableArray<NamedList> Lists, NavigationPath<TodoPage, TodoEntry> Path, BackRequest? Pending);

/// <summary>One page's lists and visit. <see cref="Pending"/> is set only on the top page that asked to leave.</summary>
public sealed record TodoPageProps(ImmutableArray<NamedList> Lists, TodoEntry Entry, BackRequest? Pending);

public sealed partial class TodoApp
{
    public static NavigationRoot<TodoMessage> Root(TodoModel model) => NavigationRoot<TodoMessage>.Create(model.Navigation, Host,
        _ => new TodoScreen(new(model.Lists, model.Path, model.Pending?.Request)).Map<TodoMessage>(static message => message),
        static request => new TodoMessage.Navigate(request));
}

/// <summary>Hosts the list pages in a UIKit navigation stack. Back from a page with a draft asks first.</summary>
internal sealed class TodoScreen(TodoScreenProps props) : Screen<TodoScreenProps, TodoMessage>(props)
{
    protected override void Render()
    {
        C.Theme = Theme.ShadcnLight;
        using var page = C.Div(key: "page").SizeFull().Background(C.Theme.Background);
        var lists = Model.Lists;
        Ui.Stack(Model.Path, Model, static (ui, props, entry) =>
            TodoPageRoutes.Render(ui, new(props.Lists, entry, entry.Id == props.Path.Top.Id ? props.Pending : null), entry.Route),
            static back => new TodoMessage.Back(back),
            static (props, entry) => entry.Route is TodoPage.Detail detail
                ? props.Lists.FirstOrDefault(list => list.Id == detail.Id)?.List.Title ?? "Todo" : "Todo",
            entry => TodoApp.Unsaved(lists, entry), TodoApp.Stack, key: "todo-stack");
    }
}

internal sealed class ListsPage : View<TodoPageProps, TodoMessage>
{
    internal ListsPage(TodoPageProps props, TodoPage.Lists route) : base(props)
    {
    }

    protected override void Render()
    {
        using var content = C.Div(key: "content").SizeFull().Padding(Space.S4).Gap(Space.S3);
        C.Heading("Todo", key: "heading");
        C.Text("One TodoList component, used twice. Each list keeps its own tasks.", Typography.Body with
        {
            Wrap = true
        }, key: "intro");
        ImmutableArray<NativeListItem> items = [.. Model.Lists.Select(static named => new NativeListItem(named.Id, named.List.Title,
            $"{named.List.Items.Count(static item => item.Done)} of {named.List.Items.Length} done"))];
        Ui.NativeList("Lists", items, null, static id => new TodoMessage.Open(id), key: "lists");
    }
}

internal sealed class DetailPage(TodoPageProps props, TodoPage.Detail route) : View<TodoPageProps, TodoMessage>(props)
{
    protected override void Render()
    {
        using var scroll = C.Div(key: "detail-scroll").SizeFull().Scroll().Padding(Space.S4).Gap(Space.S3);
        if (Model.Lists.FirstOrDefault(list => list.Id == route.Id) is not { } named)
        {
            C.Text("This list is no longer available.", key: "missing-list");
            return;
        }
        var id = named.Id;
        Ui.Child(new TodoListView(new(named.List, Math.Min(480, C.Size.Width - 2 * Space.S4))), id,
            message => new TodoMessage.GotList(id, message));
        if (Model.Pending is { } pending)
            Confirmation(pending);
    }

    void Confirmation(BackRequest pending)
    {
        C.Label("Discard unsaved changes?", key: "confirmation");
        using var actions = C.Div(key: "confirmation-actions").Row().Gap(Space.S2);
        Ui.Button("Keep editing", new TodoMessage.CancelBack(), key: "keep");
        Ui.Button("Discard and leave", new TodoMessage.ConfirmBack(pending), new(ButtonVariant.Danger), key: "discard");
    }
}
