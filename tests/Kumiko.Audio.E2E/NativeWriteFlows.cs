using System.Text.Json;
using Kumiko.Audio;

static class NativeWriteFlows
{
    internal static async Task Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;
        var results = new List<(string Name, string? Error, List<string> Trace)>();
        foreach (int nativeResult in new[] { -77, 0, 1 })
            foreach (string control in new[] { "pause", "seek", "stop", "dispose" })
            {
                var trace = new List<string>();
                string? error = null;
                LinuxDrainFlows.Init(0);
                LinuxDrainFlows.HoldWrite(nativeResult);
                var player = new AudioPlayer(new DrainReviewFlows.Decoder(), new LinuxAudioOutput());
                try
                {
                    await player.PlayAsync();
                    await Task.Run(LinuxDrainFlows.WaitWrite).WaitAsync(TimeSpan.FromSeconds(3));
                    var commands = Task.WhenAll(player.SetVolumeAsync(.25f), player.SetLoopAsync(false), player.PlayAsync());
                    await commands.WaitAsync(TimeSpan.FromSeconds(2));
                    trace.Add("state-controls-complete;volume:" + player.State.Volume);
                    Require(player.State.Volume == .25f && player.State.Phase == PlaybackPhase.Playing && !player.Completion.IsCompleted);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    var transport = control switch
                    {
                        "pause" => player.PauseAsync(),
                        "seek" => player.SeekAsync(TimeSpan.FromMilliseconds(10)),
                        "stop" => player.StopAsync(),
                        _ => player.DisposeAsync().AsTask()
                    };
                    await Task.Run(LinuxDrainFlows.WaitDrop).WaitAsync(TimeSpan.FromSeconds(3));
                    trace.Add("drop-observed;control-complete:" + transport.IsCompleted + ";early-reuse:" + LinuxDrainFlows.EarlyReuse());
                    Require(!transport.IsCompleted && LinuxDrainFlows.EarlyReuse() == 0);
                    LinuxDrainFlows.ReleaseWrite();
                    await transport.WaitAsync(TimeSpan.FromSeconds(3));
                    trace.Add("joined;phase:" + player.State.Phase + ";valid-samples:" + LinuxDrainFlows.ValidSamples());
                    Require(LinuxDrainFlows.ValidSamples() == 1 && LinuxDrainFlows.EarlyReuse() == 0 && player.State.Phase != PlaybackPhase.Failed);
                    if (control == "pause")
                        Require(player.State.Phase == PlaybackPhase.Paused && !player.Completion.IsCompleted);
                }
                catch (Exception failure)
                {
                    error = failure.GetType().Name + ": " + failure.Message;
                }
                finally
                {
                    LinuxDrainFlows.ReleaseWrite();
                    await player.DisposeAsync();
                }
                results.Add(($"R3-S1 native write result {nativeResult} with {control} preserves state controls/pin/join", error, trace));
            }
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r3-write.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "actual LinuxAudioOutput, host C substitution with held write/unwind and sample inspection; not ALSA/device execution");
            json.WriteStartArray("cases");
            foreach (var result in results)
            {
                json.WriteStartObject();
                json.WriteString("name", result.Name);
                json.WriteString("error", result.Error);
                json.WriteStartArray("trace");
                foreach (string value in result.Trace)
                    json.WriteStringValue(value);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        foreach (var result in results)
            check(result.Error is null, result.Name + (result.Error is null ? "" : ": " + result.Error));
    }
    static void Require(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Native write ownership assertion failed.");
    }
}
