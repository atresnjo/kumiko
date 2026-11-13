using Kumiko.Desktop;
using Kumiko.Rendering;

namespace Launcher;

/// <summary>Deterministic desktop for snapshot and behavior runs. Never touches desktop registrations.</summary>
public sealed class FixtureLauncherDesktop : ILauncherDesktop
{
    public Func<CancellationToken, Task<bool>>? LaunchProvenance
    {
        get; set;
    }
    public Task<bool> GetLoginLaunchAsync(CancellationToken token) => LaunchProvenance?.Invoke(token) ?? Task.FromResult(LoginLaunch).WaitAsync(token);
    public static InstalledApplication[] Applications =>
    [
        new("dev.fixture.calendar", "Calendar", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Calendar.app")),
        new("dev.fixture.editor", "Code Editor", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Code Editor.app")),
        new("dev.fixture.files", "Files", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Files.app")),
        new("dev.fixture.notes", "Notes", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Notes.app")),
        new("dev.fixture.terminal", "Terminal", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Terminal.app")),
        new("dev.fixture.web", "Web Browser", Path.Combine(Path.GetTempPath(), "kumiko-launcher-fixtures", "Web Browser.app"))
    ];
    public Func<CancellationToken, Task<IReadOnlyList<InstalledApplication>>> Load { get; set; } = _ => Task.FromResult<IReadOnlyList<InstalledApplication>>(Applications);
    public bool LaunchFails
    {
        get; set;
    }
    public bool StartupFails
    {
        get; set;
    }
    public bool ShortcutsUnsupported
    {
        get; set;
    }
    public bool ShortcutConflict
    {
        get; set;
    }
    public bool LoginLaunch
    {
        get; set;
    }
    public bool Visible
    {
        get; private set;
    }
    public bool QuitRequested
    {
        get; private set;
    }
    public int Shows
    {
        get; private set;
    }
    public int StartupWrites
    {
        get; private set;
    }
    public StartupStatus Startup { get; private set; } = new(StartupState.Disabled);
    public List<string> Events { get; } = [];
    public Dictionary<string, Action> Shortcuts { get; } = new(StringComparer.Ordinal);
    public event Action? Dismissed;
    public Task<IReadOnlyList<InstalledApplication>> Catalog(CancellationToken token) => Load(token);
    public Image? Icon(InstalledApplication application) => null;
    public Task Launch(InstalledApplication application)
    {
        Events.Add("launch:" + application.Id);
        return LaunchFails ? Task.FromException(new IOException("Fixture launch denied")) : Task.CompletedTask;
    }
    public StartupStatus ReadStartup() => Startup;
    public StartupStatus SetStartup(bool enabled)
    {
        StartupWrites++;
        if (StartupFails)
            throw new IOException("Could not change start at login. Try again.");
        return Startup = new(enabled ? StartupState.Enabled : StartupState.Disabled);
    }
    public bool Register(string accelerator, Action callback)
    {
        if (ShortcutsUnsupported)
            throw new PlatformNotSupportedException("Fixture shortcut service unavailable");
        if (ShortcutConflict)
            return false;
        Shortcuts.Add(accelerator, callback);
        return true;
    }
    public void Unregister(string accelerator) => Shortcuts.Remove(accelerator);
    public void Show()
    {
        Shows++;
        Visible = true;
        Events.Add("show");
    }
    public void Hide()
    {
        Visible = false;
        Events.Add("hide");
        Dismissed?.Invoke();
    }
    public void Blur() => Hide();
    public void Quit() => QuitRequested = true;
}
