using Kumiko;
using Kumiko.Desktop;

namespace DiskMap;

/// <summary>What DiskMap asks of the desktop. Tests use <see cref="RecordingDesk"/> so nothing reaches Finder or the Trash.</summary>
internal interface IDesk
{
    Task<string?> ChooseFolder(string start, CancellationToken token);
    void Reveal(string path);
    void Open(string path);
    void Trash(string path);
}

/// <summary>Finder, the Trash and the folder picker; the picker runs modally on the App thread.</summary>
internal sealed class NativeDesk(App app) : IDesk
{
    public async Task<string?> ChooseFolder(string start, CancellationToken token)
    {
        var chosen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Dispatch(() =>
        {
            try
            {
                var paths = Dialogs.Open(new("Choose a Folder to Scan", start, Files: false, Directories: true));
                chosen.TrySetResult(paths.Length > 0 ? paths[0] : null);
            }
            catch (Exception error)
            {
                chosen.TrySetException(error);
            }
        });
        return await chosen.Task.WaitAsync(token).ConfigureAwait(false);
    }

    public void Reveal(string path) => Shell.Reveal(path);
    public void Open(string path) => Shell.Open(path);
    public void Trash(string path) => Shell.Trash(path);
}

/// <summary>Records requests instead of performing them; trashing a path listed in Refuse fails.</summary>
internal sealed class RecordingDesk : IDesk
{
    readonly object gate = new();
    readonly List<string> calls = [];

    public string? Folder
    {
        get; set;
    }
    public HashSet<string> Refuse { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (gate)
                return [.. calls];
        }
    }

    public Task<string?> ChooseFolder(string start, CancellationToken token) => Task.FromResult(Record("choose:" + start, Folder));
    public void Reveal(string path) => Record("reveal:" + path, path);
    public void Open(string path) => Record("open:" + path, path);

    public void Trash(string path)
    {
        Record("trash:" + path, path);
        if (Refuse.Contains(path))
            throw new IOException("Could not move the item to Trash.");
    }

    T Record<T>(string call, T result)
    {
        lock (gate)
            calls.Add(call);
        return result;
    }
}
