using System.Diagnostics.CodeAnalysis;
using Kumiko.Audio;

static class LifetimeFlows
{
    // Keep the native factory reachable for AOT compilation without opening an output device.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(AudioOutput))]
    internal static async Task Run(Action<bool, string> check)
    {
        var live = new HeldDecoder();
        var liveOutput = new HeldOutput();
        var livePlayer = new AudioPlayer(live, liveOutput);
        await livePlayer.PlayAsync();
        await live.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await livePlayer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        check(live.Cancelled && live.Disposed && liveOutput.Disposed,
            "disposal cancels a live decoder read before releasing owned resources");
        var decoder = new OwnedDecoder();
        var sink = new HeldOutput();
        var player = new AudioPlayer(decoder, sink);
        await player.PlayAsync();
        await sink.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await player.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        check(sink.Cancelled && sink.Disposed && decoder.Disposed, "disposal cancels held output and joins before releasing decoder/device");
        try
        {
            await player.PlayAsync();
            check(false, "disposed player rejects commands");
        }
        catch (ObjectDisposedException)
        {
            check(true, "disposed player rejects commands");
        }
        var failedSink = new HeldOutput { Fail = true };
        await using var failed = new AudioPlayer(new OwnedDecoder(), failedSink);
        await failed.PlayAsync();
        try
        {
            await failed.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            check(false, "device failure is observable");
        }
        catch (IOException)
        {
            check(failed.State.Phase == PlaybackPhase.Failed && failed.State.Error is not null, "device removal publishes failure and completes with its error");
        }
    }
    class OwnedDecoder : IAudioDecoder
    {
        public AudioFormat Format => new(8000, 1);
        public long? LengthFrames => null;
        public long PositionFrames
        {
            get; private set;
        }
        public bool CanSeek => false;
        internal bool Disposed;
        public int Read(Span<float> samples)
        {
            samples.Fill(.1f);
            PositionFrames += samples.Length;
            return samples.Length;
        }
        public void Seek(long frame) => throw new NotSupportedException();
        public virtual void Dispose() => Disposed = true;
    }
    sealed class HeldDecoder : OwnedDecoder, IAudioDecoder
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled;
        public async ValueTask<int> ReadAsync(Memory<float> samples, CancellationToken token)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
    }
    sealed class HeldOutput : IAudioOutput
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Cancelled, Disposed, Fail;
        public void Open(AudioFormat format)
        {
        }
        public async ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token)
        {
            Started.TrySetResult();
            if (Fail)
                throw new IOException("Injected device removed");
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }
        public void Reset()
        {
        }
        public void Dispose() => Disposed = true;
    }
}
