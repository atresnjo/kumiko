using Kumiko.Audio;

static class ControlFlows
{
    internal static async Task Run(Action<bool, string> check)
    {
        foreach (bool holdRead in new[] { true, false })
        {
            foreach (string control in new[] { "pause", "seek", "stop", "cancel" })
            {
                var decoder = new Decoder(holdRead);
                var output = new Output(!holdRead);
                await using var player = new AudioPlayer(decoder, output);
                await player.PlayAsync();
                await (holdRead ? decoder.Started.Task : output.Started.Task).WaitAsync(TimeSpan.FromSeconds(3));
                if (control == "cancel")
                {
                    decoder.DelayCancellation = holdRead;
                    output.DelayCancellation = !holdRead;
                    using var token = new CancellationTokenSource();
                    var pause = player.PauseAsync(token.Token);
                    token.Cancel();
                    bool cancelled = false;
                    try
                    {
                        await pause.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled = true;
                    }
                    finally
                    {
                        decoder.Unwind.TrySetResult();
                        output.Unwind.TrySetResult();
                    }
                    check(cancelled, "cancelled control completes promptly during held " + (holdRead ? "read" : "write"));
                    await player.SetLoopAsync(false).WaitAsync(TimeSpan.FromSeconds(2));
                    check(player.State.Phase == PlaybackPhase.Playing, "cancelled pause never executes later during " + (holdRead ? "read" : "write"));
                }
                else
                {
                    await (control switch
                    {
                        "pause" => player.PauseAsync(),
                        "seek" => player.SeekAsync(TimeSpan.FromMilliseconds(2)),
                        _ => player.StopAsync()
                    }).WaitAsync(TimeSpan.FromSeconds(2));
                    check((holdRead ? decoder.Cancelled : output.Cancelled) > 0 && !decoder.Disposed && !output.Disposed,
                        control + " interrupts held " + (holdRead ? "read" : "write") + " without destroying the player");
                    check(player.State.Phase == (control == "pause" ? PlaybackPhase.Paused : control == "stop" ? PlaybackPhase.Stopped : PlaybackPhase.Playing),
                        control + " publishes its state after interrupting " + (holdRead ? "read" : "write"));
                    if (!holdRead && control == "pause")
                    {
                        output.Hold = false;
                        decoder.HoldAfterFirst = true;
                        await player.SetVolumeAsync(.5f);
                        await player.PlayAsync();
                        await output.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                        check(output.LastSamples.All(s => Math.Abs(s - .25f) < .001f) && decoder.Reads == 1,
                            "pause resume retries unsubmitted frames once with current gain and without reading ahead");
                    }
                }
            }
        }
    }
    sealed class Decoder(bool hold) : IAudioDecoder
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Unwind = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool DelayCancellation, HoldAfterFirst, Disposed;
        internal int Reads, Cancelled;
        public AudioFormat Format => new(8000, 1);
        public long? LengthFrames => null;
        public long PositionFrames
        {
            get; private set;
        }
        public bool CanSeek => true;
        public int Read(Span<float> samples) => throw new InvalidOperationException("Use cancellable read.");
        public async ValueTask<int> ReadAsync(Memory<float> samples, CancellationToken token)
        {
            Started.TrySetResult();
            if (hold || HoldAfterFirst && Reads > 0)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref Cancelled);
                    if (DelayCancellation)
                        await Unwind.Task;
                    throw;
                }
            }
            Reads++;
            samples.Span.Fill(.5f);
            PositionFrames += samples.Length;
            return samples.Length;
        }
        public void Seek(long frame) => PositionFrames = frame;
        public void Dispose() => Disposed = true;
    }
    sealed class Output(bool hold) : IAudioOutput
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Submitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Unwind = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Hold = hold, DelayCancellation, Disposed;
        internal int Cancelled;
        internal float[] LastSamples = [];
        public void Open(AudioFormat format)
        {
        }
        public async ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token)
        {
            Started.TrySetResult();
            if (Hold)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref Cancelled);
                    if (DelayCancellation)
                        await Unwind.Task;
                    throw;
                }
            }
            LastSamples = samples.ToArray();
            Submitted.TrySetResult();
        }
        public void Reset()
        {
        }
        public void Dispose() => Disposed = true;
    }
}
