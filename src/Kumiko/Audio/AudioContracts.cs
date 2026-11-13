namespace Kumiko.Audio;

public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    public void Validate()
    {
        if (SampleRate is < 8000 or > 384000 || Channels is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(AudioFormat), "Expected 8–384 kHz and 1–8 channels.");
    }
}

/// <summary>Owned seekable or live decoder. Reads interleaved normalized float samples in complete frames.
/// One player worker calls ReadAsync/Seek/Dispose; LengthFrames is null for unknown/live duration.
/// Live/network decoders override ReadAsync and honor cancellation; synchronous Read must finish in bounded time.</summary>
public interface IAudioDecoder : IDisposable
{
    AudioFormat Format
    {
        get;
    }
    long? LengthFrames
    {
        get;
    }
    long PositionFrames
    {
        get;
    }
    bool CanSeek
    {
        get;
    }
    int Read(Span<float> samples);
    ValueTask<int> ReadAsync(Memory<float> samples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(samples.Span));
    }
    void Seek(long frame);
}

/// <summary>Owned device boundary. Write provides bounded device backpressure. Native device latency can lag the submitted position.
/// All calls are serialized on the player's worker. Reset discards queued output; cancellation must unblock Write.</summary>
public interface IAudioOutput : IDisposable
{
    void Open(AudioFormat format);
    ValueTask WriteAsync(ReadOnlyMemory<float> samples, CancellationToken token);
    ValueTask DrainAsync(CancellationToken token) => ValueTask.CompletedTask;
    void Reset();
}

public enum PlaybackPhase
{
    Stopped,
    Playing,
    Paused,
    Ended,
    Failed,
    Disposed
}
public sealed record PlaybackState(PlaybackPhase Phase, TimeSpan Position, TimeSpan? Duration, float Volume, bool Loop, string? Error = null);

/// <summary>Explicit opt-in to actual sound. Offscreen consumers must inject their own silent IAudioOutput.</summary>
public static class AudioOutput
{
    public static IAudioOutput SystemDefault() => OperatingSystem.IsMacOS() ? new MacAudioOutput() :
        OperatingSystem.IsWindows() ? new WindowsAudioOutput() : OperatingSystem.IsLinux() ? new LinuxAudioOutput() :
        throw new PlatformNotSupportedException("No native audio output for this platform.");
}
