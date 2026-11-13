using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Kumiko.Runtime;
using Todo.Components;

namespace Todo;

[RouteUnion("page")]
public abstract partial record TodoPage
{
    private TodoPage()
    {
    }
    [Route("lists", "lists", 1)] public sealed record Lists : TodoPage;
    [Route("lists/{Id}", "list", 2)] public sealed record Detail(string Id) : TodoPage;
}

/// <summary>One page visit. The path is the list overview, optionally followed by one list's detail.</summary>
public sealed record TodoEntry(int Id, TodoPage Route) : INavigationEntry<TodoPage, TodoEntry>
{
    public static TodoEntry Create(int id, TodoPage route) => new(id, route);
    public static bool IsValidPath(ImmutableArray<TodoEntry> entries) =>
        entries[0].Route is TodoPage.Lists && entries.Length <= 2 && entries.Skip(1).All(static entry => entry.Route is TodoPage.Detail);
}

/// <summary>A reusable TodoList and the stable ID its detail route uses.</summary>
public sealed record NamedList(string Id, TodoListModel List);

/// <summary>
/// The lists and the page path. Drafts belong to each list; leaving a detail page with a draft waits for
/// <see cref="Pending"/> to be confirmed.
/// </summary>
public sealed record TodoModel(ImmutableArray<NamedList> Lists)
{
    /// <summary>The single root screen's history. Pages within it use <see cref="Path"/>.</summary>
    public NavigationHistory<string> Navigation { get; init; } = NavigationHistory<string>.Start("todo");
    public NavigationPath<TodoPage, TodoEntry> Path { get; init; } = NavigationPath<TodoPage, TodoEntry>.Start(new TodoPage.Lists());
    public PendingNavigation<TodoPage, TodoEntry>? Pending
    {
        get; init;
    }

    [JsonIgnore] public TodoPage Page => Path.Top.Route;
}

public abstract partial record TodoMessage
{
    public sealed record Open(string Id) : TodoMessage;
    public sealed record GotList(string Id, TodoListMessage Message) : TodoMessage;
    public sealed record Back(BackRequest Request) : TodoMessage;
    public sealed record ConfirmBack(BackRequest Request) : TodoMessage;
    public sealed record CancelBack : TodoMessage;
    public sealed record Navigate(HistoryRequest Request) : TodoMessage;
}
