using System.Collections.Immutable;
using System.Globalization;
using DiskMap;
using DiskMap.Core;
using Kumiko;
using Kumiko.Runtime;

var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var demo = args.Contains("--demo");
var folder = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) is { } given
    ? Path.GetFullPath(given.StartsWith('~') ? home + given[1..] : given)
    : home;
var options = new WindowOptions(Title: "DiskMap", Width: 1360, Height: 860, MinWidth: 760, MinHeight: 480,
    Theme: args.Contains("--dark") ? Kumiko.UI.Theme.DarkTheme : args.Contains("--light") ? Kumiko.UI.Theme.Light : null);
string[] offscreen = ["--kumiko-snapshot", "--kumiko-tree", "--kumiko-audit", "--kumiko-replay", "--kumiko-step"];
bool hidden = args.Contains("--kumiko-agent") || offscreen.Any(option => Dev.Option(args, option) is not null);
Model initial;
if (demo || hidden)
{
    // Offscreen runs show one settled frame, so they scan before the first build instead of in the background.
    var root = demo ? Demo.Root : folder;
    var tree = demo ? Demo.Home() : Scanner.Scan(folder);
    initial = DiskMapApp.Show(root, tree) with
    {
        Home = demo ? "/Users/demo" : home
    };
    if (Dev.Option(args, "--open") is { } open)
        initial = DiskMapApp.Update(initial, new Msg.Open([.. open.Split('/', StringSplitOptions.RemoveEmptyEntries)])).Item1;
    if (Dev.Option(args, "--select") is { } select)
        initial = DiskMapApp.Update(initial, new Msg.Select([.. select.Split('/', StringSplitOptions.RemoveEmptyEntries)])).Item1;
    if (Dev.Option(args, "--metric") is { } metric && Enum.TryParse<Metric>(metric, true, out var parsed))
        initial = DiskMapApp.Update(initial, new Msg.SetMetric(parsed)).Item1;
    if (!demo)
        initial = initial with
        {
            Volume = Volume.Of(folder)
        };
}
else
    initial = DiskMapApp.Start(folder) with
    {
        Home = home
    };
if (hidden)
{
    using var program = DiskMapApp.CreateProgram(initial, new KumikoServices().Add<IDesk>(new RecordingDesk()));
    program.RetainHistory = false;
    Dev.Run("DiskMap", 1360, 860, program, args, options);
    return;
}
if (Dev.Option(args, "--kumiko-culture") is { } culture)
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
using var app = new App(new("DiskMap"));
using var live = DiskMapApp.CreateProgram(initial, new KumikoServices().Add<IDesk>(new NativeDesk(app)));
// A scanned tree is large; keeping every model for time travel would keep every tree.
live.RetainHistory = false;
live.DevMode = args.Contains("--kumiko-dev");
app.NewWindow(options with
{
    DevMode = live.DevMode
}, live);
app.Run();
