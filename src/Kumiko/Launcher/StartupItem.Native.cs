using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Kumiko.Platform;
using Microsoft.Win32;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal sealed class NativeStartupItem(App app, string identity, string name) : IStartupItemBackend
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    string Executable => Environment.ProcessPath ?? throw new InvalidOperationException("No app executable.");
    string Marker => "--kumiko-login=" + identity;
    internal string WindowsCommand => "\"" + Executable + "\" " + Marker;
    string LinuxPath => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", identity + ".desktop");
    string LinuxContent => "[Desktop Entry]\nType=Application\nName=" + name.Replace("\\", "\\\\", StringComparison.Ordinal) + "\nExec=\"" +
        Executable.Replace("\\", "\\\\\\\\", StringComparison.Ordinal).Replace("\"", "\\\\\\\"", StringComparison.Ordinal).Replace("`", "\\\\`", StringComparison.Ordinal).Replace("$", "\\\\$", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal) +
        "\" " + Marker + "\nX-Kumiko-Owner=" + identity + "\n";

    internal static bool ReadMacLoginEvent(nint appleEvent) => O.N(appleEvent, "eventClass") == 0x61657674 &&
        O.N(appleEvent, "eventID") == 0x6f617070 && O.N(O.O(appleEvent, "paramDescriptorForKeyword:", (nuint)0x70726474), "enumCodeValue") == 0x6c676974;
    public bool WasLaunchedAtLogin => OperatingSystem.IsMacOS()
        ? app.Backend is MacApp mac && mac.LaunchAtLogin.IsCompletedSuccessfully && mac.LaunchAtLogin.Result
        : LaunchedWith(Environment.GetCommandLineArgs().Skip(1));

    // The marker is an option, so App.ParseOpenArguments never delivers it as OpenFile; after `--` it is an operand.
    internal bool LaunchedWith(IEnumerable<string> arguments) => arguments.TakeWhile(argument => argument != "--").Contains(Marker, StringComparer.Ordinal);
    public Task<bool> GetLaunchAtLoginAsync(CancellationToken cancellationToken)
    {
        app.CheckThread();
        return (OperatingSystem.IsMacOS() && app.Backend is MacApp mac ? mac.LaunchAtLogin : Task.FromResult(WasLaunchedAtLogin)).WaitAsync(cancellationToken);
    }

    StartupStatus? Unsupported()
    {
        if (OperatingSystem.IsMacOS())
        {
            if (!OperatingSystem.IsMacOSVersionAtLeast(13))
                return new(StartupState.Unsupported, "Start at login requires macOS 13 or later.");
            using var pool = new AutoreleasePool();
            var bundle = O.O(O.Class("NSBundle"), "mainBundle");
            if (O.Text(O.O(bundle, "bundleIdentifier")) != identity || !O.Text(O.O(bundle, "bundlePath")).EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return new(StartupState.Unsupported, "Run the installed .app with its matching bundle identity.");
        }
        else if (!File.Exists(Executable) || Path.GetFileNameWithoutExtension(Executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return new(StartupState.Unsupported, "Publish an apphost before enabling start at login.");
        return null;
    }

    public StartupStatus Read()
    {
        if (Unsupported() is { } unsupported)
            return unsupported;
        if (OperatingSystem.IsMacOS())
            return Mac(false, false);
        if (OperatingSystem.IsWindows())
            return ReadWindows();
        return ReadLinux(LinuxPath, LinuxContent);
    }

    public StartupStatus SetEnabled(bool enabled)
    {
        var state = Read();
        if (state.State is StartupState.Unsupported or StartupState.OwnedByAnother)
            throw new InvalidOperationException(state.Detail);
        if (enabled && state.State is StartupState.Enabled or StartupState.RequiresApproval || !enabled && state.State == StartupState.Disabled)
            return state;
        if (OperatingSystem.IsMacOS())
            return Mac(true, enabled);
        if (OperatingSystem.IsWindows())
            WriteWindows(enabled);
        else
            WriteLinux(LinuxPath, LinuxContent, enabled);
        return Read();
    }

    [SupportedOSPlatform("windows")]
    StartupStatus ReadWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(identity);
        return value is null ? new(StartupState.Disabled) : value is string text && text == WindowsCommand ? new(StartupState.Enabled) : new(StartupState.OwnedByAnother, "A different executable owns this startup identity.");
    }

    [SupportedOSPlatform("windows")]
    void WriteWindows(bool enabled)
    {
        if (WindowsCommand.Length > 260)
            throw new InvalidOperationException("Windows Run command exceeds 260 characters.");
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        // Recheck ownership immediately before mutation.
        if (ReadWindows().State == StartupState.OwnedByAnother)
            throw new InvalidOperationException("Startup registration ownership changed.");
        if (enabled)
            key.SetValue(identity, WindowsCommand, RegistryValueKind.String);
        else
            key.DeleteValue(identity, false);
    }

    internal static StartupStatus ReadLinux(string path, string content)
    {
        if (new FileInfo(path).LinkTarget is not null)
            return new(StartupState.OwnedByAnother, "A symbolic link owns this startup identity.");
        if (!File.Exists(path))
            return new(StartupState.Disabled);
        if (File.ReadAllText(path) != content)
            return new(StartupState.OwnedByAnother, "A different or edited startup entry owns this identity.");
        return new(StartupState.Enabled);
    }

    internal static void WriteLinux(string path, string content, bool enabled)
    {
        if (ReadLinux(path, content).State == StartupState.OwnedByAnother)
            throw new InvalidOperationException("Refusing to replace a foreign startup entry.");
        if (!enabled)
        {
            File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static unsafe StartupStatus Mac(bool write, bool enabled, nint fixtureService = 0)
    {
        if (fixtureService == 0)
            NativeLibrary.Load("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement");
        using var pool = new AutoreleasePool();
        var service = fixtureService != 0 ? fixtureService : O.O(O.Class("SMAppService"), "mainAppService");
        if (write)
        {
            nint error = 0;
            if (O.B(service, enabled ? "registerAndReturnError:" : "unregisterAndReturnError:", (nint)(&error)) == 0)
                throw new IOException(error == 0 ? "Login registration failed." : O.Text(O.O(error, "localizedDescription")));
        }
        return (int)O.N(service, "status") switch
        {
            0 => new(StartupState.Disabled),
            1 => new(StartupState.Enabled),
            2 => new(StartupState.RequiresApproval, "Approve the launcher in System Settings → General → Login Items."),
            _ => new(StartupState.Unsupported, "Service Management could not find this packaged application.")
        };
    }
}
