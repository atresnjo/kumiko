using Kumiko.Desktop;
using Launcher;

static class AsyncFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        var old = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var ui = new Session(new()
        {
            Load = _ => old.Task
        }))
        {
            check(ui.Model.Loading && ui.Tester.ContainsText("Finding your applications"), "pending catalog shows a loading state while the panel is usable");
            ui.Scene(output, "launcher-loading");
            ui.Desktop.Load = _ => Task.FromResult<IReadOnlyList<InstalledApplication>>(FixtureLauncherDesktop.Applications);
            ui.Program.Send(new LauncherMsg.Refresh());
            ui.Pump();
            check(!ui.Model.Loading && ui.Model.Entries.Length == 6, "explicit refresh can finish before an older catalog request");
            var delivered = ui.Deliveries;
            old.SetResult([]);
            ui.Wait(() => ui.Deliveries > delivered);
            check(ui.Model.Entries.Length == 6, "late superseded catalog result cannot erase newer results");
        }
        var pending = new TaskCompletionSource<IReadOnlyList<InstalledApplication>>();
        var disposed = new Session(new()
        {
            Load = _ => pending.Task
        });
        var callback = disposed.Desktop.Shortcuts.Values.Single();
        // A synchronous completion queues a main-thread result, then disposal retires that queue entry.
        pending.SetResult(FixtureLauncherDesktop.Applications);
        disposed.Host.Dispose();
        disposed.DrainCallbacks();
        var shows = disposed.Desktop.Shows;
        callback();
        disposed.DrainCallbacks();
        check(disposed.Model.Entries.Length == 0 && disposed.Desktop.Shows == shows, "queued catalog reply and captured shortcut callback are ignored after host disposal");
        check(disposed.Desktop.Shortcuts.Count == 0, "host disposal releases its shortcut registration");
        disposed.Dispose();
    }
}
