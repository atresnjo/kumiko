using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace Kumiko.Desktop;
/// <summary>
/// Chooses the AppUserModelID for unpackaged processes. An ID the application already set is used as
/// is. Otherwise Kumiko registers its deterministic ID per user under
/// HKCU\Software\Classes\AppUserModelId (DisplayName plus an ownership mark, no icon) and adopts it
/// with SetCurrentProcessExplicitAppUserModelID while the App has no windows; that call is startup-only,
/// so later owners skip it to avoid regrouping taskbar buttons (the notifier names the ID explicitly).
/// The registration persists so Action Center can name delivered toasts after the process exits.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe class WindowsToastIdentity
{
    const string Classes = @"Software\Classes\AppUserModelId\";
    const string Owner = "KumikoExecutable";
    internal static string Prepare(App app)
    {
        if (Explicit() is { } existing)
            return existing;
        string executable = Environment.ProcessPath ?? "";
        string id = WindowsToastContent.ApplicationId(app.Options.Name, executable);
        try
        {
            Register(id, app.Options.Name, executable);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            throw new PlatformNotSupportedException(@"Registering the toast AppUserModelID under HKCU\" + Classes + id + " failed: " + ex.Message, ex);
        }

        if (app.Windows.Count > 0)
            return id;
        int status = SetCurrentProcessExplicitAppUserModelID(id);
        if (status < 0)
            throw new PlatformNotSupportedException($"SetCurrentProcessExplicitAppUserModelID failed (HRESULT 0x{status:X8}).");
        return id;
    }

    // A registration without this executable's mark belongs to someone else and is left unchanged.
    static void Register(string id, string name, string executable)
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(Classes + id))
        {
            if (existing is not null && !Equals(existing.GetValue(Owner), executable))
                return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(Classes + id);
        key.SetValue("DisplayName", name);
        key.SetValue(Owner, executable);
    }

    static string? Explicit()
    {
        nint value = 0;
        if (GetCurrentProcessExplicitAppUserModelID(&value) < 0 || value == 0)
            return null;
        try
        {
            var text = Marshal.PtrToStringUni(value);
            return string.IsNullOrEmpty(text) ? null : text;
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")]
    static extern int GetCurrentProcessExplicitAppUserModelID(nint* id);
}
