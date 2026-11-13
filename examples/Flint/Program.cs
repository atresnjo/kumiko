using Flint;
using Flint.Services;
using Kumiko;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var offscreen = args.Any(a => a.StartsWith("--kumiko-snapshot", StringComparison.Ordinal) || a.StartsWith("--kumiko-tree", StringComparison.Ordinal) || a.StartsWith("--kumiko-agent", StringComparison.Ordinal) || a.StartsWith("--kumiko-audit", StringComparison.Ordinal) || a.StartsWith("--kumiko-replay", StringComparison.Ordinal) || a.StartsWith("--kumiko-step", StringComparison.Ordinal));
var explicitState = Dev.Option(args, "--state-dir") ?? Environment.GetEnvironmentVariable("FLINT_HOME");
var temporaryState = offscreen && explicitState is null;
var stateDirectory = explicitState ?? (temporaryState ? Path.Combine(Path.GetTempPath(), "kumiko-flint-offscreen-" + Guid.NewGuid().ToString("N")) : Path.Combine(home, ".kumiko-flint"));
// Like the original: FLINT_ROOTS adds folders (hidden or on other volumes) to the home-folder scan.
var roots = (Environment.GetEnvironmentVariable("FLINT_ROOTS") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }));
builder.Services.AddFlint(options =>
{
    options.StateDirectory = stateDirectory;
    options.DiscoveryRoots = [.. roots, home];
});
using var host = builder.Build();
var repositoryPath = Dev.Option(args, "--repo");
var startupScope = host.Services.CreateAsyncScope();
FlintModel model;
try
{
    model = startupScope.ServiceProvider.GetRequiredService<FlintStartup>().Restore(home, offscreen, repositoryPath);
}
finally
{
    startupScope.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
using var program = FlintApp.CreateProgram(model, host.Services);
// Past diffs and repository snapshots are only needed for explicit diagnostics.
program.RetainHistory = offscreen || args.Contains("--kumiko-dev", StringComparer.Ordinal)
    || args.Contains("--kumiko-debug", StringComparer.Ordinal) || Dev.Option(args, "--kumiko-record") is not null;
if (!offscreen && repositoryPath is { } path)
    program.Send(new Msg.Open(path));
else if (!offscreen)
    program.Send(new Msg.Start());
try
{
    host.StartAsync().GetAwaiter().GetResult();
    Dev.Run("Flint", 1440, 900, program, args, new("Flint", 1440, 900, 900, 560, HiddenTitleBar: true, Theme: Palette.Theme));
}
finally
{
    try
    {
        program.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
    finally
    {
        host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        if (temporaryState && Directory.Exists(stateDirectory))
            Directory.Delete(stateDirectory, true);
    }
}
