using System.Globalization;
using Kumiko;
using Kumiko.Runtime;

namespace CRMLite;

/// <summary>
/// Starts CRM Lite. Snapshot, tree, audit, agent, replay and step runs go through <c>Dev.Run</c> with an
/// <see cref="OfflineDesktop"/>, so they never construct native shell services. An interactive run owns its App,
/// installs the native menu and binds a <see cref="NativeCrmDesktop"/>.
/// </summary>
public static class CrmDesktopHost
{
    static readonly string[] OffscreenOptions = ["--kumiko-snapshot", "--kumiko-tree", "--kumiko-audit", "--kumiko-replay", "--kumiko-step"];

    public static bool Offscreen(string[] args) => args.Contains("--kumiko-agent", StringComparer.Ordinal) ||
        OffscreenOptions.Any(option => Dev.Option(args, option) is not null);

    public static void Run(string title, CrmModel initial, FakeCrmBackend backend, Screen? route, string[] args, WindowOptions options)
    {
        if (Offscreen(args))
        {
            using var offscreen = CrmApp.CreateProgram(initial, backend.Services(new OfflineDesktop("Offscreen runs never post notifications.")));
            Start(offscreen, route);
            Dev.Run(title, options.Width, options.Height, offscreen, args, options);
            return;
        }
        if (Dev.Option(args, "--kumiko-culture") is { } culture)
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        using var app = new App(new(title));
        using var desktop = new NativeCrmDesktop(app);
        using var program = CrmApp.CreateProgram(initial, backend.Services(desktop));
        program.DevMode = args.Contains("--kumiko-dev", StringComparer.Ordinal);
        program.DebugEnabled = args.Contains("--kumiko-debug", StringComparer.Ordinal);
        if (Dev.Option(args, "--kumiko-record") is { } record)
            program.Record(record);
        Start(program, route);
        app.SetMenu(CrmMenu.Template(title));
        app.NewWindow(options with
        {
            DevMode = options.DevMode || program.DevMode
        }, program);
        if (HotReload.Watching)
            HotReload.Persist(program, app, Path.Combine(AppContext.BaseDirectory, "kumiko-hot", title + ".json"));
        app.Run();
    }

    static void Start(Program<CrmModel, CrmMsg> program, Screen? route)
    {
        if (route is not null)
            program.Send(new CrmMsg.Go(route));
    }
}
