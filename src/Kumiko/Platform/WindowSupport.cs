namespace Kumiko.Platform;

internal static class WindowSupport
{
    internal static void Validate(WindowOptions options)
    {
        if (OperatingSystem.IsMacOS() && options.ContentProtection)
            throw new PlatformNotSupportedException("macOS provides no public API that prevents ScreenCaptureKit capture. NSWindow sharingType is a legacy hint, not content protection.");
        if (!OperatingSystem.IsMacOS() && options.TrafficLightPosition is not null)
            throw new PlatformNotSupportedException("Traffic light positions require macOS.");
        if (OperatingSystem.IsMacOS() && (options.SkipTaskbar || options.AutoHideMenuBar))
            throw new PlatformNotSupportedException("macOS has an application Dock/menu bar, not per-window taskbar entries or auto-hidden menu bars.");
        if (OperatingSystem.IsMacOS() && options.Material is WindowMaterial.Mica or WindowMaterial.Acrylic)
            throw new PlatformNotSupportedException("Mica/Acrylic require Windows 11. Use Sidebar or UnderWindow on macOS.");
        if (OperatingSystem.IsWindows() && options.Material is WindowMaterial.Sidebar or WindowMaterial.UnderWindow)
            throw new PlatformNotSupportedException("Sidebar/UnderWindow materials require macOS. Use Mica or Acrylic on Windows 11.");
        if (OperatingSystem.IsLinux())
        {
            if (!options.HasShadow)
                throw new PlatformNotSupportedException("GTK 3 exposes no portable compositor shadow toggle.");
            if (options.Material != WindowMaterial.None)
                throw new PlatformNotSupportedException("GTK 3 has no portable compositor backdrop material API.");
            if (options.ContentProtection)
                throw new PlatformNotSupportedException("Linux compositors provide no portable content protection API.");
            if (!options.Movable || !options.Minimizable || !options.Maximizable)
                throw new PlatformNotSupportedException("GTK 3 does not expose individual move/minimize/maximize permission toggles.");
        }

        if (OperatingSystem.IsWindows() && options.VisibleOnAllWorkspaces)
            throw new PlatformNotSupportedException("Windows exposes no supported public pin-to-all-virtual-desktops API.");
    }
}
