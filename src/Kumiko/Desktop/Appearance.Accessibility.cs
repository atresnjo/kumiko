using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public static partial class Appearance
{
    /// <summary>
    /// Whether the user asked for more contrast: Increase Contrast on macOS, a high-contrast theme on Windows,
    /// or a HighContrast GTK theme on Linux.
    /// </summary>
    public static bool HighContrast
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            if (OperatingSystem.IsMacOS())
            {
                using var pool = new AutoreleasePool();
                return O.B(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "accessibilityDisplayShouldIncreaseContrast") != 0;
            }
            if (OperatingSystem.IsWindows())
            {
                var info = new HighContrastInfo
                {
                    Size = (uint)Marshal.SizeOf<HighContrastInfo>()
                };
                return SystemParametersInfoW(0x0042, info.Size, ref info, 0) != 0 && (info.Flags & 1) != 0;
            }
            return GtkThemeName().Contains("HighContrast", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The system text-size multiplier (1 = normal): Windows "Make text bigger" or the GNOME text scaling factor.
    /// macOS has no system-wide setting for AppKit text and reports 1.
    /// </summary>
    public static float TextScale
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            if (OperatingSystem.IsWindows())
                return WindowsTextScale();
            if (OperatingSystem.IsMacOS())
                return 1;
            var settings = gtk_settings_get_default();
            if (settings == 0)
                return 1;
            g_object_get(settings, "gtk-xft-dpi", out var dpi, 0);
            return dpi > 0 ? Math.Clamp(dpi / 1024f / 96f, 1, 3) : 1;
        }
    }

    /// <summary>Chooses the default window theme from the system preferences.</summary>
    internal static Theme ThemeFor(bool dark, bool highContrast, float textScale, Theme? systemColors)
    {
        var theme = highContrast ? systemColors ?? (dark ? UI.Theme.HighContrastDark : UI.Theme.HighContrast) : dark ? UI.Theme.DarkTheme : UI.Theme.Light;
        return textScale > 1.01f && float.IsFinite(textScale) ? theme.WithTextScale(Math.Min(textScale, 3)) : theme;
    }

    /// <summary>The user's Windows high-contrast colors mapped onto the high-contrast theme.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static Theme SystemColors()
    {
        static Color System(int index)
        {
            var value = GetSysColor(index);
            return new((value & 255) / 255f, ((value >> 8) & 255) / 255f, ((value >> 16) & 255) / 255f);
        }
        var window = System(5);
        var text = System(8);
        var highlight = System(13);
        var highlightText = System(14);
        var hot = System(26);
        return UI.Theme.HighContrast with
        {
            Dark = window.R * .2126f + window.G * .7152f + window.B * .0722f < .5f,
            Background = window,
            Surface = window,
            SurfaceHover = System(15),
            Text = text,
            Muted = text,
            Border = text,
            InputBorder = text,
            Accent = highlight,
            AccentHover = highlight,
            AccentText = highlightText,
            Selection = highlight,
            SelectionText = highlightText,
            FocusRing = hot,
            Track = text,
            Knob = window,
            Danger = text,
            DangerText = window,
            Success = text
        };
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static float WindowsTextScale() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Accessibility",
        "TextScaleFactor", 100) is int percent && percent > 100 ? Math.Min(percent / 100f, 2.25f) : 1;

    static string GtkThemeName()
    {
        var settings = gtk_settings_get_default();
        if (settings == 0)
            return Environment.GetEnvironmentVariable("GTK_THEME") ?? "";
        g_object_get_pointer(settings, "gtk-theme-name", out var name, 0);
        if (name == 0)
            return "";
        var value = Marshal.PtrToStringUTF8(name) ?? "";
        g_free(name);
        return value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HighContrastInfo
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    static extern int SystemParametersInfoW(uint action, uint parameter, ref HighContrastInfo value, uint flags);
    [DllImport("user32.dll")]
    static extern uint GetSysColor(int index);
    [DllImport(Gtk.GObject, EntryPoint = "g_object_get")]
    static extern void g_object_get_pointer(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, out nint value, nint end);
    [DllImport(Gtk.GLib)]
    static extern void g_free(nint value);
}
