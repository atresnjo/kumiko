using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

/// <summary>Everything the Windows update planner reads from or does to the process. Tests inject one;
/// <see cref="Current"/> is the real Windows process.</summary>
internal sealed record WindowsUpdateHost(
    string ProcessPath,
    int ProcessId,
    string? PackageFullName,
    string LocalAppData,
    IReadOnlyList<string> ProtectedRoots,
    string PowerShell,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    Action<string> Open,
    Action<ProcessStartInfo> Start,
    Action Quit,
    Action<Action> OnQuit)
{
    const int Success = 0;
    const int InsufficientBuffer = 122;
    const int NoPackage = 15700;

    // appmodel.h: LONG GetCurrentPackageFullName(UINT32* packageFullNameLength, PWSTR packageFullName), Windows 8+.
    [DllImport("kernel32.dll", ExactSpelling = true)]
    static extern unsafe int GetCurrentPackageFullName(uint* length, char* name);

    internal static WindowsUpdateHost Current(App app)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
            Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        return new(
            Environment.ProcessPath ?? throw new InvalidOperationException("No executable path is available."),
            Environment.ProcessId,
            PackageName(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            roots.Where(root => root.Length > 0).ToArray(),
            Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Environment.CurrentDirectory,
            Environment.GetCommandLineArgs().Skip(1).ToArray(),
            OpenWithShell,
            start => Process.Start(start)?.Dispose(),
            app.Quit,
            action => app.DidQuit += action);
    }

    static unsafe string? PackageName()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(&length, null);
        if (result == NoPackage)
            return null;
        if (result != InsufficientBuffer || length is 0 or > 1024)
            throw new Win32Exception(result, "Could not read the package identity.");
        var buffer = new char[length];
        fixed (char* name = buffer)
            result = GetCurrentPackageFullName(&length, name);
        if (result != Success)
            throw new Win32Exception(result, "Could not read the package identity.");
        return new string(buffer, 0, (int)length - 1);
    }

    static void OpenWithShell(string package)
    {
        try
        {
            Process.Start(new ProcessStartInfo(package) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException("App Installer could not open the update. Install App Installer from the Microsoft Store, or deploy the MSIX with Intune or Add-AppxPackage.", error);
        }
    }
}
