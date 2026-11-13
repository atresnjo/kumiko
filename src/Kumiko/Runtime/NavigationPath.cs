using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Kumiko.Runtime;

/// <summary>Application-owned entry state and pure path-shape policy for a model-owned stack.</summary>
public interface INavigationEntry<TRoute, TEntry> where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
{
    int Id
    {
        get;
    }
    TRoute Route
    {
        get;
    }
    static abstract TEntry Create(int id, TRoute route);
    static abstract bool IsValidPath(ImmutableArray<TEntry> entries);
}

/// <summary>An immutable, JSON-serializable path. Entry identities identify visits, not routes.</summary>
public sealed record NavigationPath<TRoute, TEntry> where TRoute : notnull where TEntry : class, INavigationEntry<TRoute, TEntry>
{
    public ImmutableArray<TEntry> Entries
    {
        get;
    }
    public int NextId
    {
        get;
    }
    public long Revision
    {
        get;
    }
    [JsonIgnore] public TEntry Top => Entries[^1];

    [JsonConstructor]
    public NavigationPath(ImmutableArray<TEntry> entries, int nextId, long revision)
    {
        if (entries.IsDefaultOrEmpty || nextId < 1 || revision < 0 || entries.Any(e => e is null || e.Route is null ||
            e.Id < 0 || e.Id >= nextId) || entries.Select(e => e.Id).Distinct().Count() != entries.Length || !TEntry.IsValidPath(entries))
            throw new InvalidOperationException("Invalid navigation path.");
        Entries = entries;
        NextId = nextId;
        Revision = revision;
    }

    public static NavigationPath<TRoute, TEntry> Start(TRoute root) => new([Fresh(0, root)], 1, 0);
    public NavigationPath<TRoute, TEntry> Push(TRoute route) => new(Entries.Add(Fresh(NextId, route)), checked(NextId + 1), checked(Revision + 1));
    public NavigationPath<TRoute, TEntry> Replace(TRoute route) => new(Entries.SetItem(Entries.Length - 1, Fresh(NextId, route)),
        checked(NextId + 1), checked(Revision + 1));

    public NavigationPath<TRoute, TEntry> Reset(TRoute root) => SameRoute(Entries[0].Route, root)
        ? Entries.Length == 1 ? this : new([Entries[0]], NextId, checked(Revision + 1))
        : new([Fresh(NextId, root)], checked(NextId + 1), checked(Revision + 1));

    /// <summary>Edits state without changing an entry's identity, route or path revision.</summary>
    public NavigationPath<TRoute, TEntry> Edit(int id, Func<TEntry, TEntry> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var index = IndexOf(id);
        if (index < 0)
            return this;
        var original = Entries[index];
        var next = edit(original);
        if (next is null || next.Id != original.Id || !SameRoute(next.Route, original.Route))
            throw new InvalidOperationException("Editing navigation state cannot change its identity or route.");
        return EqualityComparer<TEntry>.Default.Equals(next, original) ? this : new(Entries.SetItem(index, next), NextId, Revision);
    }

    public int IndexOf(int id)
    {
        for (var i = 0; i < Entries.Length; i++)
            if (Entries[i].Id == id)
                return i;
        return -1;
    }

    public NavigationPath<TRoute, TEntry> Pop(int target)
    {
        var index = IndexOf(target);
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(target));
        return index == Entries.Length - 1 ? this : new(Entries.Take(index + 1).ToImmutableArray(), NextId, checked(Revision + 1));
    }

    /// <summary>Removes matching visits, reserving an allocation boundary and invalidating outstanding requests.</summary>
    public NavigationPath<TRoute, TEntry> RemoveWhere(Func<TEntry, bool> remove)
    {
        ArgumentNullException.ThrowIfNull(remove);
        return new(Entries.Where(e => !remove(e)).ToImmutableArray(), checked(NextId + 1), checked(Revision + 1));
    }

    public BackRequest RequestTo(int target, string stack) => new(stack, Revision, Top.Id, target);

    /// <summary>Rejects stale, cross-stack or non-ancestor Back requests without changing the path.</summary>
    public NavigationPath<TRoute, TEntry>? Back(BackRequest request, string stack)
    {
        if (request is null)
            return null;
        var index = IndexOf(request.To);
        return Matches(request, stack) && index >= 0 && index < Entries.Length - 1 ? Pop(request.To) : null;
    }

    /// <summary>Reconciles a proposed path, retaining each kept visit's current state and order.</summary>
    public NavigationPath<TRoute, TEntry>? Follow(NavigationPath<TRoute, TEntry> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Revision <= Revision || desired.NextId < NextId)
            return null;
        var entries = ImmutableArray.CreateBuilder<TEntry>(desired.Entries.Length);
        var previous = -1;
        foreach (var entry in desired.Entries)
        {
            var index = IndexOf(entry.Id);
            if (index < 0 ? entry.Id < NextId : index <= previous || !SameRoute(Entries[index].Route, entry.Route))
                return null;
            entries.Add(index < 0 ? entry : Entries[index]);
            previous = Math.Max(previous, index);
        }
        return new(entries.MoveToImmutable(), desired.NextId, desired.Revision);
    }

    /// <summary>Checks only drafts not already approved for removal by a confirmed path.</summary>
    public bool RequiresConfirmation(NavigationPath<TRoute, TEntry> desired, Func<TEntry, bool> unsaved,
        NavigationPath<TRoute, TEntry>? confirmed = null)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(unsaved);
        return Entries.Any(e => desired.IndexOf(e.Id) < 0 && (confirmed is null || confirmed.IndexOf(e.Id) >= 0) && unsaved(e));
    }

    internal bool Matches(BackRequest? request, string stack) => request is not null && request.Stack == stack &&
        request.Revision == Revision && request.From == Top.Id;

    static bool SameRoute(TRoute left, TRoute right) => EqualityComparer<TRoute>.Default.Equals(left, right);

    static TEntry Fresh(int id, TRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var entry = TEntry.Create(id, route);
        if (entry is null || entry.Id != id || !SameRoute(entry.Route, route))
            throw new InvalidOperationException("Navigation entry creation must preserve its allocated identity and route.");
        return entry;
    }
}
