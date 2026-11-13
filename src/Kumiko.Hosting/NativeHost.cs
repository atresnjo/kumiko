using Kumiko.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kumiko.Hosting;

/// <summary>DI-owned application and a main-thread bridge for hosted services.</summary>
public sealed class NativeApplication : IDisposable, IAsyncDisposable
{
    public App App
    {
        get;
    }
    public Window? Window
    {
        get; private set;
    }

    readonly HashSet<Runtime.IProgram> programs = [];
    bool disposed;
    public NativeApplication(AppOptions options) => App = new(options);
    public Window CreateWindow(WindowOptions options, Runtime.IProgram program)
    {
        programs.Add(program);
        return Window = App.NewWindow(options, program);
    }
    public IReadOnlyList<Window> Windows => App.Windows;
    public void CreateWindows(Runtime.IProgram program, WindowOptions? defaults = null)
    {
        programs.Add(program);
        App.NewWindows(program, defaults);
    }
    public void Dispatch(Action action) => App.Dispatch(action);
    public void Invalidate() => Dispatch(() =>
    {
        foreach (var window in App.Windows)
            window.Invalidate();
    });
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var program in programs)
            program.Dispose();
        App.Dispose();
    }
    /// <summary>Cancel programs and dispose native resources on the calling UI thread, then join effect cleanup.</summary>
    public ValueTask DisposeAsync()
    {
        var pending = DrainPrograms();
        Dispose();
        return new(pending);
    }

    internal Task DrainPrograms()
    {
        foreach (var program in programs)
            program.Dispose();
        return Task.WhenAll(programs.OfType<IAsyncDisposable>().Select(program => program.DisposeAsync().AsTask()));
    }
}

public static class NativeHost
{
    public static IServiceCollection AddKumiko(this IServiceCollection services, AppOptions options)
    {
        services.AddSingleton(_ => new NativeApplication(options));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => Runtime.KumikoServices.From(provider));
        return services;
    }

    /// <summary>Compatibility overload. DI resolves all registrations in execution scopes; no service list is needed.</summary>
    public static IServiceCollection AddKumiko(this IServiceCollection services, AppOptions options, params Type[] effectServices)
    {
        ArgumentNullException.ThrowIfNull(effectServices);
        services.AddKumiko(options);
        services.AddSingleton(provider => Runtime.KumikoServices.From(provider).Validate(effectServices));
        return services;
    }

    /// <summary>Call synchronously on the process entry thread. Awaiting the host
    /// before entering the UI loop can resume on a worker and violate AppKit.</summary>
    public static void RunKumiko(this IHost host, NativeApplication application)
    {
        using var shutdown = host.Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => application.Dispatch(application.App.Quit));
        try
        {
            host.StartAsync().GetAwaiter().GetResult();
            application.App.Run();
        }
        finally
        {
            shutdown.Dispose();
            try
            {
                application.DrainPrograms().GetAwaiter().GetResult();
            }
            finally
            {
                try
                {
                    host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                }
                finally
                {
                    application.Dispose();
                }
            }
        }
    }
}
