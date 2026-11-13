using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kumiko.Documents;

/// <summary>Explicit private recovery directory. Contains unsaved content; never uses user defaults.
/// The caller owns scheduling, retention and cleanup after a confirmed close/save.</summary>
public sealed class DocumentRecovery : IDisposable, IAsyncDisposable
{
    readonly string directory;
    readonly FileStream lease;
    readonly object sync = new();
    readonly SemaphoreSlim storage = new(1, 1);
    readonly CancellationTokenSource lifetime = new();
    readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly Dictionary<string, long> generations = new(StringComparer.Ordinal);
    int active;
    bool disposed, cancelled;

    sealed class Operation(DocumentRecovery owner, CancellationTokenSource cancellation, long generation) : IDisposable
    {
        internal CancellationToken Token => cancellation.Token;
        internal readonly long Generation = generation;
        public void Dispose()
        {
            cancellation.Dispose();
            lock (owner.sync)
            {
                owner.active--;
                owner.CloseIfIdle();
            }
        }
    }
    Operation Begin(string? id, bool change, CancellationToken token)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            long generation = id is null ? 0 : generations.GetValueOrDefault(id);
            if (change)
                generations[id!] = generation = checked(generation + 1);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            active++;
            return new(this, cancellation, generation);
        }
    }
    void Current(string id, Operation operation)
    {
        operation.Token.ThrowIfCancellationRequested();
        lock (sync)
            if (generations.GetValueOrDefault(id) != operation.Generation)
                throw new OperationCanceledException("Recovery write was superseded or removed.");
    }
    void CloseIfIdle()
    {
        if (!disposed || !cancelled || active != 0 || closed.Task.IsCompleted)
            return;
        lease.Dispose();
        storage.Dispose();
        lifetime.Dispose();
        closed.TrySetResult();
    }
    public DocumentRecovery(string directory, string session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        this.directory = Path.Combine(Path.GetFullPath(directory), Hash(session));
        Directory.CreateDirectory(this.directory);
        lease = new FileStream(Path.Combine(this.directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    string Location(string id) => Path.Combine(directory, Hash(id) + ".recovery");

    public async Task WriteAsync<T>(Document<T> document, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
    {
        using var operation = Begin(document.Id, true, token);
        token = operation.Token;
        using var bytes = new MemoryStream();
        using (var json = new Utf8JsonWriter(bytes))
        {
            json.WriteStartObject();
            json.WriteNumber("schema", 1);
            json.WriteString("id", document.Id);
            json.WriteString("title", document.Title);
            json.WriteString("path", document.Path);
            json.WriteNumber("revision", document.Revision);
            json.WriteBoolean("hasSaved", document.HasSaved);
            json.WriteBoolean("exists", document.Version.Exists);
            json.WriteString("digest", document.Version.Digest);
            json.WriteBase64String("value", codec.Encode(document.Value).Span);
            json.WriteBase64String("saved", codec.Encode(document.SavedValue).Span);
            json.WriteEndObject();
        }
        await storage.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Current(document.Id, operation);
            using var store = new FileDocumentStore();
            string path = Location(document.Id);
            var expected = await store.VersionAsync(path, token).ConfigureAwait(false);
            var request = Document<byte[]>.Create(document.Id, document.Title, bytes.ToArray()).BeginSave(path, expected).Request;
            await store.SaveAsync(request, new BytesCodec(), token).ConfigureAwait(false);
        }
        finally
        {
            storage.Release();
        }
    }

    public async Task<Document<T>?> ReadAsync<T>(string id, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
    {
        using var operation = Begin(null, false, token);
        await storage.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            return await ReadCore(id, codec, operation.Token).ConfigureAwait(false);
        }
        finally
        {
            storage.Release();
        }
    }
    async Task<Document<T>?> ReadCore<T>(string id, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
    {
        using var store = new FileDocumentStore();
        DocumentRead<byte[]> read;
        try
        {
            read = await store.ReadAsync(Location(id), new BytesCodec(), token).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        using var json = JsonDocument.Parse(read.Value, new()
        {
            MaxDepth = 8
        });
        var root = json.RootElement;
        if (root.GetProperty("schema").GetInt32() != 1 || root.GetProperty("id").GetString() != id)
            throw new InvalidDataException("Unknown or mismatched recovery document.");
        var result = Document<T>.Create(id, root.GetProperty("title").GetString() ?? "Recovered", codec.Decode(root.GetProperty("value").GetBytesFromBase64())) with
        {
            Path = root.GetProperty("path").GetString(),
            SavedValue = codec.Decode(root.GetProperty("saved").GetBytesFromBase64()),
            HasSaved = root.GetProperty("hasSaved").GetBoolean(),
            Version = new(root.GetProperty("exists").GetBoolean(), root.GetProperty("digest").GetString() ?? ""),
            Revision = root.GetProperty("revision").GetInt64()
        };
        token.ThrowIfCancellationRequested();
        return result;
    }
    public async Task<IReadOnlyList<Document<T>>> ReadAllAsync<T>(IDocumentCodec<T> codec, CancellationToken token = default) where T : notnull
    {
        using var operation = Begin(null, false, token);
        token = operation.Token;
        await storage.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var documents = new List<Document<T>>();
            using var store = new FileDocumentStore();
            foreach (string path in Directory.EnumerateFiles(directory, "*.recovery").Order(StringComparer.Ordinal))
            {
                var read = await store.ReadAsync(path, new BytesCodec(), token).ConfigureAwait(false);
                using var json = JsonDocument.Parse(read.Value, new()
                {
                    MaxDepth = 8
                });
                string id = json.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("Recovery identity is missing.");
                var document = await ReadCore(id, codec, token).ConfigureAwait(false);
                if (document is not null)
                    documents.Add(document);
            }
            return documents.AsReadOnly();
        }
        finally
        {
            storage.Release();
        }
    }
    public void Remove(string id) => RemoveAsync(id).GetAwaiter().GetResult();
    public async Task RemoveAsync(string id, CancellationToken token = default)
    {
        using var operation = Begin(id, true, token);
        await storage.WaitAsync(operation.Token).ConfigureAwait(false);
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            File.Delete(Location(id));
        }
        finally
        {
            storage.Release();
        }
    }
    /// <summary>Starts cancellation immediately. The lease remains held until active operations quiesce.
    /// Await DisposeAsync when shutdown must finish before another owner opens this session.</summary>
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
        }
        try
        {
            lifetime.Cancel();
        }
        finally
        {
            lock (sync)
            {
                cancelled = true;
                CloseIfIdle();
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(closed.Task);
    }
    sealed class BytesCodec : IDocumentCodec<byte[]>
    {
        public byte[] Decode(ReadOnlyMemory<byte> bytes) => bytes.ToArray();
        public ReadOnlyMemory<byte> Encode(byte[] value) => value;
    }
}
