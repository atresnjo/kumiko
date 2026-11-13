using System.Runtime.InteropServices;
using Kumiko.Audio;

static class LinuxDrainFlows
{
    // Resolver and library have process lifetime: cached P/Invokes must never outlive the fixture.
    static nint fixture;
    internal static unsafe void Init(int result)
    {
        if (fixture == 0)
        {
            string file = OperatingSystem.IsMacOS() ? "libkumiko-alsa-fixture.dylib" : "libkumiko-alsa-fixture.so";
            fixture = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, file));
            NativeLibrary.SetDllImportResolver(typeof(AudioPlayer).Assembly, (name, _, _) => name == "libasound.so.2" ? fixture : 0);
        }
        ((delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(fixture, "fixture_init"))(result);
    }
    internal static unsafe void HoldWrite(int result) => ((delegate* unmanaged[Cdecl]<int, void>)NativeLibrary.GetExport(fixture, "fixture_hold_write"))(result);
    internal static unsafe void WaitWrite() => ((delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(fixture, "fixture_wait_write"))();
    internal static unsafe void WaitDrop() => ((delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(fixture, "fixture_wait_drop"))();
    internal static unsafe void ReleaseWrite() => ((delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(fixture, "fixture_release_write"))();
    internal static unsafe int EarlyReuse() => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(fixture, "fixture_early_reuse"))();
    internal static unsafe int ValidSamples() => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(fixture, "fixture_valid_samples"))();
    static unsafe void Wait() => ((delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(fixture, "fixture_wait_drain"))();
    static unsafe int Prepared() => ((delegate* unmanaged[Cdecl]<int>)NativeLibrary.GetExport(fixture, "fixture_prepared"))();
    internal static async Task Run(int nativeResult, Action<bool> require, List<string> trace)
    {
        Init(nativeResult);
        using (var output = new LinuxAudioOutput())
        using (var token = new CancellationTokenSource())
        {
            output.Open(new(8000, 1));
            var draining = Task.Run(async () => await output.DrainAsync(token.Token));
            await Task.Run(Wait).WaitAsync(TimeSpan.FromSeconds(3));
            token.Cancel();
            bool cancelled = false;
            try
            {
                await draining.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            trace.Add("cancelled:" + cancelled + ";prepared:" + Prepared());
            require(cancelled && Prepared() == 0);
        }
        Init(nativeResult);
        await using var player = new AudioPlayer(new DrainReviewFlows.Decoder(), new LinuxAudioOutput());
        await player.PlayAsync();
        await Task.Run(Wait).WaitAsync(TimeSpan.FromSeconds(3));
        await player.SetVolumeAsync(.5f).WaitAsync(TimeSpan.FromSeconds(3));
        require(!player.Completion.IsCompleted && player.State.Phase == PlaybackPhase.Playing);
        await player.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        trace.Add("phase:" + player.State.Phase + ";completion:" + player.Completion.Status);
        require(player.State.Phase == PlaybackPhase.Paused && !player.Completion.IsCompleted);
        await player.SeekAsync(TimeSpan.Zero);
    }
}
