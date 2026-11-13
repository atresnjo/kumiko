using Kumiko;
using Kumiko.Desktop;
using Kumiko.Runtime;
using Launcher;

// Export, agent, replay and hidden routes choose fixtures before any native services exist.
string[] fixtureOptions = ["--kumiko-snapshot", "--kumiko-tree", "--kumiko-audit", "--kumiko-replay", "--kumiko-step"];
var offscreen = args.Contains("--kumiko-agent", StringComparer.Ordinal) || args.Contains("--hidden", StringComparer.Ordinal) ||
    fixtureOptions.Any(option => Dev.Option(args, option) is not null);
if (args.Contains("--hidden", StringComparer.Ordinal) && !fixtureOptions.Any(option => Dev.Option(args, option) is not null))
    args = [.. args, "--kumiko-snapshot=artifacts/launcher/hidden.png", "--kumiko-tree=artifacts/launcher/hidden.txt"];
var shortcut = Dev.Option(args, "--shortcut") ?? "Alt+Shift+Space";
var initial = LauncherApp.Init with { Shortcut = shortcut, ShortcutDraft = shortcut };
using var program = LauncherApp.CreateProgram(initial);
if (offscreen)
{
    var fixture = new FixtureLauncherDesktop();
    using var controller = new LauncherController(program, fixture, static action => action());
    controller.Start();
    Dev.Run("Kumiko Launcher", 680, 520, program, args, new(Icons: new(_ => null)));
    return;
}

using var app = new App(new("Kumiko Launcher", QuitOnLastWindow: false) { ActivationPolicy = ActivationPolicy.Accessory });
if (!app.RequestSingleInstanceLock(NativeLauncherDesktop.Identity))
    return;
using var desktop = new NativeLauncherDesktop(app, program);
using var host = new LauncherController(program, desktop, app.Dispatch);
app.SecondInstance += _ => host.Summon();
using var menu = new Menu(app,
    new MenuItem("Show launcher", Click: _ => host.Summon()),
    new MenuItem("Settings", Click: _ =>
    {
        host.Summon();
        program.Send(new LauncherMsg.ShowSettings());
    }),
    new MenuItem("Quit launcher", Click: _ => app.Quit()));
Tray? tray = null;
string? trayError = null;
try
{
    tray = new Tray(app, new("launcher", Title: "K", ToolTip: "Kumiko Launcher", Menu: menu));
    tray.Click += host.Toggle;
}
catch (Exception error) when (error is PlatformNotSupportedException or InvalidOperationException)
{
    trayError = "Tray unavailable: " + error.Message + " Use the shortcut or Settings to quit.";
}
using (tray)
{
    host.Start();
    if (trayError is not null)
        program.Send(new LauncherMsg.Failure(trayError));
    app.Run();
}
