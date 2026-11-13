using System.Diagnostics;
using Kumiko;
using Launcher;

static class InstanceFlows
{
    public static bool Child(string[] args)
    {
        if (args.FirstOrDefault() != "--launcher-instance-child")
            return false;
        using var app = new App(new("Launcher child", false, true));
        if (app.RequestSingleInstanceLock(args[1]))
            throw new InvalidOperationException("A second instance unexpectedly acquired the primary lock.");
        return true;
    }

    public static void Run(Action<bool, string> check)
    {
        using var app = new App(new("Launcher instance evidence", false, true));
        var identity = "dev.fixture.launcher." + Guid.NewGuid().ToString("N");
        check(app.RequestSingleInstanceLock(identity), "hidden primary process owns a unique current-user instance channel");
        using var ui = new Session(new()
        {
            LoginLaunch = true
        });
        var forwarded = false;
        app.SecondInstance += message =>
        {
            forwarded = message.Arguments.Contains("--launcher-instance-child", StringComparer.Ordinal);
            ui.Host.Summon();
        };
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        start.ArgumentList.Add("--launcher-instance-child");
        start.ArgumentList.Add(identity);
        using var child = Process.Start(start)!;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while ((!child.HasExited || !forwarded) && DateTime.UtcNow < deadline)
            app.RunFor(TimeSpan.FromMilliseconds(15));
        if (!child.HasExited)
        {
            child.Kill();
            throw new TimeoutException("Second instance did not exit.");
        }
        ui.Pump();
        check(child.ExitCode == 0 && forwarded && ui.Desktop.Visible && ui.Desktop.Shows == 1, "real second process forwards arguments, exits and summons the existing fixture host exactly once");
    }
}
