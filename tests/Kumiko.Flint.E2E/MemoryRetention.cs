using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Flint;
using Kumiko.UI;

static class MemoryRetention
{
    public static void Run(Harness h, Repositories fixture, string output, IServiceProvider services)
    {
        File.WriteAllText(Path.Combine(fixture.Repo, "memory-a.txt"), new string('a', 600_000) + "\n");
        File.WriteAllText(Path.Combine(fixture.Repo, "memory-b.txt"), new string('b', 600_000) + "\n");
        h.Click("Local Changes");
        h.Press(Key.R, Modifiers.Primary);
        var before = GC.GetTotalMemory(true);
        var patches = new List<WeakReference>();
        for (var i = 0; i < 24; i++)
            patches.Add(Select(h, i % 2 == 0 ? "memory-a.txt" : "memory-b.txt"));
        h.Click("Unstaged: words.txt");
        h.Idle();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var after = GC.GetTotalMemory(true);
        var retained = patches.Count(p => p.IsAlive);
        using (var stream = File.Create(Path.Combine(output, "memory.json")))
        using (var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteNumber("selections", patches.Count);
            json.WriteNumber("retainedObsoletePatches", retained);
            json.WriteNumber("managedBytesBefore", before);
            json.WriteNumber("managedBytesAfter", after);
            json.WriteNumber("workingSetBytes", Process.GetCurrentProcess().WorkingSet64);
            json.WriteEndObject();
        }
        h.Shot("memory-final");
        h.Check(retained == 0, $"Obsolete real Git patches are collectible after leaving large diffs ({retained} retained)");
        h.Check(h.Tab.Patch.Contains("delta", StringComparison.Ordinal), "Current real Git patch survives history retirement");
        var session = Path.Combine(output, "memory-session.jsonl");
        h.Program.Save(session);
        using var replay = FlintApp.CreateProgram(FlintApp.Init, services);
        replay.Replay(session);
        using var tester = new Tester(replay, new(1440, 900));
        h.Check(replay.Model.Current?.Patch == h.Tab.Patch, "Current-only checkpoint exports and replays without retired patches");
        h.Program.Record(Path.Combine(output, "memory-recording.jsonl"));
        h.Program.Send(new Msg.Notify("Recording after history retirement"));
        h.Idle();
        h.Program.Save(session);
        replay.Replay(session);
        tester.Settle();
        tester.Screenshot(Path.Combine(output, "memory-replay.png"));
        h.Check(replay.Model.Toasts.Any(t => t.Text == "Recording after history retirement"), "Explicit recording retains and replays subsequent real updates");
        h.Check(h.Program.Back() && !h.Program.Model.Toasts.Any(t => t.Text == "Recording after history retirement"), "Time travel is available again after enabling recording");
        h.Check(h.Program.Forward(), "Recorded update remains reachable with Forward");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference Select(Harness h, string file)
    {
        h.Click("Unstaged: " + file);
        h.Check(h.Tab.Patch.Length >= 600_000, "Large diff loaded through real Git and UI selection");
        return new(h.Tab.Patch);
    }
}
