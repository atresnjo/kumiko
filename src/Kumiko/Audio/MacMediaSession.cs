using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Audio;

internal sealed class MacMediaSession : IMediaSessionBackend
{
    static int active;
    static nint library;
    readonly List<(nint Command, nint Target, NativeBlock Block)> targets = [];
    nint center;
    bool owns, disposed;
    MediaMetadata? metadata;
    public void Start(Action<MediaRequest> requested)
    {
        if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
            throw new InvalidOperationException("This process already owns a system media session.");
        owns = true;
        library = NativeLibrary.Load("/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer");
        using var pool = new AutoreleasePool();
        center = O.O(O.Class("MPNowPlayingInfoCenter"), "defaultCenter");
        var commands = O.O(O.Class("MPRemoteCommandCenter"), "sharedCommandCenter");
        void Add(string selector, MediaCommand command)
        {
            nint native = O.O(commands, selector);
            var block = NativeBlock.ResultPointer(value =>
            {
                if (disposed || metadata is null || command == MediaCommand.Next && !metadata.CanNext ||
                    command == MediaCommand.Previous && !metadata.CanPrevious || command == MediaCommand.Seek && !metadata.CanSeek)
                    return 100;
                TimeSpan? position = command == MediaCommand.Seek ? TimeSpan.FromSeconds(O.D(value, "positionTime")) : null;
                requested(new(command, position));
                return 0;
            });
            nint target = O.O(native, "addTargetWithHandler:", block.Handle);
            O.V(target, "retain");
            targets.Add((native, target, block));
        }
        Add("playCommand", MediaCommand.Play);
        Add("pauseCommand", MediaCommand.Pause);
        Add("togglePlayPauseCommand", MediaCommand.Toggle);
        Add("stopCommand", MediaCommand.Stop);
        Add("nextTrackCommand", MediaCommand.Next);
        Add("previousTrackCommand", MediaCommand.Previous);
        Add("changePlaybackPositionCommand", MediaCommand.Seek);
    }
    public void Publish(MediaMetadata metadata, PlaybackState state)
    {
        this.metadata = metadata;
        using var pool = new AutoreleasePool();
        var info = O.New("NSMutableDictionary");
        try
        {
            nint Key(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
            void Text(string key, string value) => O.V(info, "setObject:forKey:", O.TempString(value), Key(key));
            void Number(string key, double value) => O.V(info, "setObject:forKey:", O.O(O.Class("NSNumber"), "numberWithDouble:", value), Key(key));
            Text("MPNowPlayingInfoPropertyExternalContentIdentifier", metadata.Id);
            Text("MPMediaItemPropertyTitle", metadata.Title);
            Text("MPMediaItemPropertyArtist", metadata.Artist);
            Text("MPMediaItemPropertyAlbumTitle", metadata.Album);
            Number("MPNowPlayingInfoPropertyElapsedPlaybackTime", state.Position.TotalSeconds);
            Number("MPNowPlayingInfoPropertyPlaybackRate", state.Phase == PlaybackPhase.Playing ? 1 : 0);
            if (state.Duration is { } duration)
                Number("MPMediaItemPropertyPlaybackDuration", duration.TotalSeconds);
            O.V(center, "setNowPlayingInfo:", info);
            O.V(center, "setPlaybackState:", (nuint)(state.Phase == PlaybackPhase.Playing ? 1 : state.Phase == PlaybackPhase.Paused ? 2 : 3));
            for (int i = 0; i < targets.Count; i++)
                O.V(targets[i].Command, "setEnabled:", (byte)(i == 4 ? metadata.CanNext ? 1 : 0 : i == 5 ? metadata.CanPrevious ? 1 : 0 : i == 6 ? metadata.CanSeek ? 1 : 0 : 1));
        }
        finally
        {
            O.Release(info);
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (!owns)
            return;
        using var pool = new AutoreleasePool();
        foreach (var (command, target, block) in targets)
        {
            O.V(command, "removeTarget:", target);
            O.Release(target);
            block.Dispose();
        }
        targets.Clear();
        if (center != 0)
        {
            O.V(center, "setNowPlayingInfo:", (nint)0);
            O.V(center, "setPlaybackState:", (nuint)0);
        }
        Interlocked.Exchange(ref active, 0);
    }
}
