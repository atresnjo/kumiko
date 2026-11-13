using System.Diagnostics;
using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public static partial class Appearance
{
    public static event Action<bool>? Changed;
    internal static void NotifyChanged() => Changed?.Invoke(IsDark);
    public static int? ThermalState
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
                return null;
            using var pool = new AutoreleasePool();
            return (int)O.N(O.O(O.Class("NSProcessInfo"), "processInfo"), "thermalState");
        }
    }

    public static bool ReduceMotion
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            if (OperatingSystem.IsMacOS())
            {
                using var pool = new AutoreleasePool();
                return O.B(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "accessibilityDisplayShouldReduceMotion") != 0;
            }

            if (OperatingSystem.IsWindows())
                return SystemParametersInfoW(0x1042, 0, out var enabled, 0) != 0 && enabled == 0;
            var settings = gtk_settings_get_default();
            if (settings == 0)
                return false;
            g_object_get(settings, "gtk-enable-animations", out var animate, 0);
            return animate == 0;
        }
    }

    [DllImport("user32.dll")]
    static extern int SystemParametersInfoW(uint action, uint parameter, out int value, uint flags);
    public static bool IsDark
    {
        get
        {
            Runtime.KumikoExecution.CheckUI();
            if (OperatingSystem.IsMacOS())
            {
                using var pool = new AutoreleasePool();
                var defaults = O.O(O.Class("NSUserDefaults"), "standardUserDefaults");
                return O.Text(O.O(defaults, "stringForKey:", O.TempString("AppleInterfaceStyle"))) == "Dark";
            }

            if (OperatingSystem.IsWindows())
                return WindowsDark();
            var settings = gtk_settings_get_default();
            if (settings != 0)
            {
                g_object_get(settings, "gtk-application-prefer-dark-theme", out var dark, 0);
                if (dark != 0)
                    return true;
            }

            return (Environment.GetEnvironmentVariable("GTK_THEME") ?? "").Contains("dark", StringComparison.OrdinalIgnoreCase);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static bool WindowsDark() => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int value && value == 0;
    /// <summary>
    /// The theme for windows without an explicit theme: light or dark, high contrast when the system asks for it
    /// (system colors on Windows), scaled by <see cref = "TextScale"/>.
    /// </summary>
    public static Theme Theme => ThemeFor(IsDark, HighContrast, TextScale, OperatingSystem.IsWindows() && HighContrast ? SystemColors() : null);

    [DllImport(Gtk.Library)]
    static extern nint gtk_settings_get_default();
    [DllImport(Gtk.GObject)]
    static extern void g_object_get(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string property, out int value, nint end);
}

public enum DirectoryKind
{
    Home,
    Documents,
    Downloads,
    Desktop,
    AppData,
    Cache,
    Temporary
}

public static class Paths
{
    public static string Get(DirectoryKind kind) => kind switch
    {
        DirectoryKind.Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        DirectoryKind.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        DirectoryKind.Downloads => Path.Combine(Get(DirectoryKind.Home), "Downloads"),
        DirectoryKind.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        DirectoryKind.AppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        DirectoryKind.Cache => OperatingSystem.IsMacOS() ? Path.Combine(Get(DirectoryKind.Home), "Library", "Caches") : OperatingSystem.IsWindows() ? Path.Combine(Get(DirectoryKind.AppData), "Cache") : Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Get(DirectoryKind.Home), ".cache"),
        DirectoryKind.Temporary => Path.GetTempPath(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}

public static class Shell
{
    [StructLayout(LayoutKind.Sequential)]
    struct FileOperation
    {
        public nint Window;
        public uint Function;
        public nint From, To;
        public ushort Flags;
        public int Aborted;
        public nint Mapping, Title;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperationW(ref FileOperation operation);
    public static void Open(string pathOrUrl) => Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("/usr/bin/open") { ArgumentList = { "-R", Path.GetFullPath(path) } });
        else if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", Path.GetFullPath(path) } });
        else
            Open(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public static void Trash(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var url = O.O(O.Class("NSURL"), "fileURLWithPath:", O.TempString(Path.GetFullPath(path)));
            if (O.B(O.O(O.Class("NSFileManager"), "defaultManager"), "trashItemAtURL:resultingItemURL:error:", url, 0, 0) == 0)
                throw new IOException("Could not move the item to Trash.");
        }
        else if (OperatingSystem.IsLinux())
        {
            using var process = Process.Start(new ProcessStartInfo("gio") { ArgumentList = { "trash", "--", Path.GetFullPath(path) } })!;
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new IOException("Trash failed.");
        }
        else
        {
            var from = Marshal.StringToCoTaskMemUni(Path.GetFullPath(path) + '\0');
            try
            {
                var operation = new FileOperation
                {
                    Function = 3,
                    From = from,
                    Flags = 0x40 | 0x10 | 0x400
                };
                if (SHFileOperationW(ref operation) != 0 || operation.Aborted != 0)
                    throw new IOException("Could not move the item to the Recycle Bin.");
            }
            finally
            {
                Marshal.FreeCoTaskMem(from);
            }
        }
    }
}
