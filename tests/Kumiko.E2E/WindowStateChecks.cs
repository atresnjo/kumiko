using Kumiko;
using Kumiko.Desktop;
using Kumiko.UI;

/// <summary>Remembered window placement across monitor changes, and its isolated storage.</summary>
static class WindowStateChecks
{
    public static void Run(string output, Action<bool, string> check)
    {
        var primary = new Screen(new(0, 0, 1920, 1080), new(0, 25, 1920, 1055), 2, true);
        var secondary = new Screen(new(1920, 0, 1280, 800), new(1920, 0, 1280, 800), 1, false);
        var onSecondary = new WindowState(2100, 100, 800, 600);
        check(onSecondary.Fit([primary, secondary]) == onSecondary, "a window on a connected secondary monitor stays there");
        var moved = onSecondary.Fit([primary]);
        check(moved.X >= 0 && moved.Right <= 1920 && moved.Y >= 25 && moved.Width == 800 && moved.Height == 600,
            "a window from a removed monitor moves onto the primary work area at its size");
        var huge = new WindowState(-500, -50, 4000, 3000).Fit([primary]);
        check(huge == new WindowState(0, 25, 1920, 1055), "an oversized window shrinks to the work area");
        var underBar = new WindowState(100, 0, 800, 600).Fit([primary]);
        check(underBar.Y == 25 && underBar.X == 100, "a window is kept below the menu bar or above the taskbar");
        var maximized = new WindowState(2100, 100, 800, 600, Maximized: true).Fit([primary]);
        check(maximized.Maximized && maximized.Width == 800, "maximized windows keep their normal size for restoring");
        check(new WindowState(10, 30, 0, -5).Fit([primary]) is { Width: >= 100, Height: >= 100 }, "degenerate sizes are repaired");

        var directory = Path.Combine(Path.GetTempPath(), "kumiko-window-state-" + Environment.ProcessId);
        Directory.CreateDirectory(directory);
        try
        {
            var store = new WindowStateStore(directory);
            check(store.Load("main") is null, "a missing state file loads as nothing");
            store.Save("main", onSecondary);
            check(store.Load("main") == onSecondary, "saved window state loads unchanged");
            check(Directory.GetFiles(directory).All(f => !f.EndsWith(".tmp", StringComparison.Ordinal)), "state is written atomically");
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
            check(store.Load("broken") is null, "a malformed state file is ignored");
            File.Copy(Path.Combine(directory, "main.json"), Path.Combine(output, "window-state.json"), true);
            check(!File.ReadAllText(Path.Combine(output, "window-state.json")).Contains(Environment.UserName, StringComparison.Ordinal) ||
                Environment.UserName.Length < 3, "the state file holds only geometry");

            using var app = new App(new("Window state", Hidden: true)) { WindowStates = store };
            var remembered = new WindowState(140, 160, 520, 380);
            store.Save("restored", remembered);
            using (var window = app.NewWindow(new WindowOptions(Title: "Window state", Width: 300, Height: 200, Visible: false)
            {
                RememberAs = "restored"
            }, _ =>
            {
            }))
            {
                app.RunFor(TimeSpan.FromMilliseconds(50));
                var state = window.State;
                check(state is { Width: 520, Height: 380 }, $"a remembered window reopens at its saved size ({state})");
                check(window.Context!.Size.Width == 520, "the view lays out at the restored size");
                window.Restore(state! with
                {
                    Width = 600
                });
                app.RunFor(TimeSpan.FromMilliseconds(50));
                check(window.State?.Width == 600, "Restore applies a new placement");
            }
            check(store.Load("restored")?.Width == 600, "closing a remembered window saves its placement");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
