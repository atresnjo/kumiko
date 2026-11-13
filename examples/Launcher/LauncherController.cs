using System.Collections.Immutable;
using Kumiko.Desktop;
using Kumiko.Rendering;
using Kumiko.Runtime;

namespace Launcher;

/// <summary>The example's desktop boundary. Evidence uses the same controller with private fixtures.</summary>
public interface ILauncherDesktop
{
    Task<bool> GetLoginLaunchAsync(CancellationToken token) => Task.FromResult(LoginLaunch).WaitAsync(token);
    Task<IReadOnlyList<InstalledApplication>> Catalog(CancellationToken token);
    Image? Icon(InstalledApplication app);
    Task Launch(InstalledApplication app);
    StartupStatus ReadStartup();
    StartupStatus SetStartup(bool enabled);
    bool LoginLaunch
    {
        get;
    }
    bool Register(string accelerator, Action callback);
    void Unregister(string accelerator);
    void Show();
    void Hide();
    void Quit();
    event Action? Dismissed;
}

/// <summary>Marshals all service results to the owning UI thread and retires late work on disposal.</summary>
public sealed class LauncherController : IDisposable
{
    readonly Program<LauncherModel, LauncherMsg> program;
    readonly ILauncherDesktop desktop;
    readonly Action<Action> dispatch;
    readonly CancellationTokenSource stop = new();
    int handled, catalogSequence;
    bool disposed, started;
    string? registered;
    public LauncherController(Program<LauncherModel, LauncherMsg> program, ILauncherDesktop desktop, Action<Action> dispatch)
    {
        this.program = program;
        this.desktop = desktop;
        this.dispatch = dispatch;
        program.ModelChanged += Changed;
        desktop.Dismissed += Dismissed;
    }

    public void Start()
    {
        if (disposed || started)
            return;
        started = true;
        try
        {
            program.Send(new LauncherMsg.StartupRead(desktop.ReadStartup()));
        }
        catch (Exception error)
        {
            program.Send(new LauncherMsg.Failure("Could not read start at login: " + error.Message));
        }
        Shortcut(program.Model.Shortcut);
        Reload();
        _ = InitialLaunch();
    }

    async Task InitialLaunch()
    {
        try
        {
            if (!await desktop.GetLoginLaunchAsync(stop.Token).ConfigureAwait(false))
                Queue(Summon);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Queue(() => program.Send(new LauncherMsg.Failure("Could not determine launch origin: " + error.Message)));
        }
    }

    public void Summon()
    {
        if (!disposed)
            program.Send(new LauncherMsg.Summon());
    }

    public void Toggle()
    {
        if (!disposed)
            program.Send(program.Model.Visible ? new LauncherMsg.Dismiss() : new LauncherMsg.Summon());
    }

    void Dismissed()
    {
        if (!disposed)
            program.Send(new LauncherMsg.DidHide());
    }

    void Changed()
    {
        var request = program.Model.Request;
        if (disposed || request.Sequence == handled)
            return;
        handled = request.Sequence;
        Queue(() => Handle(request));
    }

    void Handle(HostRequest request)
    {
        try
        {
            switch (request.Action)
            {
                case HostAction.Show:
                    desktop.Show();
                    break;
                case HostAction.Hide:
                    desktop.Hide();
                    break;
                case HostAction.Refresh:
                    Reload();
                    break;
                case HostAction.Launch:
                    var target = program.Model.Entries.FirstOrDefault(e => e.Id == request.Value);
                    if (target is not null)
                    {
                        desktop.Hide();
                        _ = Launch(target);
                    }
                    break;
                case HostAction.Startup:
                    program.Send(new LauncherMsg.StartupRead(desktop.SetStartup(bool.Parse(request.Value))));
                    break;
                case HostAction.Shortcut:
                    Shortcut(request.Value);
                    break;
                case HostAction.Quit:
                    desktop.Quit();
                    break;
            }
        }
        catch (Exception error)
        {
            program.Send(new LauncherMsg.Failure(error.Message));
        }
    }

    void Shortcut(string accelerator)
    {
        try
        {
            if (registered == accelerator)
                return;
            if (!desktop.Register(accelerator, () => Queue(Toggle)))
            {
                program.Send(new LauncherMsg.Failure(registered is null
                    ? "Shortcut unavailable. Choose another in Settings."
                    : "Shortcut unavailable. Choose another in Settings; the previous shortcut still works."));
                return;
            }
            var previous = registered;
            registered = accelerator;
            if (previous is not null)
                desktop.Unregister(previous);
            program.Send(new LauncherMsg.ShortcutSaved(accelerator));
        }
        catch (Exception error)
        {
            program.Send(new LauncherMsg.Failure("Could not register shortcut: " + error.Message));
        }
    }

    void Reload()
    {
        var sequence = ++catalogSequence;
        try
        {
            _ = CompleteCatalog(desktop.Catalog(stop.Token), sequence);
        }
        catch (Exception error)
        {
            program.Send(new LauncherMsg.Catalog([], "Could not load applications: " + error.Message));
        }
    }

    async Task CompleteCatalog(Task<IReadOnlyList<InstalledApplication>> task, int sequence)
    {
        try
        {
            var entries = await task.ConfigureAwait(false);
            Queue(() =>
            {
                if (sequence != catalogSequence)
                    return;
                var normalized = ApplicationCatalog.Normalize(entries);
                var offset = 0;
                PrepareIcons();
                void PrepareIcons()
                {
                    if (sequence != catalogSequence)
                        return;
                    var end = Math.Min(offset + 8, Math.Min(128, normalized.Count));
                    while (offset < end)
                    {
                        try
                        {
                            _ = desktop.Icon(normalized[offset]);
                        }
                        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
                        {
                            // Missing icons never prevent launching an otherwise valid application.
                        }
                        offset++;
                    }
                    if (offset < Math.Min(128, normalized.Count))
                        Queue(PrepareIcons);
                    else
                        program.Send(new LauncherMsg.Catalog(normalized.ToImmutableArray()));
                }
            });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Queue(() =>
            {
                if (sequence == catalogSequence)
                    program.Send(new LauncherMsg.Catalog([], "Could not load applications: " + error.Message));
            });
        }
    }

    async Task Launch(InstalledApplication target)
    {
        try
        {
            await desktop.Launch(target).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            Queue(() =>
            {
                program.Send(new LauncherMsg.Summon());
                program.Send(new LauncherMsg.Failure("Could not open " + target.Name + ": " + error.Message));
            });
        }
    }

    void Queue(Action action)
    {
        if (Volatile.Read(ref disposed))
            return;
        try
        {
            dispatch(() =>
            {
                if (!disposed)
                    action();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Volatile.Write(ref disposed, true);
        stop.Cancel();
        stop.Dispose();
        program.ModelChanged -= Changed;
        desktop.Dismissed -= Dismissed;
        if (registered is { } accelerator)
            desktop.Unregister(accelerator);
    }
}
