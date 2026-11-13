using Kumiko.Audio;

static class QueueDrainFlows
{
    internal static async Task Run(Action<bool, string> check)
    {
        var queue = new Queue();
        var output = new MacAudioOutput(queue);
        var player = new AudioPlayer(new OneChunk(), output);
        await player.PlayAsync();
        await queue.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        queue.Returned!();
        await queue.Draining.Task.WaitAsync(TimeSpan.FromSeconds(3));
        check(!player.Completion.IsCompleted && !queue.Trace.Contains("stop:True"),
            "AudioQueue buffer reuse cannot complete EOF or immediately stop pending audio");
        queue.Finish();
        await player.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        await player.DisposeAsync();
        check(queue.Trace.IndexOf("stop:False") < queue.Trace.IndexOf("running:false") &&
            queue.Trace.IndexOf("running:false") < queue.Trace.IndexOf("dispose"),
            "AudioQueue EOF waits for running-property stop notification before resource disposal");
        queue.Returned!();
        queue.RunningChanged!();
        check(queue.ReadsAfterDispose == 0, "late private queue callbacks do not inspect disposed native resources");
        var cancelledQueue = new Queue();
        await using var cancelledPlayer = new AudioPlayer(new OneChunk(), new MacAudioOutput(cancelledQueue));
        await cancelledPlayer.PlayAsync();
        await cancelledQueue.Submitted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancelledQueue.Returned!();
        await cancelledQueue.Draining.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cancelledPlayer.PauseAsync().WaitAsync(TimeSpan.FromSeconds(3));
        check(cancelledPlayer.State.Phase == PlaybackPhase.Paused && !cancelledPlayer.Completion.IsCompleted && cancelledQueue.Trace.Contains("stop:True"),
            "pause cancels asynchronous native drain without reporting a false EOF");
    }
    sealed class OneChunk : IAudioDecoder
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
            samples.Fill(.1f);
            PositionFrames = samples.Length;
            return samples.Length;
        }
        public void Seek(long frame) => PositionFrames = frame;
        public void Dispose()
        {
        }
    }
    sealed class Queue : IMacAudioQueue
    {
        internal readonly List<string> Trace = [];
        internal readonly TaskCompletionSource Submitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Draining = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Action? Returned, RunningChanged;
        internal int ReadsAfterDispose;
        bool running, disposed;
        public bool IsRunning
        {
            get
            {
                if (disposed)
                    Interlocked.Increment(ref ReadsAfterDispose);
                return running;
            }
        }
        public void Open(AudioFormat format, Action returned, Action runningChanged)
        {
            Returned = returned;
            RunningChanged = runningChanged;
        }
        public void Submit(ReadOnlySpan<float> samples) => Submitted.TrySetResult();
        public void Start() => running = true;
        public void Stop(bool immediate)
        {
            Trace.Add("stop:" + immediate);
            if (immediate)
            {
                running = false;
                RunningChanged!();
            }
            else
                Draining.TrySetResult();
        }
        internal void Finish()
        {
            running = false;
            Trace.Add("running:false");
            RunningChanged!();
        }
        public void Reset() => Trace.Add("reset");
        public void Dispose()
        {
            disposed = true;
            Trace.Add("dispose");
        }
    }
}
