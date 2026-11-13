using Kumiko.UI;

namespace Kumiko;

public sealed record WindowOptions(string Title = "", float Width = 800, float Height = 600, float MinWidth = 200, float MinHeight = 150, bool Resizable = true, bool Visible = true, bool HiddenTitleBar = false, bool AlwaysOnTop = false, float? X = null, float? Y = null, Theme? Theme = null, ApplicationIconCache? Icons = null, bool DevMode = false)
{
    public float MaxWidth
    {
        get; init;
    }
    public float MaxHeight
    {
        get; init;
    }
    public bool FullScreen
    {
        get; init;
    }
    public bool Maximized
    {
        get; init;
    }
    /// <summary>Creates a keyboard-capable launcher panel. macOS uses a nonactivating NSPanel.</summary>
    public bool Panel
    {
        get; init;
    }
    public bool Frameless
    {
        get; init;
    }
    public bool Transparent
    {
        get; init;
    }
    public float Opacity { get; init; } = 1;
    public WindowMaterial Material
    {
        get; init;
    }
    public Point? TrafficLightPosition
    {
        get; init;
    }
    public bool HasShadow { get; init; } = true;
    public bool Movable { get; init; } = true;
    public bool Minimizable { get; init; } = true;
    public bool Maximizable { get; init; } = true;
    public bool Closable { get; init; } = true;
    public bool SkipTaskbar
    {
        get; init;
    }
    public bool VisibleOnAllWorkspaces
    {
        get; init;
    }
    public bool ContentProtection
    {
        get; init;
    }
    public bool IgnoreMouseEvents
    {
        get; init;
    }
    public bool AutoHideMenuBar
    {
        get; init;
    }
    public Window? Parent
    {
        get; init;
    }
    public bool Modal
    {
        get; init;
    }
    /// <summary>
    /// Restores the window's last placement saved under this name and saves it again on close, fitted to the
    /// screens that are connected now. Uses <see cref = "App.WindowStates"/>.
    /// </summary>
    public string? RememberAs
    {
        get; init;
    }
}

public enum WindowMaterial
{
    None,
    Sidebar,
    UnderWindow,
    Mica,
    Acrylic
}
