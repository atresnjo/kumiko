using Kumiko.Runtime;
using Todo.Components;

namespace Todo;

public sealed partial class TodoApp : IKumiko<TodoModel, TodoMessage>
{
    /// <summary>The root navigation host key; history requests from any other host are ignored.</summary>
    internal const string Host = "app";
    /// <summary>The page stack's key; Back requests for any other stack are ignored.</summary>
    internal const string Stack = "main";

    public static TodoModel Init => new([
        new("work", TodoList.Init("Work", ("Write report", false), ("Review pull request", false), ("Plan sprint", true))),
        new("home", TodoList.Init("Home", ("Buy groceries", false), ("Call the plumber", false), ("Water plants", true)))]);

    public static (TodoModel, Cmd<TodoMessage>) Update(TodoModel model, TodoMessage message) => message switch
    {
        TodoMessage.Open open => (Open(model, open.Id), Cmd<TodoMessage>.None),
        TodoMessage.GotList got => UpdateList(model, got),
        TodoMessage.Back back => (Back(model, back.Request), Cmd<TodoMessage>.None),
        TodoMessage.ConfirmBack confirm => (Confirm(model, confirm.Request), Cmd<TodoMessage>.None),
        TodoMessage.CancelBack => (model with { Pending = null }, Cmd<TodoMessage>.None),
        TodoMessage.Navigate navigate => (model with { Navigation = model.Navigation.Move(navigate.Request, Host) }, Cmd<TodoMessage>.None),
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    };

    /// <summary>Opens a list's detail from the overview; anywhere else, or while confirming, it does nothing.</summary>
    static TodoModel Open(TodoModel model, string id) => model.Page is TodoPage.Lists && model.Pending is null && Find(model, id) >= 0
        ? model with
        {
            Path = model.Path.Push(new TodoPage.Detail(id))
        }
        : model;

    /// <summary>Applies a current Back request, or holds it for confirmation when a removed page has a draft.</summary>
    static TodoModel Back(TodoModel model, BackRequest request)
    {
        if (model.Path.Back(request, Stack) is not { } desired)
            return model;
        var lists = model.Lists;
        return model.Path.RequiresConfirmation(desired, entry => Unsaved(lists, entry))
            ? model with
            {
                Pending = new(request, desired)
            }
            : model with
            {
                Path = desired,
                Pending = null
            };
    }

    /// <summary>Leaves only for the exact pending request, discarding the drafts of the pages it removes.</summary>
    static TodoModel Confirm(TodoModel model, BackRequest request)
    {
        if (model.Pending?.Confirm(model.Path, request, Stack) is not { } desired)
            return model;
        var lists = model.Lists;
        foreach (var entry in model.Path.Entries)
            if (desired.IndexOf(entry.Id) < 0 && entry.Route is TodoPage.Detail detail && Find(model, detail.Id) is var index and >= 0)
                lists = lists.SetItem(index, lists[index] with
                {
                    List = Discard(lists[index].List)
                });
        return model with
        {
            Lists = lists,
            Path = desired,
            Pending = null
        };
    }

    /// <summary>Editing a list withdraws a confirmation that would discard that list's drafts.</summary>
    static (TodoModel, Cmd<TodoMessage>) UpdateList(TodoModel model, TodoMessage.GotList got)
    {
        var index = Find(model, got.Id);
        if (index < 0)
            return (model, Cmd<TodoMessage>.None);
        var id = got.Id;
        var withdrawn = model.Pending is { } pending &&
            model.Path.Entries.Any(entry => entry.Route is TodoPage.Detail detail && detail.Id == id && pending.Desired.IndexOf(entry.Id) < 0);
        return TodoList.Update(model.Lists[index].List, got.Message).Fold(list => model with
        {
            Lists = model.Lists.SetItem(index, new(id, list)),
            Pending = withdrawn ? null : model.Pending
        }, message => (TodoMessage)new TodoMessage.GotList(id, message));
    }

    internal static bool Unsaved(System.Collections.Immutable.ImmutableArray<NamedList> lists, TodoEntry entry) =>
        entry.Route is TodoPage.Detail detail && lists.FirstOrDefault(list => list.Id == detail.Id) is { } named &&
        (!string.IsNullOrWhiteSpace(named.List.Draft) || named.List.Items.Any(static item => item.Draft is not null));

    static TodoListModel Discard(TodoListModel list) => list with
    {
        Draft = "",
        Items = [.. list.Items.Select(static item => item with { Draft = null })]
    };

    static int Find(TodoModel model, string id)
    {
        for (var index = 0; index < model.Lists.Length; index++)
            if (model.Lists[index].Id == id)
                return index;
        return -1;
    }
}
