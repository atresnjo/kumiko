using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Web;

static class StreamingFlows
{
    internal static async Task Run(Action<bool, string> check)
    {
        var registry = new ServiceRegistry();
        registry.BindStream<int, int>("Numbers", "range", static (_, count) => Range(count), StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        using var argument = JsonDocument.Parse("3");
        var values = new List<string>();
        await foreach (var value in registry.StreamAsync("Numbers.range", argument.RootElement, default))
            values.Add(value);
        check(values.SequenceEqual(new[] { "0", "1", "2" }), "typed streaming registry serializes a producer incrementally");
        check(registry.TypeScript().Contains("AsyncIterable<number>", StringComparison.Ordinal), "generated client exposes typed async iteration");
        using var cancel = new CancellationTokenSource();
        await using var iterator = registry.StreamAsync("Numbers.range", argument.RootElement, new(null, cancel.Token)).GetAsyncEnumerator();
        check(await iterator.MoveNextAsync() && iterator.Current == "0", "stream begins only when the consumer pulls");
        cancel.Cancel();
        try
        {
            await iterator.MoveNextAsync();
            check(false, "stream cancellation observed");
        }
        catch (OperationCanceledException)
        {
            check(true, "stream cancellation observed");
        }
    }
    static async IAsyncEnumerable<int> Range(int count, [EnumeratorCancellation] CancellationToken token = default)
    {
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}
[JsonSerializable(typeof(int))]
internal partial class StreamJson : JsonSerializerContext;

static class NativeStreamingFlow
{
    internal static void Run(Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new Kumiko.App(new(Name: "Kumiko streaming fixture", Hidden: true));
        try
        {
            using var forbidden = Kumiko.Audio.MediaSession.System(app);
            check(false, "hidden app cannot publish system media");
        }
        catch (InvalidOperationException)
        {
            check(true, "hidden app rejects system media session before opening native backend");
        }
        var logs = new List<string>();
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        app.Services.BindStream<int, int>("Numbers", "range", static (_, count) => Range(count), StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        using var window = app.NewPage(new(Visible: false), new(Assets: [Asset.Text("/index.html", """
            <!doctype html><html><body><script type="module">
            import {Numbers} from '/kumiko.js';
            const values=[];
            for await (const value of Numbers.range(3)) values.push(value);
            kumiko.log('stream:'+values.join(','));
            try { for await (const value of kumiko.stream('Missing.stream',{})) {} }
            catch(error) { kumiko.log('stream-error'); }
            </script></body></html>
            """)]));
        int heldStarted = 0, heldDisposed = 0;
        app.Services.BindStream<int, int>("Held", "wait", (ctx, _) => Hold(ctx.CancellationToken, () => Interlocked.Increment(ref heldStarted), () => Interlocked.Increment(ref heldDisposed)),
            StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        window.Page!.Logged += logs.Add;
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(10) && logs.Count < 2)
            app.RunFor(TimeSpan.FromMilliseconds(40));
        check(logs.Contains("stream:0,1,2") && logs.Contains("stream-error"), "hidden WKWebView executes generated streaming client with completion and errors");
        window.Page.Evaluate("window.held=kumiko.stream('Held.wait',0);window.held.next().catch(()=>{});");
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && Volatile.Read(ref heldStarted) == 0)
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(heldStarted == 1, "native stream starts a held producer on demand");
        window.Page.Load("/index.html");
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && Volatile.Read(ref heldDisposed) == 0)
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(heldDisposed == 1, "webview navigation cancels and disposes held producer before late replies can reach the new page");
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && !window.Page.IsReady)
            app.RunFor(TimeSpan.FromMilliseconds(20));
        window.Page.Evaluate("window.held=kumiko.stream('Held.wait',0);window.held.next().catch(()=>{});");
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && Volatile.Read(ref heldStarted) < 2)
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(heldStarted == 2, "new navigation has an independent streaming generation");
        window.Page.Dispose();
        deadline.Restart();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5) && Volatile.Read(ref heldDisposed) < 2)
            app.RunFor(TimeSpan.FromMilliseconds(20));
        check(heldDisposed == 2, "webview disposal cancels active producer and quiesces stream resources");
        check(!window.IsVisible && errors.Count == 0, "native streaming remains hidden and callbacks do not escape");
    }
    static async IAsyncEnumerable<int> Hold([EnumeratorCancellation] CancellationToken token, Action started, Action disposed)
    {
        try
        {
            started();
            await Task.Delay(Timeout.Infinite, token);
            yield return 0;
        }
        finally
        {
            disposed();
        }
    }
    static async IAsyncEnumerable<int> Range(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return i;
            await Task.Yield();
        }
    }
}
