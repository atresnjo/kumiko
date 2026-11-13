using System.Collections.Immutable;
using DiskMap.Core;
using Kumiko.Runtime;

namespace DiskMap;

/// <summary>Commands that reach the desktop or the disk; they run on workers and answer with messages.</summary>
internal static class Effects
{
    public static Cmd<Msg> ReadVolume(string root) => Cmd<Msg>.Run("volume", _ => Task.Run(() => (Msg)new Msg.VolumeRead(Volume.Of(root))),
        _ => new Msg.VolumeRead(null));

    public static Cmd<Msg> Reveal(string path) => Cmd<Msg>.Run<IDesk>("reveal", (desk, _) =>
    {
        desk.Reveal(path);
        return Task.FromResult<Msg>(new Msg.Noted(null));
    }, error => new Msg.Noted(error.Message));

    public static Cmd<Msg> Open(string path) => Cmd<Msg>.Run<IDesk>("open", (desk, _) =>
    {
        desk.Open(path);
        return Task.FromResult<Msg>(new Msg.Noted(null));
    }, error => new Msg.Noted(error.Message));

    public static Cmd<Msg> Choose(string start) => Cmd<Msg>.Run<IDesk>("choose", async (desk, token) =>
        (Msg)new Msg.Chosen(await desk.ChooseFolder(start, token).ConfigureAwait(false)), error => new Msg.Noted(error.Message));

    /// <summary>Moves each path to the Trash; reports the ones that moved and the first failure.</summary>
    public static Cmd<Msg> Trash(ImmutableArray<string> paths) => Cmd<Msg>.Run<IDesk>("trash", (desk, _) => Task.Run(() =>
    {
        var moved = ImmutableArray.CreateBuilder<string>();
        string? failure = null;
        foreach (var path in paths)
        {
            try
            {
                desk.Trash(path);
                moved.Add(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                failure ??= $"Could not move {Path.GetFileName(path)} to the Trash: {error.Message}";
            }
        }
        return (Msg)new Msg.Trashed(moved.ToImmutable(), failure);
    }), error => new Msg.Trashed([], error.Message));
}
