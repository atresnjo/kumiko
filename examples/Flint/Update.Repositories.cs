using System.Collections.Immutable;
using Flint.Git;
using Flint.Services;
using Kumiko.Runtime;

namespace Flint;

public sealed partial class FlintApp
{
    static (FlintModel, Cmd<Msg>) Open(FlintModel m, string path)
    {
        if (m.Opening || string.IsNullOrWhiteSpace(path))
            return Pure(m);
        var workspace = m.ActiveWorkspace;
        var generation = m.Generation + 1;
        // Opens have their own generation: repository loads bump the shared counter while an open is in flight.
        m = m with
        {
            Opening = true,
            Error = "",
            Generation = generation,
            OpenGeneration = generation,
            Placeholder = null,
            OpenedFrom = m.Workspace.Active
        };
        // The tab appears before Git answers; Loaded replaces it with the repository root and Failed removes it.
        var directory = Folder(m.Home, path);
        if (directory is not null && m.Workspace.Repositories.Contains(directory))
            m = EditWorkspace(m, workspace, w => w with { Active = directory });
        else if (directory is not null && !m.Workspaces.Any(w => w.Repositories.Contains(directory)))
            m = EditWorkspace(m, workspace, w => w with { Repositories = w.Repositories.Add(directory), Active = directory }) with
            {
                Placeholder = directory
            };
        return (m, Cmd<Msg>.Run<IGitService>("open-repository",
            async (services, token) => new Msg.Loaded(workspace, await services.Load(path, 4000, token).ConfigureAwait(false), Generation: generation),
            error => new Msg.Failed(error.Message, Generation: generation)));
    }
    /// <summary>The absolute folder an open names, expanded against the model's home; null for a relative path.</summary>
    static string? Folder(string home, string path)
    {
        var full = path.StartsWith("~/", StringComparison.Ordinal) && home.Length > 0 ? Path.Combine(home, path[2..]) : path;
        return Path.IsPathFullyQualified(full) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(full)) : null;
    }
    /// <summary>Starts a full load of one repository; each repository has its own generation so concurrent loads all land.</summary>
    static (FlintModel, Cmd<Msg>) Load(FlintModel m, string path, int limit, TimeSpan delay, string key = "refresh:")
    {
        var generation = m.Generation + 1;
        var workspace = m.ActiveWorkspace;
        return (m with
        {
            Generation = generation,
            Loads = m.Generations.SetItem(path, generation)
        }, Cmd<Msg>.Debounce<IGitService>(key + path, delay,
            async (services, token) => new Msg.Loaded(workspace, await services.Load(path, limit, token).ConfigureAwait(false), path, Generation: generation),
            error => new Msg.Failed(error.Message, path, Generation: generation)));
    }
    static (FlintModel, Cmd<Msg>) Loaded(FlintModel m, Msg.Loaded loaded)
    {
        var open = loaded.Original is null;
        if (loaded.Mutation && loaded.Original is { } mutated)
            m = m with
            {
                Running = m.Operations.Remove(mutated)
            };
        if (loaded.Mutation && loaded.Notice.Length > 0)
            m = Notify(m, loaded.Notice, ToastKind.Success);
        var current = open ? loaded.Generation == m.OpenGeneration : m.Generations.TryGetValue(loaded.Original!, out var latest) && latest == loaded.Generation;
        if (!current && !loaded.Mutation)
            return Pure(m);
        var snapshot = loaded.Snapshot;
        // An open of a subfolder showed the folder's tab; the repository root replaces it.
        if (open && m.Placeholder != snapshot.Path)
            m = m.WithoutPlaceholder();
        if (open)
            m = m with
            {
                Placeholder = null,
                OpenedFrom = null,
                Opening = false,
                RecentPaths = [snapshot.Path, .. m.Recent.Where(r => r != snapshot.Path).Take(29)],
                Known = m.Known.Contains(snapshot.Path) ? m.Known : m.Known.Add(snapshot.Path)
            };
        var home = m.Workspace.Repositories.Contains(snapshot.Path) ? m.Workspace : m.Workspaces.FirstOrDefault(w => w.Repositories.Contains(snapshot.Path));
        if (!open && home is null)
            return Pure(m);
        var old = m.Repositories.Lookup(snapshot.Path);
        var tab = old is null ? new RepoTab(snapshot) : old with
        {
            Snapshot = snapshot,
            LoadingMore = false
        };
        if (loaded.Mutation)
            tab = tab with
            {
                Request = tab.Request + 1
            };
        if (loaded.Action == GitAction.Commit)
            tab = tab with
            {
                Subject = "",
                Body = "",
                Amend = false,
                Sha = null,
                Detail = null,
                File = null,
                Patch = "",
                Rows = []
            };
        m = m with
        {
            Repositories = m.Repositories.SetItem(snapshot.Path, tab),
            Error = "",
            Palette = open ? PaletteMode.Closed : m.Palette
        };
        if (open)
        {
            if (home is not null && home.Id != m.ActiveWorkspace)
                m = Notify(m with
                {
                    ActiveWorkspace = home.Id
                }, $"{snapshot.Name} is in workspace \"{home.Name}\"", ToastKind.Info);
            m = home is not null ? EditWorkspace(m, home.Id, w => w with { Active = snapshot.Path })
                : EditWorkspace(m, m.ActiveWorkspace, w => w with { Repositories = w.Repositories.Add(snapshot.Path), Active = snapshot.Path });
        }
        if (loaded.RemovedPath is { } removed)
            m = Forget(m, removed);
        var (next, command) = Settle(m, snapshot.Path);
        if (loaded.Next is { } chained)
        {
            var (pushed, push) = Execute(next, snapshot.Path, chained);
            return (pushed, Cmd<Msg>.Batch(command, push));
        }
        return (next, command);
    }
    /// <summary>After new data: keep the selected commit and shown file loaded.</summary>
    static (FlintModel, Cmd<Msg>) Settle(FlintModel m, string path)
    {
        if (!m.Repositories.TryGetValue(path, out var tab) || m.Workspace.Active != path)
            return Pure(m);
        var snapshot = tab.Snapshot;
        if (tab.Page == Page.History && snapshot.Commits.Length > 0 && (tab.Detail is null || tab.Sha is null))
            return SelectCommit(m, tab.Sha ?? snapshot.Commits[0].Sha, path);
        if (tab.Page == Page.Changes)
            return ShowWorkingFile(m, path);
        return Pure(m);
    }
    /// <summary>Drops a repository from every workspace; its folder is about to disappear.</summary>
    static FlintModel Forget(FlintModel m, string removed) => m with
    {
        Workspaces = [.. m.Workspaces.Select(w => w with
        {
            Repositories = w.Repositories.Remove(removed),
            Active = w.Active == removed ? w.Repositories.Remove(removed).ElementAtOrDefault(Math.Max(0, Math.Min(w.Repositories.IndexOf(removed), w.Repositories.Length - 2))) : w.Active
        })],
        Repositories = m.Repositories.Remove(removed)
    };
    static (FlintModel, Cmd<Msg>) Refresh(FlintModel m, string? path, bool more, bool statusOnly)
    {
        path ??= m.Workspace.Active;
        if (path is null || m.Operations.ContainsKey(path) || !m.Repositories.TryGetValue(path, out var tab))
            return Pure(m);
        if (more)
            return LoadMore(m, path);
        if (!statusOnly)
            return Load(m with
            {
                Error = ""
            }, path, tab.Snapshot.Limit, TimeSpan.FromMilliseconds(40));
        var root = path;
        var generation = m.Generations.GetValueOrDefault(path);
        return (m, Cmd<Msg>.Debounce<IGitService>("status:" + root, TimeSpan.FromMilliseconds(20),
            async (services, token) => new Msg.StatusLoaded(root, generation, await services.Status(root, token).ConfigureAwait(false)),
            error => new Msg.Failed(error.Message, root, Generation: generation)));
    }
    /// <summary>Applies a status-only refresh unless a newer full load started; a moved HEAD needs the full snapshot.</summary>
    static (FlintModel, Cmd<Msg>) StatusLoaded(FlintModel m, Msg.StatusLoaded loaded)
    {
        if (!m.Repositories.TryGetValue(loaded.Path, out var tab) || m.Generations.GetValueOrDefault(loaded.Path) != loaded.Generation
            || m.Operations.ContainsKey(loaded.Path))
            return Pure(m);
        var head = loaded.Status.Head;
        if (head.Sha != tab.Snapshot.Head.Sha || head.Branch != tab.Snapshot.Head.Branch)
            return Refresh(m, loaded.Path, false, false);
        m = m with
        {
            Repositories = m.Repositories.SetItem(loaded.Path, tab with
            {
                Snapshot = tab.Snapshot with
                {
                    Head = head,
                    Staged = loaded.Status.Staged,
                    Unstaged = loaded.Status.Unstaged
                }
            })
        };
        return m.Workspace.Active == loaded.Path && tab.Page == Page.Changes ? ShowWorkingFile(m, loaded.Path) : Pure(m);
    }
    /// <summary>Loads three times as many commits when the history list nears its end.</summary>
    static (FlintModel, Cmd<Msg>) LoadMore(FlintModel m, string path)
    {
        if (!m.Repositories.TryGetValue(path, out var tab) || !tab.Snapshot.HasMore || tab.LoadingMore || m.Operations.ContainsKey(path))
            return Pure(m);
        m = m with
        {
            Repositories = m.Repositories.SetItem(path, tab with
            {
                LoadingMore = true
            })
        };
        return Load(m, path, tab.Snapshot.Limit * 3, TimeSpan.Zero, "more:");
    }
    /// <summary>Stores the leaving tab's history offset.</summary>
    static FlintModel Remember(FlintModel m, float? scroll) => scroll is { } offset ? Edit(m, r => r with { HistoryScroll = offset }) : m;
    /// <summary>Asks the arriving tab's freshly built history list to return to its stored offset.</summary>
    static FlintModel Restore(FlintModel m) => Edit(m, r => r with { RestoreScroll = r.Page == Page.History && r.HistoryScroll > 0 });
    static FlintModel SelectRepo(FlintModel m, string path) => m.Workspace.Repositories.Contains(path)
        ? ChangeWorkspace(m, w => w with { Active = path }) : m;
    /// <summary>Paints the cached snapshot first, then revalidates it in the background.</summary>
    static (FlintModel, Cmd<Msg>) Switched((FlintModel Model, Cmd<Msg> Command) activated, string path)
    {
        if (!activated.Model.Repositories.TryGetValue(path, out var tab) || activated.Model.Operations.ContainsKey(path))
            return activated;
        var (m, refresh) = Load(activated.Model, path, tab.Snapshot.Limit, TimeSpan.FromMilliseconds(32));
        return (m, Cmd<Msg>.Batch(activated.Command, refresh));
    }
    static FlintModel CloseRepo(FlintModel m, string path)
    {
        if (!m.Workspace.Repositories.Contains(path))
            return m;
        var index = m.Workspace.Repositories.IndexOf(path);
        var paths = m.Workspace.Repositories.Remove(path);
        var next = m.Workspace.Active != path ? m.Workspace.Active : paths.IsEmpty ? null : paths[Math.Min(index, paths.Length - 1)];
        m = ChangeWorkspace(m, w => w with { Repositories = paths, Active = next });
        return m.Workspaces.Any(w => w.Repositories.Contains(path)) ? m : m with
        {
            Repositories = m.Repositories.Remove(path)
        };
    }
    static (FlintModel, Cmd<Msg>) Activate(FlintModel model)
    {
        if (model.Workspace.Active is not { } path)
            return Pure(model);
        if (!model.Repositories.ContainsKey(path))
            return Load(model, path, 4000, TimeSpan.Zero);
        if (model.Current is { Page: Page.History, Detail: null } tab && tab.Snapshot.Commits.Length > 0)
            return SelectCommit(model, tab.Sha ?? tab.Snapshot.Commits[0].Sha);
        return Pure(model);
    }
    static (FlintModel, Cmd<Msg>) SwitchTab(FlintModel model, int index, bool relative)
    {
        var paths = model.Workspace.Repositories;
        if (paths.IsEmpty)
            return Pure(model);
        if (relative)
            index = (paths.IndexOf(model.Workspace.Active ?? "") + index + paths.Length) % paths.Length;
        else if (index == 8)
            index = paths.Length - 1;
        return index < 0 || index >= paths.Length || paths[index] == model.Workspace.Active ? Pure(model)
            : Switched(Activate(Restore(SelectRepo(model, paths[index]))), paths[index]);
    }
    /// <summary>Moves the active repository to the front of the recent-tab order, forgetting closed ones.</summary>
    static FlintModel Visited(FlintModel model)
    {
        var recent = model.RecentTabs ?? [];
        if (model.Workspace.Active is not { } active || (recent.Length > 0 && recent[0] == active))
            return model;
        var open = model.Workspaces.SelectMany(w => w.Repositories).ToImmutableHashSet(StringComparer.Ordinal);
        return model with
        {
            RecentTabs = [active, .. recent.Where(path => path != active && open.Contains(path))]
        };
    }
    /// <summary>The first press targets the previous repository; later presses step on, wrapping like Alt+Tab.</summary>
    static FlintModel CycleTabs(FlintModel model, int delta)
    {
        var order = model.RecentOrder;
        if (order.Length < 2)
            return model;
        var index = model.Switcher is { } held ? Math.Max(0, order.IndexOf(held.Target)) : 0;
        var target = order[((index + delta) % order.Length + order.Length) % order.Length];
        if (model.Switcher is { } current)
            return model with
            {
                Switcher = current with
                {
                    Target = target
                }
            };
        return model with
        {
            Switcher = new(model.Sequence + 1, target),
            Sequence = model.Sequence + 1
        };
    }
    static (FlintModel, Cmd<Msg>) CommitTabs(FlintModel model, float? scroll)
    {
        if (model.Switcher is not { } switcher)
            return Pure(model);
        var released = model with
        {
            Switcher = null
        };
        if (switcher.Target == model.Workspace.Active || !model.Workspace.Repositories.Contains(switcher.Target))
            return Pure(released);
        return Transition(released, new Msg.SelectRepo(switcher.Target, scroll));
    }
    static FlintModel Revealed(FlintModel model, string path, bool history)
    {
        if (!model.Repositories.TryGetValue(path, out var tab))
            return model;
        return model with
        {
            Repositories = model.Repositories.SetItem(path, history ? tab with
            {
                RevealHistory = false,
                RestoreScroll = false
            } : tab with
            {
                RevealDiff = false,
                RevealFiles = false
            })
        };
    }
}

static class ImmutableQueries
{
    public static TValue? Lookup<TKey, TValue>(this ImmutableDictionary<TKey, TValue> values, TKey key) where TKey : notnull
        => values.TryGetValue(key, out var value) ? value : default;
    public static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> match)
    {
        for (var i = 0; i < items.Length; i++)
            if (match(items[i]))
                return i;
        return -1;
    }
}
