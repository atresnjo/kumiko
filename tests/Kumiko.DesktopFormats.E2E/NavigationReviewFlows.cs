using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kumiko;
using Kumiko.RichText;
using Kumiko.Web;
using Minimal = Kumiko.Examples.RichEditor;

static class NavigationReviewFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Navigation review fixture", Hidden: true));
        using var program = Minimal.EditorApp.CreateProgram();
        const string html = """
            <!doctype html><html><body><a id="next" href="/next.html">Next</a><script>
            kumiko.on('review',p=>kumiko.log('event:'+p));
            kumiko.log('ready:'+location.pathname);
            </script></body></html>
            """;
        using var window = app.NewPage(new(Visible: false), new(Assets:
            [Asset.Text("/index.html", html), Asset.Text("/next.html", html)]), program);
        var page = window.Page!;
        var logs = new List<string>();
        var timeline = new List<string>();
        var errors = new List<Exception>();
        var assertions = new List<(string Name, bool Passed)>();
        page.Logged += text =>
        {
            logs.Add(text);
            timeline.Add(text);
        };
        app.UnhandledException += errors.Add;
        app.Services.Bind<int, int>("Owner", "sync", value =>
        {
            window.SetTitle("Hidden synchronous service");
            program.Send(new Minimal.EditorMessage.Edit(new RichCommand.Insert("service")));
            program.ProcessMessages();
            return value;
        }, StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        app.Services.BindAsync<int, int>("Owner", "async", async (_, value) =>
        {
            window.SetTitle("Hidden asynchronous service prefix");
            program.Send(new Minimal.EditorMessage.Edit(new RichCommand.Insert("prefix")));
            program.ProcessMessages();
            await Task.Yield();
            return value;
        }, StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        Pump(app, () => page.IsReady);
        page.Evaluate("Promise.all([kumiko.call('Owner.sync',1),kumiko.call('Owner.async',2)]).then(()=>kumiko.log('owner-ok')).catch(e=>kumiko.log('owner-error:'+e.message))");
        Pump(app, () => logs.Any(s => s.StartsWith("owner-", StringComparison.Ordinal)));
        assertions.Add(("P6 synchronous and asynchronous service prefixes own native UI and typed message processing",
            logs.Contains("owner-ok") && program.Model.Editor.Value.PlainText.Contains("service", StringComparison.Ordinal) &&
            program.Model.Editor.Value.PlainText.Contains("prefix", StringComparison.Ordinal)));
        var notice = new NanoEvent<int>("review", StreamJson.Default.Int32, "number");
        app.Services.RegisterEvent(notice);
        foreach (string mode in new[] { "location", "link", "reload", "host", "fragment" })
            Navigate(mode);
        assertions.Add(("P5 native browser navigation stays hidden and native callbacks contain exceptions", !window.IsVisible && errors.Count == 0));
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-navigation.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "actual hidden WKWebView, local assets, simulated service/stream gates, no OS input");
            json.WriteStartArray("checks");
            foreach (var item in assertions)
            {
                json.WriteStartObject();
                json.WriteString("name", item.Name);
                json.WriteBoolean("passed", item.Passed);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("logs");
            foreach (string log in timeline)
                json.WriteStringValue(log);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        foreach (var item in assertions)
            check(item.Passed, item.Name);

        void Navigate(string mode)
        {
            timeline.Add("navigation:" + mode);
            page.Load("/index.html");
            Pump(app, () => page.IsReady);
            logs.Clear();
            var old = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int started = 0, finished = 0, cancelled = 0, streams = 0, disposed = 0;
            string service = "Review" + mode;
            app.Services.BindAsync<int, int>(service, "held", async (ctx, value) =>
            {
                Interlocked.Increment(ref started);
                using var registration = ctx.CancellationToken.Register(() => Interlocked.Increment(ref cancelled));
                await (value == 1 ? old.Task : fresh.Task);
                if (value == 1)
                    notice.Emit(window, 111);
                Interlocked.Increment(ref finished);
                return value * 111;
            }, StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
            app.Services.BindStream<int, int>(service, "heldStream", (ctx, _) => Hold(ctx.CancellationToken,
                () => Interlocked.Increment(ref streams), () => Interlocked.Increment(ref disposed)),
                StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
            app.Services.BindStream<int, int>(service, "once", static (_, _) => Once(),
                StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
            page.Evaluate($"kumiko.call('{service}.held',1).catch(()=>{{}});window.held=kumiko.stream('{service}.heldStream',0);window.held.next().catch(()=>{{}})");
            Pump(app, () => Volatile.Read(ref started) == 1 && Volatile.Read(ref streams) == 1);
            if (mode is "host" or "fragment")
            {
                notice.Emit(window, 333);
                page.Load(mode == "fragment" ? "/next.html#cross" : "/next.html");
            }
            else
                page.Evaluate(mode switch
                {
                    "location" => "location.assign('/next.html')",
                    "link" => "document.getElementById('next').click()",
                    _ => "location.reload()"
                });
            Pump(app, () => page.IsReady && logs.Any(s => s.StartsWith("ready:", StringComparison.Ordinal)));
            app.RunFor(TimeSpan.FromMilliseconds(60));
            assertions.Add(("P5 " + mode + " cancels old unary and stream work before new requests",
                Volatile.Read(ref disposed) == 1 && Volatile.Read(ref cancelled) == 1));
            page.Evaluate($"kumiko.call('{service}.held',2).then(v=>kumiko.log('new-result:'+v)).catch(e=>kumiko.log('new-error:'+e.message));(async()=>{{try{{for await(const v of kumiko.stream('{service}.once',0))kumiko.log('new-stream:'+v)}}catch(e){{kumiko.log('new-stream-error:'+e.message)}}}})()");
            Pump(app, () => Volatile.Read(ref started) == 2);
            old.TrySetResult();
            Pump(app, () => Volatile.Read(ref finished) >= 1);
            app.RunFor(TimeSpan.FromMilliseconds(80));
            assertions.Add(("P5 " + mode + " rejects late replies and events from the previous document",
                !logs.Any(s => s.StartsWith("new-result:", StringComparison.Ordinal) || s is "event:111" or "event:333")));
            fresh.TrySetResult();
            Pump(app, () => logs.Any(s => s.StartsWith("new-result:", StringComparison.Ordinal)));
            assertions.Add(("P5 " + mode + " allows fresh unary and stream IDs to be reused safely",
                logs.Contains("new-result:222") && logs.Contains("new-stream:42") && !logs.Any(s => s.StartsWith("new-stream-error", StringComparison.Ordinal))));
            assertions.Add(("P5 " + mode + " disposes the old stream without a new client cleanup request", Volatile.Read(ref disposed) == 1));
        }
    }
    static void Pump(App app, Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
            app.RunFor(TimeSpan.FromMilliseconds(20));
        if (!done())
            throw new TimeoutException("Hidden navigation fixture timed out.");
    }
    static async IAsyncEnumerable<int> Hold([EnumeratorCancellation] CancellationToken token, Action started, Action disposed)
    {
        started();
        try
        {
            await Task.Delay(Timeout.Infinite, token);
            yield return 0;
        }
        finally
        {
            disposed();
        }
    }
    static async IAsyncEnumerable<int> Once()
    {
        yield return 42;
        await Task.Yield();
    }
}
