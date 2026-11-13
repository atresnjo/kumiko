using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.UI;
using Launcher;

static class ReviewGeometryFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        using var app = new App(new("Hidden Windows coordinate fixture", false, true));
        using var program = PassiveApp.CreateProgram();
        using var panel = new LauncherPanel(app, program, new(Width: 680, Height: 520), new PrivatePresentation(), physicalScreenPixels: true);
        var handle = panel.Window.NativeHandle;
        using var stream = File.Create(Path.Combine(output, "review-geometry.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("boundary", "Windows coordinate policy injected into real hidden native panel on " + System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier);
        json.WriteStartArray("placements");
        foreach (var scale in new[] { 1f, 2f, 1.5f, 2f, 1f })
        {
            var screen = new Screen(new(-3000, -500, 3000, 2000), new(-2980, -460, 2960, 1920), scale, false);
            for (var repeat = 0; repeat < 3; repeat++)
            {
                panel.Show(screen);
                var bounds = panel.Window.Bounds;
                check(bounds.Width == 680 * scale && bounds.Height == 520 * scale && bounds.X == -2980 + (2960 - 680 * scale) / 2,
                    "repeated Windows-coordinate Show fits logical size using the target screen DPI without accumulating scale");
                check(app.Windows.Count == 1 && panel.Window.NativeHandle == handle && !panel.Window.IsVisible,
                    "mixed DPI geometry retains one never-published native panel");
                panel.Hide();
            }
            json.WriteStartObject();
            json.WriteNumber("scale", scale);
            json.WriteNumber("width", panel.Window.Bounds.Width);
            json.WriteNumber("height", panel.Window.Bounds.Height);
            json.WriteEndObject();
        }
        var small = new Screen(new(5, 8, 1000, 700), new(5, 8, 1000, 700), 2, true);
        panel.Show(small);
        check(panel.Window.Bounds == small.WorkArea, "physical-pixel fitting clips a 2x panel to a smaller work area");
        panel.Hide();
        using var logicalProgram = PassiveApp.CreateProgram();
        using var logical = new LauncherPanel(app, logicalProgram, new(Width: 680, Height: 520), new PrivatePresentation(), physicalScreenPixels: false);
        logical.Show(new(new(0, 0, 2000, 1600), new(0, 0, 2000, 1600), 2, true));
        check(logical.Window.Bounds.Width == 680 && logical.Window.Bounds.Height == 520,
            "macOS and Linux screen geometry stays logical even on a 2x display");
        foreach (var theme in new[] { Theme.Light, Theme.DarkTheme })
        {
            using var view = LauncherApp.CreateProgram(LauncherApp.Init with
            {
                Loading = false,
                Entries = [.. FixtureLauncherDesktop.Applications]
            });
            using var tester = new Tester(view, new(680, 520, 2, theme, true, new(_ => null)));
            tester.Settle();
            var name = "review-2x-" + (theme == Theme.Light ? "light" : "dark");
            tester.Screenshot(Path.Combine(output, name + ".png"));
            File.WriteAllText(Path.Combine(output, name + ".txt"), tester.Dump());
        }
        json.WriteEndArray();
        json.WriteString("renderBoundary", "Tester logical 680x520 at scale 2; Windows SetWindowPos/DPI callbacks require Windows execution");
        json.WriteEndObject();
    }

    sealed class PrivatePresentation : ILauncherPanelPresentation
    {
        public bool IsVisible
        {
            get; private set;
        }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
    }
}
