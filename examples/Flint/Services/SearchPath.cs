using System.Diagnostics;

namespace Flint.Services;

/// <summary>
/// Apps opened from Finder or the Dock inherit launchd's PATH, which lacks Homebrew and user tool directories (gh, and
/// node for Git hooks). The login shell's PATH comes first and inherited entries follow.
/// </summary>
public static class SearchPath
{
    const string Marker = "__flint_path__";
    static readonly Lazy<Task<string>> user = new(() => Task.Run(() => Login(Shell(), Inherited, TimeSpan.FromSeconds(5))));
    static string Inherited => Environment.GetEnvironmentVariable("PATH") ?? "";
    /// <summary>The user's login-shell PATH, queried once off the UI thread. Windows processes already inherit the user PATH.</summary>
    public static Task<string> User() => OperatingSystem.IsWindows() ? Task.FromResult(Inherited) : user.Value;
    static string Shell() => Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell
        : OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/sh";
    /// <summary>Runs printenv rather than expanding $PATH so fish and other shells that store PATH as a list report it joined.</summary>
    public static string Login(string shell, string inherited, TimeSpan timeout)
    {
        var start = new ProcessStartInfo(shell)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("-l");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add($"echo {Marker}; /usr/bin/printenv PATH");
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return inherited;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            // A daemon started by the profile can hold the pipe open after the shell exits.
            if (!process.WaitForExit(timeout) || !output.Wait(timeout))
            {
                Kill(process);
                return inherited;
            }
            if (process.ExitCode != 0)
                return inherited;
            var text = output.Result;
            var at = text.LastIndexOf(Marker, StringComparison.Ordinal);
            if (at < 0)
                return inherited;
            var line = text[(at + Marker.Length)..].TrimStart('\r', '\n').Split('\n')[0].Trim();
            return line.Length == 0 ? inherited : Merge(line, inherited);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return inherited;
        }
    }
    static string Merge(string first, string then)
    {
        var entries = new List<string>();
        foreach (var entry in (first + Path.PathSeparator + then).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            if (!entries.Contains(entry, StringComparer.Ordinal))
                entries.Add(entry);
        return string.Join(Path.PathSeparator, entries);
    }
    static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
    /// <summary>The file a child with this PATH would run. Process.Start alone searches Flint's own PATH.</summary>
    public static string? Resolve(string executable, string path)
    {
        if (OperatingSystem.IsWindows())
            return executable;
        if (executable.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return executable;
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate) && (File.GetUnixFileMode(candidate) & execute) != 0)
                return candidate;
        }
        return null;
    }
}
