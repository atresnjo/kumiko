using Kumiko.Audio;

static class MediaFlows
{
    internal static void Run(Action<bool, string> check)
    {
        var backend = new FixtureMedia();
        var requests = new List<MediaRequest>();
        using (var session = new MediaSession(backend))
        {
            session.Requested += requests.Add;
            session.Publish(new("track", "Title", "Artist"), new(PlaybackPhase.Paused, TimeSpan.Zero, TimeSpan.FromSeconds(1), 1, false));
            backend.Callback!(new(MediaCommand.Play));
            check(backend.Published == 1 && requests is [{ Command: MediaCommand.Play }], "injected media boundary publishes state and returns typed commands");
        }
        backend.Callback!(new(MediaCommand.Next));
        check(backend.Disposed && requests.Count == 1, "late media callback cannot act after session disposal");
    }
    sealed class FixtureMedia : IMediaSessionBackend
    {
        internal Action<MediaRequest>? Callback;
        internal int Published;
        internal bool Disposed;
        public void Start(Action<MediaRequest> requested) => Callback = requested;
        public void Publish(MediaMetadata metadata, PlaybackState state) => Published++;
        public void Dispose() => Disposed = true;
    }
}
