using Kumiko;
using Kumiko.Desktop;

namespace ReactOnly;

/// <summary>The whole window is the React page. C# only registers native services; there is no Kumiko view or program.</summary>
static class NativeHost
{
    public const string Title = "React Only";

    /// <summary>A null dev server serves the embedded Vite bundle from memory at kumiko://localhost/.</summary>
    public static Window Open(App app, string? devServer, bool visible)
    {
        NativeServices.Register(app);
        // macOS keeps its traffic lights over the page; Windows draws its controls in React.
        // GTK's compact header already has system buttons, so Linux keeps its native title bar.
        var options = new WindowOptions(Title, 960, 640, MinWidth: 520, MinHeight: 360, Visible: visible,
            HiddenTitleBar: !OperatingSystem.IsLinux());
        var window = app.NewPage(options, devServer is null ? new(Assets: WebBundle.Load()) : new(Url: devServer));
        Action<bool> appearance = dark => NativeServices.Appearance.Emit(window, dark);
        Appearance.Changed += appearance;
        window.Closed += () => Appearance.Changed -= appearance;
        return window;
    }
}
