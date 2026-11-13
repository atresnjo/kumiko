using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flint.Git;

namespace Flint.Services;

public interface IRepositoryCache
{
    Snapshot? Read(string path);
    Task Write(Snapshot snapshot, CancellationToken token);
}
/// <summary>Disposable revalidation hints. Credentials, drafts and patch contents never enter the disk cache.</summary>
public sealed class RepositoryCache(string directory) : IRepositoryCache
{
    string FileName(string path) => Path.Combine(directory, "cache", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))) + ".json");
    public Snapshot? Read(string path)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize(File.ReadAllText(FileName(path)), CacheJson.Default.Snapshot);
            return snapshot?.Path == path && snapshot.Head is not null && !snapshot.Commits.IsDefault && !snapshot.Graph.IsDefault
                && snapshot.Graph.Length == snapshot.Commits.Length && !snapshot.Refs.IsDefault
                && !snapshot.Staged.IsDefault && !snapshot.Unstaged.IsDefault && !snapshot.Stashes.IsDefault
                && !snapshot.Worktrees.IsDefault ? snapshot : null;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    public async Task Write(Snapshot snapshot, CancellationToken token)
    {
        if (snapshot.Commits.Length > 1500)
            snapshot = snapshot with
            {
                Commits = snapshot.Commits[..1500],
                Graph = snapshot.Graph[..1500],
                HasMore = true,
                Limit = 1500
            };
        var file = FileName(snapshot.Path);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, CacheJson.Default.Snapshot), token).ConfigureAwait(false);
            File.Move(temp, file, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A cache failure must not turn a successful Git action into a failed action.
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
[JsonSerializable(typeof(Snapshot))]
partial class CacheJson : JsonSerializerContext;
