using Kumiko;
using Kumiko.Desktop;
using Kumiko.Rendering;
using Kumiko.Runtime;

namespace Launcher;

public sealed class NativeLauncherDesktop : ILauncherDesktop, IDisposable
{
    readonly App app;
    readonly LauncherPanel panel;
    readonly ApplicationCatalog catalog;
    readonly StartupItem startup;
    GlobalShortcut? shortcuts;
    readonly FileIconCache icons = new();
    public const string Identity = "dev.kumiko.launcher";
    public event Action? Dismissed;
    public NativeLauncherDesktop(App app, Program<LauncherModel, LauncherMsg> program) : this(app, program, null, null)
    {
    }

    internal NativeLauncherDesktop(App app, Program<LauncherModel, LauncherMsg> program, ApplicationCatalog? catalog, StartupItem? startup)
    {
        this.app = app;
        panel = new(app, program, new(Title: "Kumiko Launcher", Width: 680, Height: 520, Icons: new(path => icons.Get(path))));
        this.catalog = catalog ?? new(app);
        this.startup = startup ?? new(app, Identity, "Kumiko Launcher");
        panel.Dismissed += OnDismissed;
    }

    void OnDismissed() => Dismissed?.Invoke();
    public Task<IReadOnlyList<InstalledApplication>> Catalog(CancellationToken token) => catalog.EnumerateAsync(token);
    public Image? Icon(InstalledApplication application) => icons.Get(application.Path);
    public Task Launch(InstalledApplication application) => catalog.LaunchAsync(application);
    public StartupStatus ReadStartup() => startup.Read();
    public StartupStatus SetStartup(bool enabled) => startup.SetEnabled(enabled);
    public bool LoginLaunch => startup.WasLaunchedAtLogin;
    public Task<bool> GetLoginLaunchAsync(CancellationToken token) => startup.GetLaunchAtLoginAsync(token);
    public bool Register(string accelerator, Action callback) => (shortcuts ??= new(app)).Register(accelerator, callback);
    public void Unregister(string accelerator) => shortcuts?.Unregister(accelerator);
    public void Show() => panel.Show();
    public void Hide() => panel.Hide();
    public void Quit() => app.Quit();
    public void Dispose()
    {
        panel.Dismissed -= OnDismissed;
        shortcuts?.Dispose();
        startup.Dispose();
        catalog.Dispose();
        panel.Dispose();
    }
}
