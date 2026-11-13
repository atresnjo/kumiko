using System.Threading.Channels;

namespace Kumiko.Audio;

/// <summary>Owned streaming player. Commands, decoding and output are serialized on one worker.
/// Paused players await a channel, never poll. DisposeAsync cancels output and joins before releasing resources.</summary>
public sealed class AudioPlayer : IAsyncDisposable
{
    sealed class Request(Action work, CancellationToken token, bool interrupt) : IDisposable
    {
        internal readonly bool Interrupt = interrupt;
        internal readonly Action Work = work;
        internal readonly CancellationToken Token = token;
        internal readonly TaskCompletionSource Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationTokenRegistration Registration;
        public void Dispose() => Registration.Dispose();
    }
    readonly object ioSync = new();
    CancellationTokenSource? activeIo;
    int buffered;
    readonly IAudioDecoder decoder;
    readonly IAudioOutput output;
    readonly Channel<Request> requests = Channel.CreateUnbounded<Request>(new() { SingleReader = true });
    readonly CancellationTokenSource lifetime = new();
    readonly Task worker;
    PlaybackState state;
    TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool opened;
    int disposing;
    public PlaybackState State => Volatile.Read(ref state);
    public Task Completion => Volatile.Read(ref completion).Task;
    /// <summary>Worker-thread notification. Dispatch immutable snapshots; consumer exceptions are isolated.</summary>
    public event Action<PlaybackState>? Changed;

    public AudioPlayer(IAudioDecoder decoder, IAudioOutput output)
    {
        decoder.Format.Validate();
        this.decoder = decoder;
        this.output = output;
        state = new(PlaybackPhase.Stopped, TimeSpan.Zero,
            decoder.LengthFrames is { } length ? TimeSpan.FromSeconds((double)length / decoder.Format.SampleRate) : null, 1, false);
        worker = Task.Run(Run);
    }
    void Publish(PlaybackState next)
    {
        Volatile.Write(ref state, next);
        if (Changed is not { } callbacks)
            return;
        foreach (Action<PlaybackState> callback in callbacks.GetInvocationList())
        {
            try
            {
                callback(next);
            }
            catch
            {
                // Never let a consumer callback tear down the decoder/output owner.
            }
        }
    }
    Task Command(Action action, CancellationToken token, bool interrupt = false)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposing) != 0, this);
        token.ThrowIfCancellationRequested();
        var request = new Request(action, token, interrupt);
        request.Registration = token.Register(() => request.Reply.TrySetCanceled(token));
        CancellationTokenSource? active;
        lock (ioSync)
        {
            if (!requests.Writer.TryWrite(request))
            {
                request.Dispose();
                throw new ObjectDisposedException(nameof(AudioPlayer));
            }
            active = interrupt ? activeIo : null;
        }
        if (active is not null)
            _ = Interrupt(active);
        return request.Reply.Task;
    }
    static async Task Interrupt(CancellationTokenSource operation)
    {
        try
        {
            await operation.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The serialized owner already completed this operation.
        }
        catch (AggregateException)
        {
            // Cancellation callbacks belong to injected boundaries; cancellation remains signalled.
        }
    }
    public Task PlayAsync(CancellationToken token = default) => Command(() =>
    {
        if (state.Phase == PlaybackPhase.Playing)
            return;
        if (state.Phase == PlaybackPhase.Failed)
            throw new InvalidOperationException("The output failed; create a new player to reopen the device.");
        if (!opened)
        {
            output.Open(decoder.Format);
            opened = true;
        }
        if (state.Phase == PlaybackPhase.Ended)
            decoder.Seek(0);
        if (completion.Task.IsCompleted)
            Volatile.Write(ref completion, new(TaskCreationOptions.RunContinuationsAsynchronously));
        Publish(state with
        {
            Phase = PlaybackPhase.Playing,
            Error = null
        });
    }, token);
    public Task PauseAsync(CancellationToken token = default) => Command(() =>
    {
        if (opened)
            output.Reset();
        Publish(state with
        {
            Phase = PlaybackPhase.Paused
        });
    }, token, interrupt: true);
    public Task StopAsync(CancellationToken token = default) => Command(() =>
    {
        if (!decoder.CanSeek)
            throw new NotSupportedException("This live decoder cannot restart.");
        if (opened)
            output.Reset();
        decoder.Seek(0);
        buffered = 0;
        Publish(state with
        {
            Phase = PlaybackPhase.Stopped,
            Position = TimeSpan.Zero
        });
        completion.TrySetResult();
    }, token, interrupt: true);
    public Task SeekAsync(TimeSpan position, CancellationToken token = default) => Command(() =>
    {
        if (!decoder.CanSeek)
            throw new NotSupportedException("This decoder cannot seek.");
        if (position < TimeSpan.Zero || state.Duration is { } duration && position > duration)
            throw new ArgumentOutOfRangeException(nameof(position));
        if (opened)
            output.Reset();
        decoder.Seek(checked((long)(position.TotalSeconds * decoder.Format.SampleRate)));
        buffered = 0;
        Publish(state with
        {
            Position = position,
            Phase = state.Phase == PlaybackPhase.Ended ? PlaybackPhase.Paused : state.Phase
        });
    }, token, interrupt: true);
    public Task SetVolumeAsync(float volume, CancellationToken token = default) => Command(() =>
    {
        if (!float.IsFinite(volume) || volume is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(volume));
        Publish(state with
        {
            Volume = volume
        });
    }, token);
    public Task SetLoopAsync(bool loop, CancellationToken token = default) => Command(() =>
    {
        if (loop && !decoder.CanSeek)
            throw new NotSupportedException("A live decoder cannot loop.");
        Publish(state with
        {
            Loop = loop
        });
    }, token);

    void Execute(Request request)
    {
        try
        {
            if (!request.Reply.Task.IsCompleted)
            {
                request.Token.ThrowIfCancellationRequested();
                request.Work();
                request.Reply.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            request.Reply.TrySetCanceled(request.Token);
        }
        catch (Exception error)
        {
            request.Reply.TrySetException(error);
        }
        finally
        {
            request.Dispose();
        }
    }
    async Task AwaitIo(Task operation)
    {
        while (!operation.IsCompleted)
        {
            // State-only requests never touch the decoder/output while its operation is held.
            while (requests.Reader.TryPeek(out var next) && !next.Interrupt)
                if (requests.Reader.TryRead(out var request))
                    Execute(request);
            if (operation.IsCompleted)
                break;
            if (requests.Reader.TryPeek(out var transport) && transport.Interrupt)
                break;
            using var wake = new CancellationTokenSource();
            var ready = requests.Reader.WaitToReadAsync(wake.Token).AsTask();
            await Task.WhenAny(operation, ready).ConfigureAwait(false);
            wake.Cancel();
            try
            {
                if (!await ready.ConfigureAwait(false))
                    break;
            }
            catch (OperationCanceledException)
            {
                // The owned I/O completed first; this only cancels the channel waiter.
            }
        }
        // Join even during transport/disposal cancellation before touching native resources.
        await operation.ConfigureAwait(false);
    }
    async Task<T> AwaitIo<T>(ValueTask<T> operation)
    {
        var pending = operation.AsTask();
        await AwaitIo(pending).ConfigureAwait(false);
        return await pending.ConfigureAwait(false);
    }

    async Task Run()
    {
        var buffer = new float[Math.Max(1, decoder.Format.SampleRate / 100) * decoder.Format.Channels];
        var gained = new float[buffer.Length];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                if (requests.Reader.TryRead(out var request))
                {
                    Execute(request);
                    continue;
                }
                if (state.Phase != PlaybackPhase.Playing)
                {
                    if (!await requests.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false))
                        break;
                    continue;
                }
                using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lock (ioSync)
                {
                    // Avoid losing a command arriving between the channel read and I/O registration.
                    if (requests.Reader.TryPeek(out _))
                        continue;
                    activeIo = operation;
                }
                bool writing = false;
                try
                {
                    if (buffered == 0)
                    {
                        int count = await AwaitIo(decoder.ReadAsync(buffer, operation.Token)).ConfigureAwait(false);
                        if (count < 0 || count > buffer.Length || count % decoder.Format.Channels != 0)
                            throw new InvalidDataException("Decoder returned an invalid frame count.");
                        buffered = count;
                        if (count == 0)
                        {
                            if (state.Loop && decoder.PositionFrames > 0)
                            {
                                decoder.Seek(0);
                                continue;
                            }
                            await AwaitIo(output.DrainAsync(operation.Token).AsTask()).ConfigureAwait(false);
                            operation.Token.ThrowIfCancellationRequested();
                            if (state.Loop && decoder.PositionFrames > 0)
                            {
                                decoder.Seek(0);
                                continue;
                            }
                            Publish(state with
                            {
                                Phase = PlaybackPhase.Ended
                            });
                            completion.TrySetResult();
                            continue;
                        }
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    for (int i = 0; i < buffered; i++)
                        gained[i] = float.IsFinite(buffer[i]) ? Math.Clamp(buffer[i] * state.Volume, -1, 1) : 0;
                    writing = true;
                    await AwaitIo(output.WriteAsync(gained.AsMemory(0, buffered), operation.Token).AsTask()).ConfigureAwait(false);
                    buffered = 0;
                    Publish(state with
                    {
                        Position = TimeSpan.FromSeconds((double)decoder.PositionFrames / decoder.Format.SampleRate)
                    });
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested && !lifetime.IsCancellationRequested)
                {
                    // Keep decoded but unsubmitted frames. Stop/seek explicitly discard them.
                    if (writing && opened)
                        output.Reset();
                }
                finally
                {
                    lock (ioSync)
                        activeIo = null;
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Publish(state with
            {
                Phase = PlaybackPhase.Failed,
                Error = error.Message
            });
            completion.TrySetException(error);
        }
        finally
        {
            requests.Writer.TryComplete();
            while (requests.Reader.TryRead(out var pending))
            {
                pending.Reply.TrySetException(new ObjectDisposedException(nameof(AudioPlayer)));
                pending.Dispose();
            }
            try
            {
                output.Dispose();
            }
            finally
            {
                decoder.Dispose();
                completion.TrySetCanceled();
                if (Volatile.Read(ref disposing) != 0)
                    Publish(state with
                    {
                        Phase = PlaybackPhase.Disposed
                    });
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        bool first = Interlocked.Exchange(ref disposing, 1) == 0;
        if (first)
        {
            requests.Writer.TryComplete();
            await lifetime.CancelAsync().ConfigureAwait(false);
        }
        await worker.ConfigureAwait(false);
        if (first)
            lifetime.Dispose();
    }
}
