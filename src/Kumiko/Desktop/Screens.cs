using Kumiko.UI;

namespace Kumiko.Desktop;

/// <summary>
/// A connected display (top-left origin, y down) in points on macOS and Linux and virtual-screen pixels on
/// Windows. <see cref="WorkArea"/> excludes the menu bar, dock and taskbar; <see cref="Scale"/> is device pixels
/// per point.
/// </summary>
public sealed record Screen(Rect Bounds, Rect WorkArea, float Scale, bool Primary);

/// <summary>The displays connected to this computer.</summary>
public static partial class Screens
{
    /// <summary>All displays, primary first. Empty when the platform has no display connection.</summary>
    public static IReadOnlyList<Screen> All
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            var screens = OperatingSystem.IsMacOS() ? Mac() : OperatingSystem.IsWindows() ? Windows() : Linux();
            return [.. screens.OrderByDescending(s => s.Primary)];
        }
    }
}
