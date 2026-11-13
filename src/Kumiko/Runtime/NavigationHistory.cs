using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Kumiko.Runtime;

/// <summary>A visit identity is independent of its destination; repeated routes are distinct visits.</summary>
public sealed record NavigationVisit<TRoute>(int Id, TRoute Route) : INavigationEntry<TRoute, NavigationVisit<TRoute>> where TRoute : notnull
{
    public static NavigationVisit<TRoute> Create(int id, TRoute route) => new(id, route);
    public static bool IsValidPath(ImmutableArray<NavigationVisit<TRoute>> entries) => true;
}

/// <summary>A Back/Forward request bound to one host and one version of its history.</summary>
public sealed record HistoryRequest(string Stack, long Revision, int From, int To);

/// <summary>Immutable browser-style history over identified visits. Domain state is not time-travelled.</summary>
public sealed record NavigationHistory<TRoute> where TRoute : notnull
{
    public NavigationPath<TRoute, NavigationVisit<TRoute>> Path
    {
        get;
    }
    public int Position
    {
        get;
    }
    [JsonIgnore] public NavigationVisit<TRoute> Current => Path.Entries[Position];
    [JsonIgnore] public bool CanGoBack => Position > 0;
    [JsonIgnore] public bool CanGoForward => Position < Path.Entries.Length - 1;

    [JsonConstructor]
    public NavigationHistory(NavigationPath<TRoute, NavigationVisit<TRoute>> path, int position)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (position < 0 || position >= path.Entries.Length)
            throw new ArgumentOutOfRangeException(nameof(position));
        Path = path;
        Position = position;
    }

    public static NavigationHistory<TRoute> Start(TRoute route) => new(NavigationPath<TRoute, NavigationVisit<TRoute>>.Start(route), 0);

    /// <summary>Appends a new visit after the current one, discarding any forward branch.</summary>
    public NavigationHistory<TRoute> Push(TRoute route) => new(Path.Pop(Current.Id).Push(route), Position + 1);

    /// <summary>Replaces the current visit with a fresh identity, discarding any forward branch.</summary>
    public NavigationHistory<TRoute> Replace(TRoute route) => new(Path.Pop(Current.Id).Replace(route), Position);

    /// <summary>Discards both directions and creates a fresh root, even for the same route.</summary>
    public NavigationHistory<TRoute> Reset(TRoute route) => new(Path.Pop(Path.Entries[0].Id).Replace(route), 0);

    public HistoryRequest? BackRequest(string stack) => Request(stack, Position - 1);
    public HistoryRequest? ForwardRequest(string stack) => Request(stack, Position + 1);

    HistoryRequest? Request(string stack, int position)
    {
        ArgumentException.ThrowIfNullOrEmpty(stack);
        return position < 0 || position >= Path.Entries.Length ? null : new(stack, Path.Revision, Current.Id, Path.Entries[position].Id);
    }

    /// <summary>Applies a still-current adjacent request. Application admission policy runs before this call.</summary>
    public NavigationHistory<TRoute> Move(HistoryRequest request, string stack)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(stack);
        var position = Path.IndexOf(request.To);
        if (request.Stack != stack || request.Revision != Path.Revision || request.From != Current.Id ||
            position < 0 || Math.Abs(position - Position) != 1)
            return this;
        return new(new(Path.Entries, Path.NextId, checked(Path.Revision + 1)), position);
    }
}
