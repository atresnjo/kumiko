using Kumiko;
using Kumiko.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

static class DiHostFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var trace = new DiTrace();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], DisableDefaults = true });
        builder.Services.AddSingleton(trace);
        builder.Services.AddScoped<DiResource>();
        builder.Services.AddScoped<IStore, DiStore>();
        builder.Services.AddKumiko(new AppOptions("DI hidden host", Hidden: true));
        using var host = builder.Build();
        var application = host.Services.GetRequiredService<NativeApplication>();
        using var program = StorageApp.CreateProgram(host.Services);
        application.CreateWindow(new("DI hidden host", 360, 200, Visible: false), program);
        program.Send(new StorageMsg.Edit("cancelled"));
        var quit = Task.Run(async () =>
        {
            await trace.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            application.Dispatch(application.App.Quit);
            await trace.Cleaning.Task.WaitAsync(TimeSpan.FromSeconds(10));
            trace.StopWatch.SetResult();
            trace.FinishCommand.SetResult();
        });
        // A bounded native timer ends the loop even if effect startup fails.
        using var timeout = application.App.Schedule(TimeSpan.FromSeconds(15), application.App.Quit);
        host.RunKumiko(application);
        quit.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        check(trace.Events.Count(e => e.StartsWith("disposed:", StringComparison.Ordinal)) == 2
            && trace.Events.Contains("command-cleaned") && trace.Events.Contains("watch-stopped"),
            "Generic Host quits hidden native windows and drains scoped command/subscription cleanup on the entry thread");
        File.WriteAllLines(Path.Combine(output, "services-di-host.txt"), trace.Events);
    }
}
