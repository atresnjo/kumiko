using System.Globalization;
using DiffViewer;
using DiffViewer.Sources;
using Kumiko;
using Kumiko.Runtime;

var request = Arguments.Parse(args, Environment.CurrentDirectory);
var store = ProjectFiles.Default(args);
var initial = DiffApp.Init with { Request = request, Store = store, Recent = ProjectFiles.Recent(store) };
// Outside a repository (opened from Finder or the Dock, whose folder is /), start in the project palette.
var outside = !Arguments.InRepository(request);
if (outside)
    initial = initial with
    {
        Loading = false
    };
var options = new WindowOptions(Title: "Diff", Width: 1280, Height: 820, MinWidth: 640, MinHeight: 420,
    Theme: args.Contains("--dark") ? Kumiko.UI.Theme.DarkTheme : null);
string[] offscreenOptions = ["--kumiko-snapshot", "--kumiko-tree", "--kumiko-audit", "--kumiko-replay", "--kumiko-step"];
// Snapshot, tree, audit, agent, replay and step runs have no native window; zen mode only changes the layout there.
if (args.Contains("--kumiko-agent") || offscreenOptions.Any(option => Dev.Option(args, option) is not null))
{
    // Offscreen snapshots render one settled frame, so they load before the first build instead of in the background.
    if (args.Any(a => a.StartsWith("--kumiko-snapshot", StringComparison.Ordinal) || a.StartsWith("--kumiko-tree", StringComparison.Ordinal)))
    {
        var document = Loader.Load(request, false, CancellationToken.None).GetAwaiter().GetResult();
        initial = DiffApp.Update(initial, new Msg.Loaded(initial.Generation, document)).Item1;
        if (args.Contains("--split"))
            initial = DiffApp.Update(initial, new Msg.SetMode(DiffViewer.Core.ViewMode.Split)).Item1;
        if (args.Contains("--zen"))
        {
            // Zen mode reads its file whole, so it loads again with that file.
            initial = DiffApp.Update(initial, new Msg.ToggleZen()).Item1;
            document = Loader.Load(request, false, CancellationToken.None, initial.Zen?.Path).GetAwaiter().GetResult();
            initial = DiffApp.Update(initial, new Msg.Loaded(initial.Generation, document)).Item1;
        }
    }
    // Offscreen runs stay reproducible: no pull requests, and the palette opens without reading commits.
    if (outside)
        initial = DiffApp.Update(initial, new Msg.OpenQuick()).Item1;
    using var offscreen = DiffApp.CreateProgram(initial, new KumikoServices().Add<IFullScreen>(new NoFullScreen()));
    offscreen.RetainHistory = false;
    Dev.Run("Diff", 1280, 820, offscreen, args, options);
    return;
}
// An interactive run owns its App, so zen mode can take the window full screen.
if (Dev.Option(args, "--kumiko-culture") is { } culture)
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
using var app = new App(new("Diff"));
var fullScreen = new WindowFullScreen(app);
using var program = DiffApp.CreateProgram(initial with
{
    GitHub = GitHubConfig.Default()
}, new KumikoServices().Add<IFullScreen>(fullScreen));
if (outside)
    program.Send(new Msg.OpenQuick());
// A long-running viewer keeps only the current model; reloads would otherwise retain every document.
program.RetainHistory = false;
program.DevMode = args.Contains("--kumiko-dev");
program.DebugEnabled = args.Contains("--kumiko-debug");
if (Dev.Option(args, "--kumiko-record") is { } record)
    program.Record(record);
fullScreen.Window = app.NewWindow(options with
{
    DevMode = program.DevMode
}, program);
if (HotReload.Watching)
    HotReload.Persist(program, app, Path.Combine(AppContext.BaseDirectory, "kumiko-hot", "Diff.json"));
app.Run();
