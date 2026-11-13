namespace Kumiko.Audio;

/// <summary>Private injectable AudioQueue boundary. Dispose quiesces native callbacks before releasing context.</summary>
internal interface IMacAudioQueue : IDisposable
{
    bool IsRunning
    {
        get;
    }
    void Open(AudioFormat format, Action returned, Action runningChanged);
    void Submit(ReadOnlySpan<float> samples);
    void Start();
    void Stop(bool immediate);
    void Reset();
}

internal sealed class MacAudioOutput(IMacAudioQueue? boundary = null) : IAudioOutput
{
    readonly IMacAudioQueue queue = boundary ?? new NativeMacAudioQueue();
    TaskCompletionSource? consumed, drained;
    bool started;
    int disposed;
    public void Open(AudioFormat format) => queue.Open(format, Returned, RunningChanged);
    void Returned()
    {
        if (Volatile.Read(ref disposed) == 0)
            Volatile.Read(ref consumed)?.TrySetResult();
    }
    void RunningChanged()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;
        try
        {
            if (!queue.IsRunning)
                Volatile.Read(ref drained)?.TrySetResult();
        }
        catch (Exception error)
        {
            Volatile.Read(ref drained)?.TrySetException(error);
        }
    }
    public ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref consumed, reply);
        queue.Submit(samples.Span);
        if (!started)
        {
            queue.Start();
            started = true;
        }
        return new(Wait(reply, token));
    }
    async Task Wait(TaskCompletionSource reply, CancellationToken token)
    {
        using var registration = token.Register(() =>
        {
            try
            {
                queue.Stop(true);
                reply.TrySetCanceled(token);
            }
            catch (Exception error)
            {
                reply.TrySetException(error);
            }
        });
        await reply.Task.ConfigureAwait(false);
        // A stop can trigger a reuse/property callback before its cancellation reply.
        token.ThrowIfCancellationRequested();
    }
    public async ValueTask DrainAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!started)
            return;
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref drained, reply);
        try
        {
            queue.Stop(false);
            RunningChanged();
            await Wait(reply, token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref drained, null);
            started = false;
        }
    }
    public void Reset()
    {
        queue.Stop(true);
        queue.Reset();
        started = false;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            queue.Dispose();
    }
}
