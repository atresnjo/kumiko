using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;
using Launcher;
using O = Kumiko.Platform.ObjC;

static partial class NativeFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var screen = new Kumiko.Desktop.Screen(new(-1400, -200, 1400, 900), new(-1380, -170, 1360, 840), 1, false);
        var placement = LauncherPanel.Placement(screen.WorkArea, new(680, 520));
        check(placement == new Rect(-1040, -170 + 320f / 3, 680, 520), "placement respects the requested display's negative origin and work area");
        check(LauncherPanel.Placement(new(5, 8, 200, 100), new(680, 520)) == new Rect(5, 8, 200, 100), "placement shrinks oversized panels within a small work area");
        using var app = new App(new("Launcher hidden native checks", false, true)
        {
            ActivationPolicy = ActivationPolicy.Accessory
        });
        using var program = LauncherApp.CreateProgram(LauncherApp.Init with
        {
            Loading = false
        });
        var publication = new PrivatePanel();
        using (var panel = new LauncherPanel(app, program, new(Width: 680, Height: 520, Icons: new(_ => null)), publication))
        {
            check(app.Windows.Count == 1 && !panel.Window.IsVisible, "panel construction owns exactly one hidden native window");
            if (OperatingSystem.IsMacOS())
                MacPanel(panel.Window, check);
            for (var i = 0; i < 30; i++)
            {
                panel.Show(screen);
                panel.Hide();
            }
            check(app.Windows.Count == 1 && !panel.Window.IsVisible && !panel.IsVisible, "thirty private show/hide cycles retain one native panel without publishing it");
            panel.Show(screen);
            panel.Window.Close();
            check(!panel.IsVisible && app.Windows.Count == 1, "close cancellation dismisses and retains the native panel");
            app.RunFor(TimeSpan.FromMilliseconds(30));
            Png.Write(Path.Combine(output, "native-hidden-panel.png"), panel.Window.Capture());
            File.WriteAllText(Path.Combine(output, "native-hidden-panel.txt"), panel.Window.Context!.Accessibility.Count + " native semantic elements; window never ordered front\n");
        }
        check(app.Windows.Count == 0, "panel disposal releases its owned native window");
        using (var passive = PassiveApp.CreateProgram())
        using (var panel = new LauncherPanel(app, passive, null, new PrivatePanel()))
        {
            panel.Show(screen);
            panel.Window.Input(input => input.Key = Key.Escape);
            app.RunFor(TimeSpan.FromMilliseconds(20));
            check(!panel.IsVisible && app.Windows.Count == 1, "unhandled Escape dismisses the native panel after view precedence without quitting");
        }
        Catalog(app, output, check);
        Startup(app, output, check);
        Shortcuts(app, check);
    }

    static void Catalog(App app, string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var root = Path.Combine(output, "catalog-fixtures");
        Directory.CreateDirectory(root);
        Bundle(root, "Alpha", "dev.fixture.alpha", "Localized Alpha");
        Bundle(root, "Duplicate", "dev.fixture.alpha", "Duplicate identity");
        Bundle(root, "Beta", "dev.fixture.beta", "Beta");
        using (var catalog = new ApplicationCatalog(app, [root]))
        {
            var task = catalog.EnumerateAsync();
            Pump(app, task);
            var apps = task.GetAwaiter().GetResult();
            check(apps.Count == 2 && apps.Any(a => a.Name == "Localized Alpha"), "real NSBundle metadata over temporary app bundles yields display names and deduplicated bundle IDs");
            var refused = false;
            try
            {
                _ = catalog.LaunchAsync(apps[0]);
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }
            check(refused, "hidden native catalog rejects a real application launch");
            var icons = new FileIconCache();
            check(icons.Get(apps[0].Path, 32)?.Pixels.Width == 32, "native file icon rasterization reads only the temporary app bundle");
            for (var i = 0; i < 140; i++)
                icons.Get(Path.Combine(root, "file-" + i), 16);
            check(icons.Count == 128, "file icon lifetime admission remains bounded at 128 entries");
        }
        var late = new ApplicationCatalog(app, [root]);
        var completion = late.EnumerateAsync();
        late.Dispose();
        Pump(app, completion);
        check(completion.IsCanceled, "disposing a native catalog cancels a pending worker/main-thread handoff");
    }

    static void Bundle(string root, string folder, string id, string name)
    {
        var contents = Path.Combine(root, folder + ".app", "Contents");
        Directory.CreateDirectory(contents);
        File.WriteAllText(Path.Combine(contents, "Info.plist"), $"<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>{id}</string><key>CFBundleName</key><string>{name}</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>");
    }

    static void Pump(App app, Task completion)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!completion.IsCompleted && DateTime.UtcNow < deadline)
            app.RunFor(TimeSpan.FromMilliseconds(10));
        if (!completion.IsCompleted)
            throw new TimeoutException("Native dispatch did not complete.");
    }

    sealed class PrivatePanel : ILauncherPanelPresentation
    {
        public bool IsVisible
        {
            get; private set;
        }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
    }

    static void MacPanel(Window window, Action<bool, string> check)
    {
        var handle = window.NativeHandle;
        check(O.B(handle, "isKindOfClass:", O.Class("NSPanel")) != 0 && (O.N(handle, "styleMask") & 128) != 0, "hidden macOS launcher is an NSPanel constructed with the nonactivating style");
        check(O.B(handle, "canBecomeKeyWindow") != 0 && O.B(handle, "canBecomeMainWindow") == 0, "native panel accepts keyboard input without becoming the main window");
        check(O.B(O.O(handle, "contentView"), "needsPanelToBecomeKey") != 0, "native content view requests panel keyboard eligibility");
        check((O.N(handle, "collectionBehavior") & 257) == 257 && O.N(handle, "level") == 3, "native panel floats and opts into all Spaces and full-screen auxiliary behavior");
        if (OperatingSystem.IsMacOSVersionAtLeast(13))
            check((O.N(handle, "collectionBehavior") & (1u << 18)) != 0, "macOS 13+ panel can join other applications' full-screen sets");
    }
}
