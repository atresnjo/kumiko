using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kumiko.Desktop;
/// <summary>One current-user pipe server; Windows uses named pipes, Unix uses the runtime's Unix-domain socket transport.</summary>
internal sealed class SingleInstance : IDisposable
{
    const int MaximumMessage = 256 * 1024;
    readonly App app;
    readonly string name;
    readonly CancellationTokenSource stop = new();
    readonly NamedPipeServerStream server;
    readonly IDisposable ownership;
    int disposed;
    SingleInstance(App app, string name, NamedPipeServerStream server, IDisposable ownership)
    {
        this.app = app;
        this.name = name;
        this.server = server;
        this.ownership = ownership;
        _ = Listen();
    }

    internal static SingleInstance? Acquire(App app, string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + "\0" + identity));
        var name = "k" + Convert.ToHexString(hash.AsSpan(0, 12));
        var ownership = InstanceOwnership.Acquire(name);
        if (ownership is not null)
        {
            try
            {
                // Crash-released OS ownership serializes server creation. Without
                // FirstPipeInstance, the runtime safely replaces stale Unix sockets.
                var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                return new(app, name, server, ownership);
            }
            catch
            {
                ownership.Dispose();
                throw;
            }
        }
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        client.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
        var request = new SecondInstanceEvent(Array.AsReadOnly(Environment.GetCommandLineArgs().Skip(1).ToArray()), Environment.CurrentDirectory);
        Send(client, request, timeout.Token).GetAwaiter().GetResult();
        var ack = new byte[1];
        client.ReadExactlyAsync(ack, timeout.Token).AsTask().GetAwaiter().GetResult();
        if (ack[0] != 1)
            throw new IOException("The first instance rejected the launch request.");
        return null;
    }

    static async Task Send(Stream stream, SecondInstanceEvent request, CancellationToken cancellation)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("cwd", request.WorkingDirectory);
            json.WriteStartArray("argv");
            foreach (var argument in request.Arguments)
                json.WriteStringValue(argument);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        if (buffer.Length > MaximumMessage)
            throw new IOException("Single-instance arguments exceed 256 KiB.");
        var size = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(size, (int)buffer.Length);
        await stream.WriteAsync(size, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(buffer.ToArray(), cancellation).ConfigureAwait(false);
        await stream.FlushAsync(cancellation).ConfigureAwait(false);
    }

    async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var size = new byte[4];
                await server.ReadExactlyAsync(size, timeout.Token).ConfigureAwait(false);
                int length = BinaryPrimitives.ReadInt32LittleEndian(size);
                if (length is <= 0 or > MaximumMessage)
                    throw new IOException("Invalid single-instance message length.");
                var bytes = new byte[length];
                await server.ReadExactlyAsync(bytes, timeout.Token).ConfigureAwait(false);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                var directory = root.GetProperty("cwd").GetString();
                if (directory is null || !Path.IsPathFullyQualified(directory))
                    throw new IOException("Single-instance working directory must be absolute.");
                var arguments = root.GetProperty("argv").EnumerateArray().Select(a => a.GetString() ?? throw new IOException("Null argument.")).ToArray();
                var request = new SecondInstanceEvent(Array.AsReadOnly(arguments), directory);
                app.Dispatch(() =>
                {
                    if (Volatile.Read(ref disposed) == 0)
                        app.DeliverSecondInstance(request);
                });
                await server.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
                await server.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                if (!stop.IsCancellationRequested)
                    try
                    {
                        app.Dispatch(() => app.Report(new IOException($"Single-instance channel {name}: {ex.Message}", ex)));
                    }
                    catch (ObjectDisposedException)
                    {
                    }
            }
            finally
            {
                if (!stop.IsCancellationRequested && server.IsConnected)
                    server.Disconnect();
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        stop.Cancel();
        server.Dispose();
        ownership.Dispose();
        // Listen owns pending asynchronous continuations; cancellation remains
        // valid until they have observed it. No blocking join on the UI thread.
    }
}
