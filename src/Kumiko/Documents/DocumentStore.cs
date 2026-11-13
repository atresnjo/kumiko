using System.Security.Cryptography;
using System.Text;

namespace Kumiko.Documents;

public interface IDocumentCodec<T> where T : notnull
{
    ReadOnlyMemory<byte> Encode(T value);
    T Decode(ReadOnlyMemory<byte> bytes);
}

public sealed record Utf8DocumentCodec : IDocumentCodec<string>
{
    static readonly UTF8Encoding Encoding = new(false, true);
    public ReadOnlyMemory<byte> Encode(string value) => Encoding.GetBytes(value);
    public string Decode(ReadOnlyMemory<byte> bytes) => Encoding.GetString(bytes.Span);
}

public interface IDocumentStore
{
    Task<DocumentRead<T>> ReadAsync<T>(string path, IDocumentCodec<T> codec, CancellationToken token) where T : notnull;
    Task<DocumentSaved<T>> SaveAsync<T>(DocumentRequest<T> request, IDocumentCodec<T> codec, CancellationToken token) where T : notnull;
    Task<FileVersion> VersionAsync(string path, CancellationToken token);
    Task MoveAsync(string source, string destination, FileVersion expected, CancellationToken token);
}

public sealed class DocumentConflictException(string path) : IOException("The document changed outside this save: " + path)
{
    public string Path { get; } = path;
}

/// <summary>Bounded asynchronous storage with sibling-file atomic replacement and optimistic conflict detection.
/// Serializes cooperating writers in this process. Uncooperative external writers can race the final version check.
/// File contents are flushed before rename; directory-entry durability across power loss is filesystem dependent.</summary>
public sealed class FileDocumentStore : IDocumentStore, IDisposable
{
    static readonly SemaphoreSlim[] Gates = Enumerable.Range(0, 127).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    readonly CancellationTokenSource lifetime = new();
    readonly object sync = new();
    bool disposed;
    public int MaximumBytes
    {
        get;
    }
    public FileDocumentStore(int maximumBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        MaximumBytes = maximumBytes;
    }

    CancellationTokenSource Operation(CancellationToken token)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        }
    }
    static int GateIndex(string path) => (int)((uint)StringComparer.Ordinal.GetHashCode(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? path.ToUpperInvariant() : path) % (uint)Gates.Length);
    static FileVersion Fingerprint(ReadOnlySpan<byte> bytes) => new(true, Convert.ToHexString(SHA256.HashData(bytes)));

    async Task<byte[]> Bytes(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
        if (input.Length > MaximumBytes)
            throw new InvalidDataException("Document exceeds the configured size limit.");
        using var output = new MemoryStream();
        var buffer = new byte[65536];
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > MaximumBytes)
                throw new InvalidDataException("Document exceeds the configured size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    async Task<FileVersion> Version(string path, CancellationToken token)
    {
        try
        {
            return Fingerprint(await Bytes(path, token).ConfigureAwait(false));
        }
        catch (FileNotFoundException)
        {
            return FileVersion.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return FileVersion.Missing;
        }
    }

    public async Task<FileVersion> VersionAsync(string path, CancellationToken token)
    {
        using var operation = Operation(token);
        return await Version(System.IO.Path.GetFullPath(path), operation.Token).ConfigureAwait(false);
    }
    public async Task<DocumentRead<T>> ReadAsync<T>(string path, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
    {
        using var operation = Operation(token);
        path = System.IO.Path.GetFullPath(path);
        var bytes = await Bytes(path, operation.Token).ConfigureAwait(false);
        operation.Token.ThrowIfCancellationRequested();
        return new(path, codec.Decode(bytes), Fingerprint(bytes));
    }

    public async Task<DocumentSaved<T>> SaveAsync<T>(DocumentRequest<T> request, IDocumentCodec<T> codec, CancellationToken token) where T : notnull
    {
        using var operation = Operation(token);
        token = operation.Token;
        token.ThrowIfCancellationRequested();
        if (request.Operation != DocumentOperation.Save)
            throw new ArgumentException("Expected a save request.", nameof(request));
        string path = System.IO.Path.GetFullPath(request.Path);
        var destinationInfo = new FileInfo(path);
        if (destinationInfo.LinkTarget is not null)
            path = destinationInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? throw new IOException("Unresolved document symbolic link.");
        var data = codec.Encode(request.Value).ToArray();
        if (data.Length > MaximumBytes)
            throw new InvalidDataException("Document exceeds the configured size limit.");
        var gate = Gates[GateIndex(path)];
        await gate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (await Version(path, token).ConfigureAwait(false) != request.Expected)
                throw new DocumentConflictException(path);
            temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".kumiko-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await output.WriteAsync(data, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            if (await Version(path, token).ConfigureAwait(false) != request.Expected)
                throw new DocumentConflictException(path);
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() && request.Expected.Exists)
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
            DocumentReplacement.Commit(temporary, path, request.Expected.Exists);
            temporary = null;
            // Cancellation after the atomic commit does not turn a committed save into a failure.
            return new(request, Fingerprint(data));
        }
        finally
        {
            try
            {
                if (temporary is not null)
                    File.Delete(temporary);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    public async Task MoveAsync(string source, string destination, FileVersion expected, CancellationToken token)
    {
        using var operation = Operation(token);
        token = operation.Token;
        source = System.IO.Path.GetFullPath(source);
        destination = System.IO.Path.GetFullPath(destination);
        var indices = new[] { GateIndex(source), GateIndex(destination) }.Distinct().Order().ToArray();
        int held = 0;
        try
        {
            foreach (var index in indices)
            {
                await Gates[index].WaitAsync(token).ConfigureAwait(false);
                held++;
            }
            if (!expected.Exists || await Version(source, token).ConfigureAwait(false) != expected || File.Exists(destination))
                throw new DocumentConflictException(source);
            token.ThrowIfCancellationRequested();
            File.Move(source, destination, overwrite: false);
        }
        finally
        {
            for (int i = held - 1; i >= 0; i--)
                Gates[indices[i]].Release();
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
                return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}
