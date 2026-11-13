using Kumiko.Desktop;

namespace Kumiko.UI;

public sealed partial class Tester
{
    /// <summary>The current offscreen desktop-menu projection. Callbacks use the same stale/disabled guards
    /// as native menus; retaining an old item is useful for testing removal and rebuild races.</summary>
    public IReadOnlyList<MenuItem> DesktopMenu => Context.DesktopMenuItems ?? [];
}
