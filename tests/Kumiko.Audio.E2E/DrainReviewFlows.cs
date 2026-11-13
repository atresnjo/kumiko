using System.Text.Json;
using Kumiko.Audio;

static class DrainReviewFlows
{
    internal static async Task Run(string output, Action<bool, string> check)
    {
        var results = new List<(string Name, string? Error, List<string> Trace)>();
        async Task Case(string name, Func<List<string>, Task> body)
        {
            var trace = new List<string>();
            string? error = null;
            try
            {
                await body(trace);
            }
            catch (Exception failure)
            {
                error = failure.GetType().Name + ": " + failure.Message;
            }
            results.Add((name, error, trace));
        }
        static void Require(bool value)
        {
            if (!value)
                throw new InvalidOperationException("Drain behavior assertion failed.");
        }
        foreach (string control in new[] { "volume", "loop-false", "play-noop" })
        {
            await Case("R2-S1 " + control + " preserves pending drain and stays responsive", async trace =>
            {
                var queue = new Queue(trace);
                await using var player = new AudioPlayer(new Decoder(), new MacAudioOutput(queue));
                await player.PlayAsync();
                await queue.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                queue.Returned!();
                await queue.Draining.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await (control switch
                {
                    "volume" => player.SetVolumeAsync(.5f),
                    "loop-false" => player.SetLoopAsync(false),
                    _ => player.PlayAsync()
                }).WaitAsync(TimeSpan.FromSeconds(3));
                Require(player.State.Phase == PlaybackPhase.Playing && !player.Completion.IsCompleted && !trace.Contains("stop:True"));
                queue.Finish();
                await player.Completion.WaitAsync(TimeSpan.FromSeconds(3));
                Require(player.State.Phase == PlaybackPhase.Ended && trace.Contains("natural-finish"));
            });
        }
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            foreach (int result in new[] { 0, -77 })
                await Case("R2-S2 cancelled native ALSA result " + result + " stays cancellation before error/preparation/EOF",
                    trace => LinuxDrainFlows.Run(result, Require, trace));
        await Case("R2-S2 player rejects normal drain return after transport cancellation", async trace =>
        {
            var outputBoundary = new InterruptedDrain();
            await using var player = new AudioPlayer(new Decoder(), outputBoundary);
            player.Changed += state => trace.Add(state.Phase.ToString());
            await player.PlayAsync();
            await outputBoundary.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await player.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Require(!player.Completion.IsCompleted && player.State.Phase == PlaybackPhase.Paused && !trace.Contains("Ended"));
        });
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r2-drain.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "silent private AudioQueue fixture and actual LinuxAudioOutput with substituted C calls; no native devices");
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
    internal sealed class Decoder : IAudioDecoder
    {
        public AudioFormat Format => new(8000, 1);
        public long? LengthFrames => 80;
        public long PositionFrames
        {
            get; private set;
        }
        public bool CanSeek => true;
        public int Read(Span<float> samples)
        {
            if (PositionFrames > 0)
                return 0;
            samples.Fill(.5f);
            PositionFrames = samples.Length;
            return samples.Length;
        }
        public void Seek(long frame) => PositionFrames = frame;
        public void Dispose()
        {
        }
    }
    sealed class InterruptedDrain : IAudioOutput
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Open(AudioFormat format)
        {
        }
        public ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token) => ValueTask.CompletedTask;
        public async ValueTask DrainAsync(CancellationToken token)
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => finished.TrySetResult());
            Started.TrySetResult();
            await finished.Task;
        }
        public void Reset()
        {
        }
        public void Dispose()
        {
        }
    }
    sealed class Queue(List<string> trace) : IMacAudioQueue
    {
        internal readonly TaskCompletionSource Submitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Draining = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? Returned, RunningChanged;
        public bool IsRunning
        {
            get; private set;
        }
        public void Open(AudioFormat format, Action returned, Action runningChanged)
        {
            Returned = returned;
            RunningChanged = runningChanged;
        }
        public void Submit(ReadOnlySpan<float> samples) => Submitted.TrySetResult();
        public void Start() => IsRunning = true;
        public void Stop(bool immediate)
        {
            trace.Add("stop:" + immediate);
            if (immediate)
            {
                IsRunning = false;
                RunningChanged!();
            }
            else
                Draining.TrySetResult();
        }
        internal void Finish()
        {
            IsRunning = false;
            trace.Add("natural-finish");
            RunningChanged!();
        }
        public void Reset() => trace.Add("reset");
        public void Dispose() => trace.Add("dispose");
    }
}
