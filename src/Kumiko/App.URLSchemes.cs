using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Kumiko;

public sealed partial class App
{
    static string ValidateScheme(string scheme)
    {
        if (string.IsNullOrEmpty(scheme) || !char.IsAsciiLetter(scheme[0]) || scheme.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.')))
            throw new ArgumentException("A URL scheme starts with a letter and contains only letters, digits, '+', '-' and '.'.", nameof(scheme));
        return scheme.ToLowerInvariant();
    }

    string SchemeCommand(bool windows)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
        static string Quote(string text, bool windows) => windows ? "\"" + text.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("`", "\\`", StringComparison.Ordinal).Replace("$", "\\$", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal) + "\"";
        var command = Quote(executable, windows);
        var entry = Environment.GetCommandLineArgs()[0];
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            command += " " + Quote(Path.GetFullPath(entry), windows);
        return command + (windows ? " \"%1\"" : " %u");
    }

    public void RegisterURLScheme(string scheme)
    {
        CheckSchemeAccess();
        scheme = ValidateScheme(scheme);
        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsScheme(scheme);
            return;
        }

        var (file, id) = LinuxSchemeFile(scheme);
        var currentOwner = XdgMime("query", "default", "x-scheme-handler/" + scheme).Trim();
        if (currentOwner.Length > 0 && currentOwner != id)
            throw new InvalidOperationException("This URL scheme already belongs to another desktop application.");
        if (File.Exists(file) && !File.ReadAllText(file).Contains("X-Kumiko-Command=" + SchemeCommand(false) + "\n", StringComparison.Ordinal))
            throw new InvalidOperationException("This URL scheme registration belongs to another executable.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var name = Options.Name.Replace('\n', ' ').Replace('\r', ' ');
        var command = SchemeCommand(false);
        File.WriteAllText(file, $"[Desktop Entry]\nType=Application\nName={name}\nExec={command}\nNoDisplay=true\nMimeType=x-scheme-handler/{scheme};\nX-Kumiko-Command={command}\n");
        XdgMime("default", id, "x-scheme-handler/" + scheme);
    }

    public bool IsURLSchemeRegistered(string scheme)
    {
        CheckSchemeAccess();
        scheme = ValidateScheme(scheme);
        if (OperatingSystem.IsWindows())
            return WindowsSchemeRegistered(scheme);
        var (file, id) = LinuxSchemeFile(scheme);
        return File.Exists(file) && XdgMime("query", "default", "x-scheme-handler/" + scheme).Trim() == id && File.ReadAllText(file).Contains("X-Kumiko-Command=" + SchemeCommand(false) + "\n", StringComparison.Ordinal);
    }

    public void UnregisterURLScheme(string scheme)
    {
        CheckSchemeAccess();
        scheme = ValidateScheme(scheme);
        if (OperatingSystem.IsWindows())
        {
            UnregisterWindowsScheme(scheme);
            return;
        }

        var (file, id) = LinuxSchemeFile(scheme);
        if (!File.Exists(file))
            return;
        if (!File.ReadAllText(file).Contains("X-Kumiko-Command=" + SchemeCommand(false) + "\n", StringComparison.Ordinal))
            throw new InvalidOperationException("The URL scheme belongs to another executable.");
        File.Delete(file);
        // Remove only this application's IDs, preserving other associations.
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        foreach (var path in new[]
        {
            Path.Combine(config, "mimeapps.list"),
            Path.Combine(Path.GetDirectoryName(file)!, "mimeapps.list")
        }

        )
        {
            if (!File.Exists(path))
                continue;
            var lines = File.ReadAllLines(path).Select(line =>
            {
                if (!line.StartsWith("x-scheme-handler/" + scheme + "=", StringComparison.Ordinal))
                    return line;
                var values = line[(line.IndexOf('=') + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries).Where(v => v != id);
                return "x-scheme-handler/" + scheme + "=" + string.Join(';', values) + ";";
            }).ToArray();
            File.WriteAllLines(path, lines);
        }
    }

    void CheckSchemeAccess()
    {
        CheckThread();
        if (Options.Hidden)
            throw new InvalidOperationException("Hidden apps cannot read or change user URL associations.");
        if (OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Declare macOS URL schemes in the bundle manifest; see PACKAGING.md.");
    }

    static (string File, string Id) LinuxSchemeFile(string scheme)
    {
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var id = "kumiko-" + scheme + ".desktop";
        return (Path.Combine(data, "applications", id), id);
    }

    static string XdgMime(params string[] arguments)
    {
        var start = new ProcessStartInfo("xdg-mime")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start xdg-mime.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(5000))
        {
            process.Kill();
            throw new IOException("xdg-mime did not finish within five seconds.");
        }

        if (process.ExitCode != 0)
            throw new IOException("xdg-mime failed: " + error);
        return output;
    }

    [SupportedOSPlatform("windows")]
    void RegisterWindowsScheme(string scheme)
    {
        using var inherited = Registry.ClassesRoot.OpenSubKey(scheme);
        using var existing = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + scheme);
        if (existing is null && inherited is not null)
            throw new InvalidOperationException("This URL scheme is already registered system-wide.");
        if (existing is not null && !Equals(existing.GetValue("KumikoCommand"), SchemeCommand(true)))
            throw new InvalidOperationException("This URL scheme already belongs to another executable.");
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme);
        key.SetValue("", "URL:" + scheme);
        key.SetValue("URL Protocol", "");
        key.SetValue("KumikoCommand", SchemeCommand(true));
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", SchemeCommand(true));
    }

    [SupportedOSPlatform("windows")]
    bool WindowsSchemeRegistered(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + scheme + @"\shell\open\command");
        return Equals(key?.GetValue(""), SchemeCommand(true));
    }

    [SupportedOSPlatform("windows")]
    void UnregisterWindowsScheme(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + scheme);
        if (key is null)
            return;
        if (!Equals(key.GetValue("KumikoCommand"), SchemeCommand(true)))
            throw new InvalidOperationException("The URL scheme belongs to another executable.");
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + scheme, false);
    }
}
