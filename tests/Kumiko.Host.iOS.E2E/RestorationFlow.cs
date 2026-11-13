using Foundation;
using Kumiko.iOS;
using Kumiko.Runtime;
using UIKit;
using static Probe;

/// <summary>Scene state is written once per background transition and restored before the first frame.</summary>
internal static class RestorationFlow
{
    internal static void Run(string output, List<string> checks)
    {
        var directory = Path.Combine(NSFileManager.DefaultManager.GetTemporaryDirectory().Path!, "kumiko-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var state = Path.Combine(directory, "scene.json");
            SaveAndRestore(output, state);
            Rejected(state, "{", "A truncated state file is ignored and removed");
            Rejected(state, """{"Count":"three"}""", "A state file from an incompatible schema is ignored and removed");
            Rejected(state, """{"Count":2,"Extra":1}""", "A state file that does not round-trip exactly is ignored and removed");
            Disabled(directory);
            SaveFailure(directory);
            Discard(directory);
            checks.Add("state-restoration");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    static NativeViewController Host(string? state, out Func<Program<HostModel, HostMessage>> program)
    {
        Program<HostModel, HostMessage>? created = null;
        program = () => created ?? throw new InvalidOperationException("The program was not created.");
        return new(() => created = HostApp.CreateProgram(), new NativeHostOptions { StatePath = state });
    }

    static void SaveAndRestore(string output, string state)
    {
        var first = Host(state, out var program);
        using (var window = Window(first))
        {
            Settle(first);
            for (var i = 0; i < 3; i++)
                program().Send(new HostMessage.Increment());
            program().Send(new HostMessage.Open(HostRoute.Details));
            Settle(first);
            Require(!File.Exists(state), "Messages alone never write scene state");
            Post(UIApplication.DidEnterBackgroundNotification);
            Require(File.Exists(state), "Entering the background writes scene state");
            var saved = File.ReadAllBytes(state);
            program().Send(new HostMessage.Increment());
            Settle(first);
            Require(saved.AsSpan().SequenceEqual(File.ReadAllBytes(state)), "Later messages do not rewrite state until the next transition");
            Detach(window, first);
        }
        first.Dispose();
        var second = Host(state, out var restored);
        Require(second.View is not null, "Loading the restored controller's view creates its program");
        Require(restored().Model.Count == 3 && restored().Model.Navigation.Current.Route == HostRoute.Details,
            "The saved model and current destination are restored before the first frame");
        Require(restored().PendingCommands == 0, "Restoring runs no commands");
        using (var window = Window(second))
        {
            Settle(second);
            Require(Text(second.Content!).Contains("Count 3", StringComparison.Ordinal), "The first frame shows the restored destination");
            Capture(second.View!, Path.Combine(output, "host-restored.png"));
            Detach(window, second);
        }
        second.Dispose();
    }

    static void Rejected(string state, string contents, string description)
    {
        File.WriteAllText(state, contents);
        var controller = Host(state, out var program);
        using (var window = Window(controller))
        {
            Settle(controller);
            Require(program().Model.Count == 0 && program().Model.Navigation.Current.Route == HostRoute.Home && !File.Exists(state), description);
            Detach(window, controller);
        }
        controller.Dispose();
    }

    static void Disabled(string directory)
    {
        var before = Directory.GetFileSystemEntries(directory).Length;
        var controller = Host(null, out var program);
        using (var window = Window(controller))
        {
            Settle(controller);
            program().Send(new HostMessage.Increment());
            Post(UIApplication.DidEnterBackgroundNotification);
            Require(Directory.GetFileSystemEntries(directory).Length == before, "Without a state path nothing is written");
            Detach(window, controller);
        }
        controller.Dispose();
    }

    static void SaveFailure(string directory)
    {
        var blocker = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocker, "file");
        Exception? reported = null;
        var controller = Host(Path.Combine(blocker, "scene.json"), out var program);
        controller.UnhandledException += error => reported = error;
        using (var window = Window(controller))
        {
            Settle(controller);
            Post(UIApplication.DidEnterBackgroundNotification);
            Require(reported is IOException or UnauthorizedAccessException, "A failed save is reported instead of crashing");
            Tap(controller.Content!, "increment");
            PumpUntil(() => program().Model.Count == 1, "The app keeps running after a failed save");
            Detach(window, controller);
        }
        controller.Dispose();
    }

    static void Discard(string directory)
    {
        var kept = NativeApplication.StatePath("kept-scene", directory);
        var discarded = NativeApplication.StatePath("discarded/../scene", directory);
        Require(Path.GetDirectoryName(discarded) == directory && kept != discarded, "Scene identifiers map to distinct files inside the state directory");
        File.WriteAllText(kept, "{}");
        File.WriteAllText(discarded, "{}");
        NativeApplication.DiscardState(["discarded/../scene"], directory);
        Require(File.Exists(kept) && !File.Exists(discarded), "Discarding a scene session removes only its state");
    }
}
