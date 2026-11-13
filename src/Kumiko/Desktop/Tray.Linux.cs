using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Desktop;
/// <summary>Ayatana exports StatusNotifierItem and dbusmenu to the session bus.</summary>
internal sealed unsafe class LinuxTrayBackend : ITrayBackend
{
    readonly nint library;
    readonly string directory;
    nint indicator;
    Action? releaseMenu;
    readonly string id;
    internal LinuxTrayBackend(App app, TrayOptions options)
    {
        app.CheckThread();
        id = options.Id;
        foreach (var name in new[]
        {
            "libayatana-appindicator3.so.1",
            "libappindicator3.so.1"
        }

        )
            if (NativeLibrary.TryLoad(name, out library))
                break;
        if (library == 0)
            throw new PlatformNotSupportedException("No StatusNotifierItem bridge is installed. Install libayatana-appindicator3; a direct SNI backend is not implemented.");
        directory = Path.Combine(Path.GetTempPath(), "kumiko-tray-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    nint Export(string name) => NativeLibrary.GetExport(library, name);
    public Rect Bounds => throw new PlatformNotSupportedException("StatusNotifierItem does not expose tray icon geometry to applications.");

    public void Update(TrayOptions options)
    {
        if (options.Title.Length > 0 || options.ToolTip.Length > 0)
            throw new PlatformNotSupportedException("This appindicator bridge cannot set status icon title/tooltips. A direct SNI backend is required.");
        if (options.Icon is null || options.Menu is null)
            throw new ArgumentException("The Linux appindicator backend requires a PNG and a menu.");
        string path = Path.Combine(directory, "icon.png");
        File.WriteAllBytes(path, options.Icon);
        var idText = Marshal.StringToCoTaskMemUTF8(id);
        var iconText = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            if (indicator == 0)
                indicator = ((delegate* unmanaged<nint, nint, int, nint>)Export("app_indicator_new"))(idText, iconText, 0);
            if (indicator == 0)
                throw new IOException("AppIndicator allocation failed.");
            ((delegate* unmanaged<nint, nint, void>)Export("app_indicator_set_icon"))(indicator, iconText);
            var (menu, release) = options.Menu.CreateLinuxTrayMenu();
            ((delegate* unmanaged<nint, nint, void>)Export("app_indicator_set_menu"))(indicator, menu);
            releaseMenu?.Invoke();
            releaseMenu = release;
            ((delegate* unmanaged<nint, int, void>)Export("app_indicator_set_status"))(indicator, 1);
        }
        finally
        {
            Marshal.FreeCoTaskMem(idText);
            Marshal.FreeCoTaskMem(iconText);
        }
    }

    public void PopUpMenu(Menu menu) => throw new PlatformNotSupportedException("The StatusNotifier host controls menu placement; appindicator cannot request PopUpMenu.");
    public void Dispose()
    {
        if (indicator != 0)
        {
            ((delegate* unmanaged<nint, int, void>)Export("app_indicator_set_status"))(indicator, 0);
            Gtk.g_object_unref(indicator);
            indicator = 0;
        }

        releaseMenu?.Invoke();
        Directory.Delete(directory, true);
        NativeLibrary.Free(library);
    }
}
