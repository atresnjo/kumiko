using System.Diagnostics;
using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

public sealed partial class ApplicationCatalog
{
    InstalledApplication? Metadata((string Path, string Id) entry)
    {
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var bundle = O.O(O.Class("NSBundle"), "bundleWithPath:", O.TempString(entry.Path));
            var id = O.Text(O.O(bundle, "bundleIdentifier"));
            if (string.IsNullOrWhiteSpace(id))
                return null;
            var name = O.Text(O.O(bundle, "objectForInfoDictionaryKey:", O.TempString("CFBundleDisplayName")));
            if (string.IsNullOrWhiteSpace(name))
                name = O.Text(O.O(bundle, "objectForInfoDictionaryKey:", O.TempString("CFBundleName")));
            return new(id, string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(entry.Path) : name, entry.Path);
        }
        if (OperatingSystem.IsWindows())
            return new(WindowsApplicationIdentity.Read(entry.Path, entry.Id), LauncherIcons.WindowsName(entry.Path), entry.Path);
        var info = g_desktop_app_info_new_from_filename(entry.Path);
        if (info == 0)
            return null;
        try
        {
            if (g_app_info_should_show(info) == 0)
                return null;
            return new(entry.Id, Marshal.PtrToStringUTF8(g_app_info_get_display_name(info)) ?? entry.Id, entry.Path);
        }
        finally
        {
            g_object_unref(info);
        }
    }

    Task LaunchNative(InstalledApplication application)
    {
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var url = O.O(O.Class("NSURL"), "fileURLWithPath:", O.TempString(application.Path));
            var configuration = O.O(O.Class("NSWorkspaceOpenConfiguration"), "configuration");
            using var completion = NativeBlock.ApplicationLaunch(error =>
            {
                if (error is null)
                    result.TrySetResult();
                else
                    result.TrySetException(new IOException(error));
            });
            O.V(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "openApplicationAtURL:configuration:completionHandler:", url, configuration, completion.Handle);
            return result.Task;
        }
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo(application.Path) { UseShellExecute = true });
            return Task.CompletedTask;
        }
        var info = g_desktop_app_info_new_from_filename(application.Path);
        if (info == 0)
            throw new IOException("The desktop entry is no longer available.");
        try
        {
            if (g_app_info_launch(info, 0, 0, out var error) == 0)
                throw new IOException(GioError(error));
        }
        finally
        {
            g_object_unref(info);
        }
        return Task.CompletedTask;
    }

    internal static string GioError(nint error)
    {
        if (error == 0)
            return "The desktop could not launch the application.";
        try
        {
            return Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "Desktop operation failed.";
        }
        finally
        {
            g_error_free(error);
        }
    }

    internal const string Gio = "libgio-2.0.so.0";
    [DllImport(Gio)]
    internal static extern nint g_desktop_app_info_new_from_filename([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
    [DllImport(Gio)]
    static extern nint g_app_info_get_display_name(nint info);
    [DllImport(Gio)]
    static extern int g_app_info_should_show(nint info);
    [DllImport(Gio)]
    static extern int g_app_info_launch(nint info, nint files, nint context, out nint error);
    [DllImport(Gtk.GObject)]
    internal static extern void g_object_unref(nint value);
    [DllImport(Gtk.GLib)]
    static extern void g_error_free(nint error);
}
