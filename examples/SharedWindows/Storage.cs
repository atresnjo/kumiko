using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedWindows;

public interface IWorkspaceStore
{
    Task<WorkspaceDocument?> Load(CancellationToken token);
    Task Save(WorkspaceDocument document, CancellationToken token);
}

public sealed class MemoryWorkspaceStore : IWorkspaceStore
{
    WorkspaceDocument? saved;
    public Task<WorkspaceDocument?> Load(CancellationToken token) => Task.FromResult(Volatile.Read(ref saved));
    public Task Save(WorkspaceDocument document, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Volatile.Write(ref saved, document);
        return Task.CompletedTask;
    }
}

sealed class FileWorkspaceStore(string path) : IWorkspaceStore
{
    readonly SemaphoreSlim writes = new(1, 1);
    public async Task<WorkspaceDocument?> Load(CancellationToken token) => File.Exists(path)
        ? JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, token), StoreJson.Default.WorkspaceDocument) : null;
    public async Task Save(WorkspaceDocument document, CancellationToken token)
    {
        await writes.WaitAsync(token);
        try
        {
            var temporary = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(document, StoreJson.Default.WorkspaceDocument), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            writes.Release();
        }
    }
}

[JsonSerializable(typeof(WorkspaceDocument))]
internal partial class StoreJson : JsonSerializerContext;
