using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flint.Services;

public interface IWorkspaceStorage
{
    Persisted? Load();
    Task Save(Persisted state, CancellationToken token);
}
public sealed class WorkspaceStorage(string directory) : IWorkspaceStorage
{
    readonly SemaphoreSlim gate = new(1, 1);
    public Persisted? Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "workspaces.json")), StorageJson.Default.Persisted);
            if (value is null || value.Workspaces.IsDefaultOrEmpty)
                return null;
            if (value.Workspaces.Any(w => w is null || string.IsNullOrWhiteSpace(w.Id) || string.IsNullOrWhiteSpace(w.Name)
                || w.Account is null || w.Repositories.IsDefault || w.Repositories.Any(string.IsNullOrWhiteSpace)
                || (w.Active is not null && !w.Repositories.Contains(w.Active))))
                return null;
            if (!value.Workspaces.Any(w => w.Id == value.ActiveWorkspace)
                || value.Workspaces.Select(w => w.Id).Distinct().Count() != value.Workspaces.Length)
                return null;
            return value;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
    public async Task Save(Persisted state, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        var temp = Path.Combine(directory, ".workspaces-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, StorageJson.Default.Persisted), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temp, Path.Combine(directory, "workspaces.json"), true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
            gate.Release();
        }
    }
}
[JsonSerializable(typeof(Persisted))]
partial class StorageJson : JsonSerializerContext;
