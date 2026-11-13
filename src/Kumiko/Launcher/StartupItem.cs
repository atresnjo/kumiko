using System.Text.RegularExpressions;

namespace Kumiko.Desktop;

public enum StartupState
{
    Disabled,
    Enabled,
    RequiresApproval,
    Unsupported,
    OwnedByAnother
}

/// <summary>Observed registration state. Desktop policy may still prevent an enabled item from running.</summary>
public sealed record StartupStatus(StartupState State, string? Detail = null);

/// <summary>Owns only this packaged application's per-user startup registration. Construction never registers.</summary>
public sealed partial class StartupItem : IDisposable
{
    readonly App app;
    readonly IStartupItemBackend backend;
    bool disposed;
    public StartupItem(App app, string identity, string name) : this(app, identity, name, null)
    {
    }

    internal StartupItem(App app, string identity, string name, IStartupItemBackend? backend)
    {
        app.CheckThread();
        if (!Regex.IsMatch(identity, "^[A-Za-z0-9][A-Za-z0-9.-]{2,127}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Use a stable reverse-DNS package identity.", nameof(identity));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains('\n') || name.Contains('\r'))
            throw new ArgumentException("A startup name must be one line.", nameof(name));
        if (backend is null && app.Options.Hidden)
            throw new InvalidOperationException("Hidden apps require a fixture startup backend.");
        this.app = app;
        this.backend = backend ?? new NativeStartupItem(app, identity, name);
        app.Disposing += Dispose;
    }

    public StartupStatus Read()
    {
        Check();
        return backend.Read();
    }

    /// <summary>Call only for an explicit user preference change; never during construction or restoration.</summary>
    public StartupStatus SetEnabled(bool enabled)
    {
        Check();
        return backend.SetEnabled(enabled);
    }

    /// <summary>Established OS launch provenance; false while macOS provenance is pending. Await GetLaunchAtLoginAsync before deciding initial visibility.</summary>
    public bool WasLaunchedAtLogin
    {
        get
        {
            Check();
            return backend.WasLaunchedAtLogin;
        }
    }

    /// <summary>Waits for launch provenance. Packaged macOS launches resolve when their open-application event arrives.</summary>
    public Task<bool> GetLaunchAtLoginAsync(CancellationToken cancellationToken = default)
    {
        Check();
        return backend.GetLaunchAtLoginAsync(cancellationToken);
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        app.CheckThread();
        disposed = true;
        app.Disposing -= Dispose;
    }
}

internal interface IStartupItemBackend
{
    Task<bool> GetLaunchAtLoginAsync(CancellationToken cancellationToken) => Task.FromResult(WasLaunchedAtLogin).WaitAsync(cancellationToken);
    StartupStatus Read();
    StartupStatus SetEnabled(bool enabled);
    bool WasLaunchedAtLogin
    {
        get;
    }
}
