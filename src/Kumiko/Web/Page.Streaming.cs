using System.Text.Json;

namespace Kumiko.Web;

public sealed partial class Page
{
    sealed class StreamCall(IAsyncEnumerator<string> iterator, CancellationTokenSource cancellation, long generation)
    {
        internal readonly long Generation = generation;
        internal readonly IAsyncEnumerator<string> Iterator = iterator;
        internal readonly CancellationTokenSource Cancellation = cancellation;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal bool Pulling;
    }
    readonly Dictionary<long, StreamCall> streamCalls = [];
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource navigation = new();
    long epoch;
    void StreamOpen(long id, string method, JsonElement argument)
    {
        if (streamCalls.Count >= 64 || streamCalls.ContainsKey(id))
            throw new InvalidOperationException("Too many streams or duplicate stream identity.");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, navigation.Token);
        try
        {
            var iterator = window.App.Services.StreamAsync(method, argument.Clone(), new(window, cancellation.Token)).GetAsyncEnumerator(cancellation.Token);
            streamCalls.Add(id, new(iterator, cancellation, epoch));
        }
        catch
        {
            cancellation.Dispose();
            throw;
        }
    }
    void StreamNext(long id)
    {
        if (!streamCalls.TryGetValue(id, out var call) || call.Pulling)
            return;
        call.Pulling = true;
        long generation = epoch;
        _ = Task.Run(async () =>
        {
            using var scope = EnterCall(call.Generation);
            await call.Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                call.Cancellation.Token.ThrowIfCancellationRequested();
                bool next = await call.Iterator.MoveNextAsync().ConfigureAwait(false);
                string json = next ? call.Iterator.Current : "null";
                PostStream(id, call, generation, "{\"t\":\"stream-item\",\"id\":" + id + ",\"done\":" + (next ? "false" : "true") + ",\"v\":" + json + "}", !next);
            }
            catch (Exception error)
            {
                PostStream(id, call, generation, "{\"t\":\"stream-item\",\"id\":" + id + ",\"error\":" + JsonSerializer.Serialize(error.Message, WebJson.Default.String) + "}", true);
            }
            finally
            {
                call.Gate.Release();
            }
        });
    }
    void PostStream(long id, StreamCall call, long generation, string json, bool ended)
    {
        try
        {
            window.App.Dispatch(() =>
            {
                if (disposed || epoch != generation || !streamCalls.TryGetValue(id, out var active) || active != call)
                    return;
                call.Pulling = false;
                Enqueue(json, false);
                if (ended)
                    StreamCancel(id);
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }
    void StreamCancel(long id)
    {
        if (!streamCalls.Remove(id, out var call))
            return;
        call.Cancellation.Cancel();
        _ = CleanupStream(call);
    }
    async Task CleanupStream(StreamCall call)
    {
        using var scope = EnterCall(call.Generation);
        await call.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await call.Iterator.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Navigation/disposal has no active page to receive cleanup failures.
        }
        finally
        {
            call.Gate.Release();
            call.Cancellation.Dispose();
        }
    }
    void CancelStreams()
    {
        epoch++;
        var previous = navigation;
        navigation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        previous.Cancel();
        previous.Dispose();
        foreach (long id in streamCalls.Keys.ToArray())
            StreamCancel(id);
    }
}
