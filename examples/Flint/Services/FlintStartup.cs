using System.Collections.Immutable;

namespace Flint.Services;

/// <summary>Startup-only constructor injection. Views and Update have no provider access.</summary>
public sealed class FlintStartup(IWorkspaceStorage storage, IRepositoryCache cache, IRepositoryDiscovery discovery, IGitService git)
{
    public FlintModel Restore(string home, bool offscreen = false, string? repositoryPath = null)
    {
        var restored = offscreen ? FlintApp.Init.Persistence : storage.Load() ?? FlintApp.Init.Persistence;
        var tabs = ImmutableDictionary<string, RepoTab>.Empty;
        if (!offscreen)
            foreach (var repository in restored.Workspaces.SelectMany(workspace => workspace.Repositories).Distinct())
                if (cache.Read(repository) is { } snapshot)
                    tabs = tabs.SetItem(repository, new(snapshot));
        var model = FlintApp.Init with
        {
            Workspaces = restored.Workspaces,
            ActiveWorkspace = restored.ActiveWorkspace,
            Sidebar = restored.Sidebar,
            RecentPaths = restored.Recent,
            Known = offscreen ? [] : discovery.Known(),
            Home = home,
            Watching = !offscreen,
            Repositories = tabs
        };
        return offscreen && repositoryPath is not null ? Bootstrap.Repository(model, git, repositoryPath) : model;
    }
}
