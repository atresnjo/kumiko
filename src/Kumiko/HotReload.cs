using Kumiko.Runtime;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Kumiko.HotReload))]

namespace Kumiko;

/// <summary>
/// .NET hot reload integration. Views are rebuilt every frame, so applying edited method bodies only needs
/// one redraw. Run an app with <c>dotnet watch</c>; <see cref="Dev"/> also keeps Kumiko models across restarts.
/// </summary>
public static class HotReload
{
    static readonly List<WeakReference<App>> apps = [];
    static readonly List<WeakReference<HostRegistration>> hosts = [];
    static readonly TimeSpan saveDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>True when the process was started by <c>dotnet watch</c>.</summary>
    public static bool Watching => Environment.GetEnvironmentVariable("DOTNET_WATCH") == "1";

    internal static void Register(App app)
    {
        lock (apps)
        {
            apps.RemoveAll(a => !a.TryGetTarget(out _));
            apps.Add(new(app));
        }
    }

    internal static void Unregister(App app)
    {
        lock (apps)
            apps.RemoveAll(a => !a.TryGetTarget(out var target) || target == app);
    }

    // The registration is owned by the host, never by the static registry. Wakes must schedule UI work.
    internal static IDisposable RegisterHost(Action wake)
    {
        var registration = new HostRegistration(wake);
        lock (hosts)
        {
            hosts.RemoveAll(host => !host.TryGetTarget(out _));
            hosts.Add(new(registration));
        }
        return registration;
    }

    sealed class HostRegistration(Action wake) : IDisposable
    {
        Action? callback = wake;

        public void Wake() => Volatile.Read(ref callback)?.Invoke();

        public void Dispose()
        {
            Interlocked.Exchange(ref callback, null);
            lock (hosts)
                hosts.RemoveAll(host => !host.TryGetTarget(out var target) || target == this);
        }
    }

    /// <summary>Called by the hot reload agent on a worker thread after edits are applied. Redraws live desktop windows and native hosts.</summary>
    public static void UpdateApplication(Type[]? types)
    {
        App[] targets;
        lock (apps)
            targets = [.. apps.Select(a => a.TryGetTarget(out var app) ? app : null).OfType<App>()];
        foreach (var app in targets)
        {
            try
            {
                app.Dispatch(() =>
                {
                    foreach (var window in app.Windows.ToArray())
                        window.Invalidate();
                });
            }
            catch (ObjectDisposedException)
            {
                // The app closed while the update was being applied.
            }
        }
        HostRegistration[] nativeTargets;
        lock (hosts)
        {
            hosts.RemoveAll(host => !host.TryGetTarget(out _));
            nativeTargets = [.. hosts.Select(host => host.TryGetTarget(out var target) ? target : null).OfType<HostRegistration>()];
        }
        foreach (var target in nativeTargets)
            target.Wake();
    }

    /// <summary>
    /// Restores the program's model from <paramref name="path"/> and saves each later model change there,
    /// including the last one before the window closes. Call before <see cref="IProgram.Attach"/>.
    /// Returns whether a saved model was restored.
    /// </summary>
    public static bool Persist(IProgram program, Window window, string path)
    {
        return Persist(program, window.App, path);
    }

    /// <summary>Persists a whole program, independent of any individual window's lifetime.</summary>
    public static bool Persist(IProgram program, App app, string path)
    {
        app.CheckThread();
        var restored = program.RestoreModel(path);
        IDisposable? pending = null;
        void Save()
        {
            pending?.Dispose();
            pending = null;
            program.SaveModel(path);
        }
        program.ModelChanged += () => pending ??= app.Schedule(saveDelay, Save);
        program.Disposing += () =>
        {
            if (pending is not null)
                Save();
        };
        return restored;
    }

    internal static string StatePath(string title)
    {
        var name = string.Concat(title.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-'));
        return Path.Combine(AppContext.BaseDirectory, "kumiko-hot", name + ".json");
    }
}
