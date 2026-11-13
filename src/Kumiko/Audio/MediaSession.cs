namespace Kumiko.Audio;

public enum MediaCommand
{
    Play,
    Pause,
    Toggle,
    Stop,
    Next,
    Previous,
    Seek,
    SetVolume,
    SetLoop
}
public sealed record MediaRequest(MediaCommand Command, TimeSpan? Position = null)
{
    public float? Volume
    {
        get; init;
    }
    public bool? Loop
    {
        get; init;
    }
}
public sealed record MediaMetadata(string Id, string Title, string Artist = "", string Album = "", bool CanNext = false, bool CanPrevious = false, bool CanSeek = true);

/// <summary>Explicit owned system-media boundary. Fixtures inject this without publishing system state.</summary>
public interface IMediaSessionBackend : IDisposable
{
    void Start(Action<MediaRequest> requested);
    void Publish(MediaMetadata metadata, PlaybackState state);
}

/// <summary>Owner-thread media publication; callbacks may originate on native threads and should dispatch messages.</summary>
public sealed class MediaSession : IDisposable
{
    readonly int thread = Environment.CurrentManagedThreadId;
    readonly IMediaSessionBackend backend;
    int disposed;
    public event Action<MediaRequest>? Requested;
    public MediaSession(IMediaSessionBackend backend)
    {
        this.backend = backend;
        try
        {
            backend.Start(request =>
            {
                if (Volatile.Read(ref disposed) == 0)
                    Requested?.Invoke(request);
            });
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
    /// <summary>Explicit opt-in. Hidden apps are forbidden from publishing a system media session.</summary>
    public static MediaSession System(App app, Window? window = null, string identity = "kumiko")
    {
        app.CheckThread();
        if (app.Options.Hidden)
            throw new InvalidOperationException("Hidden applications must inject an isolated media backend.");
        return new(OperatingSystem.IsMacOS() ? new MacMediaSession() : OperatingSystem.IsWindows() ?
            new WindowsMediaSession(window?.NativeHandle ?? throw new ArgumentException("Windows media controls require an owning window.", nameof(window))) :
            OperatingSystem.IsLinux() ? new LinuxMediaSession(identity) : throw new PlatformNotSupportedException());
    }
    void Check()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Media sessions belong to their creating thread.");
    }
    public void Publish(MediaMetadata metadata, PlaybackState state)
    {
        Check();
        if (metadata.Id.Length is 0 or > 1024 || metadata.Title.Length > 8192 || metadata.Artist.Length > 8192 || metadata.Album.Length > 8192 ||
            state.Position < TimeSpan.Zero || state.Duration < TimeSpan.Zero)
            throw new ArgumentException("Invalid media metadata or timeline.");
        backend.Publish(metadata, state);
    }
    public void Dispose()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;
        Check();
        Interlocked.Exchange(ref disposed, 1);
        Requested = null;
        backend.Dispose();
    }
}
