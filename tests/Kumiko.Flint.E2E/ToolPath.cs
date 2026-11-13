using System.Diagnostics;
using Flint.Services;

/// <summary>
/// A Flint opened from Finder or the Dock has launchd's PATH, so tools come from the login shell's PATH, which children
/// also receive. Fake tools and fake login shells stand in for the user's; their shell and profile are never run.
/// </summary>
static class ToolPath
{
    public static void Run(Repositories fixture, Action<bool, string> check)
    {
        if (OperatingSystem.IsWindows())
            return;
        var bin = Path.Combine(fixture.Root, "tool-bin");
        Directory.CreateDirectory(bin);
        Script(Path.Combine(bin, "flint-probe"), "printf '%s' \"$PATH\"");
        var childPath = bin + ":/usr/bin:/bin";
        var runner = new ProcessRunner(new Dictionary<string, string> { ["PATH"] = childPath });
        var output = Attempt(() => runner.Run("flint-probe", fixture.Root, [], default).GetAwaiter().GetResult());
        check(output == childPath, $"A tool is found on the PATH its child receives, not only Flint's own ({output})");
        var missing = Attempt(() => runner.Run("flint-missing-tool", fixture.Root, [], default).GetAwaiter().GetResult());
        check(missing.StartsWith("IOException", StringComparison.Ordinal) && missing.Contains("flint-missing-tool", StringComparison.Ordinal),
            $"A missing tool is reported by name ({missing})");

        var shell = Path.Combine(bin, "login-shell");
        Script(shell, "echo 'Welcome /banner/bin'\nPATH=/login/bin:/usr/bin exec /bin/sh -c \"$3\"");
        var login = Attempt(() => SearchPath.Login(shell, "/usr/bin:/bin:/inherited/bin", TimeSpan.FromSeconds(10)));
        check(login == "/login/bin:/usr/bin:/bin:/inherited/bin",
            $"The login shell's PATH comes first, banners are ignored and inherited entries follow ({login})");

        var hanging = Path.Combine(bin, "hanging-shell");
        Script(hanging, "sleep 30");
        var clock = Stopwatch.StartNew();
        var slow = Attempt(() => SearchPath.Login(hanging, "/inherited/bin", TimeSpan.FromMilliseconds(300)));
        check(slow == "/inherited/bin" && clock.Elapsed < TimeSpan.FromSeconds(5),
            $"A hanging login shell keeps the inherited PATH after its timeout ({slow}, {clock.ElapsedMilliseconds} ms)");
        var broken = Attempt(() => SearchPath.Login(Path.Combine(bin, "no-such-shell"), "/inherited/bin", TimeSpan.FromSeconds(10)));
        check(broken == "/inherited/bin", $"A missing login shell keeps the inherited PATH ({broken})");
    }
    static string Attempt(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception error)
        {
            return error.GetType().Name + ": " + error.Message;
        }
    }
    static void Script(string path, string body)
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
