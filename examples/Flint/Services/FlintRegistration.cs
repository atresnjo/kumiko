using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Flint.Services;

public sealed class FlintOptions
{
    public string StateDirectory { get; set; } = "";
    public string[] DiscoveryRoots { get; set; } = [];
}

public static class FlintRegistration
{
    /// <summary>One registration path for the app and isolated hosts. Register replacements before or after this call.</summary>
    public static IServiceCollection AddFlint(this IServiceCollection services, Action<FlintOptions> configure)
    {
        services.AddOptions<FlintOptions>().Configure(configure)
            .Validate(options => !string.IsNullOrWhiteSpace(options.StateDirectory), "Flint requires a state directory.");
        services.TryAddSingleton<IProcessRunner>(_ => new ProcessRunner(searchPath: SearchPath.User));
        services.TryAddSingleton<IRepositoryCache>(provider => new RepositoryCache(provider.GetRequiredService<IOptions<FlintOptions>>().Value.StateDirectory));
        // These instances own shared caches and mutation/persistence gates across all execution scopes.
        services.TryAddSingleton<IGitService>(provider => new GitService(provider.GetRequiredService<IProcessRunner>(),
            provider.GetRequiredService<IOptions<FlintOptions>>().Value.StateDirectory, provider.GetRequiredService<IRepositoryCache>()));
        services.TryAddSingleton<IWorkspaceStorage>(provider => new WorkspaceStorage(provider.GetRequiredService<IOptions<FlintOptions>>().Value.StateDirectory));
        services.TryAddSingleton<IRepositoryDiscovery>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FlintOptions>>().Value;
            return new RepositoryDiscovery(options.DiscoveryRoots, options.StateDirectory);
        });
        services.TryAddSingleton<IRepositoryWatcher, RepositoryWatcher>();
        services.TryAddSingleton<IGitHubAccounts, GitHubAccounts>();
        services.TryAddSingleton<ILauncher, SystemLauncher>();
        services.TryAddScoped<FlintStartup>();
        return services;
    }
}
