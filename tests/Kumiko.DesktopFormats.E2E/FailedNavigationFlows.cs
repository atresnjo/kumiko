using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Web;

static class FailedNavigationFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Retained document navigation fixture", Hidden: true));
        using var window = app.NewPage(new(Visible: false), new(Assets:
            [Asset.Text("/index.html", "<!doctype html><html><body id='original'>Private retained DOM</body></html>")]));
        var page = window.Page!;
        var logs = new List<string>();
        var timeline = new List<string>();
        var assertions = new List<(string Name, bool Passed)>();
        var errors = new List<Exception>();
        page.Logged += value =>
        {
            logs.Add(value);
            timeline.Add(value);
        };
        app.UnhandledException += errors.Add;
        nint native = FindWebView(ObjC.O(window.NativeHandle, "contentView"));
        string Title() => ObjC.Text(ObjC.O(native, "title"));
        app.Services.Bind<int, int>("Failed", "echo", static value => value,
            StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        Pump(app, () => page.IsReady);
        page.Evaluate("kumiko.call('Failed.echo',1).then(v=>kumiko.log('initial:'+v))");
        Pump(app, () => logs.Contains("initial:1"));
        bool rejected = false;
        try
        {
            page.Load("file:///tmp/unsupported-kumiko-fixture");
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        page.Evaluate("document.title=document.getElementById('original')?'rejected-alive':'missing';kumiko.call('Failed.echo',2).then(v=>kumiko.log('after-rejected:'+v))");
        Pump(app, () => logs.Contains("after-rejected:2"));
        assertions.Add(("R2-P3 rejected Load keeps live DOM and existing ready bridge", rejected && page.IsReady && Title() == "rejected-alive" && logs.Contains("after-rejected:2")));
        var notice = new NanoEvent<int>("failed-review", StreamJson.Default.Int32, "number");
        foreach (string mode in new[] { "refused", "stopped", "superseded", "fragment", "empty", "identical", "history", "hostfragment", "pendinghistory", "pendinghash" })
            Navigate(mode);
        assertions.Add(("R2-P3 failure callbacks stay hidden and exception-contained", !window.IsVisible && errors.Count == 0));
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r2-navigation.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "actual hidden WKWebView, private local assets/loopback sockets, native title independent of bridge; no OS input");
            json.WriteStartArray("checks");
            foreach (var item in assertions)
            {
                json.WriteStartObject();
                json.WriteString("name", item.Name);
                json.WriteBoolean("passed", item.Passed);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("timeline");
            foreach (string value in timeline)
                json.WriteStringValue(value);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        foreach (var item in assertions)
            check(item.Passed, item.Name);

        void Navigate(string mode)
        {
            page.Load("/index.html");
            Pump(app, () => page.IsReady);
            bool fragment = mode is "fragment" or "empty" or "identical" or "history" or "hostfragment" or "pendinghistory" or "pendinghash";
            string target = mode is "history" or "pendinghistory" ? "/index.html?state=4#superseding" : mode == "empty" ? "/index.html#" :
                mode is "identical" or "pendinghash" ? "/index.html#identical" : "/index.html#superseding";
            string expectedHash = mode == "empty" ? "" : mode is "identical" or "pendinghash" ? "#identical" : "#superseding";
            if (mode is "identical" or "pendinghash")
            {
                page.Load(target);
                app.RunFor(TimeSpan.FromMilliseconds(60));
            }
            if (mode == "history")
                page.Evaluate("history.replaceState({},'', '/index.html?state=4#before')");
            page.Evaluate($"window.retained='{mode}';document.title='before:{mode}'");
            Pump(app, () => Title() == "before:" + mode);
            logs.Clear();
            timeline.Add("mode:" + mode);
            var old = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int started = 0, cancelled = 0, oldDisposed = 0, newDisposed = 0, streams = 0, oldFinished = 0;
            string prefix = fragment ? "R4-P1 " : "R2-P3 ";
            string service = "Fail" + mode;
            app.Services.BindAsync<int, int>(service, "held", async (ctx, value) =>
            {
                Interlocked.Increment(ref started);
                using var registration = ctx.CancellationToken.Register(() => Interlocked.Increment(ref cancelled));
                await (value == 1 ? old.Task : fresh.Task);
                if (value == 1)
                {
                    notice.Emit(window, 111);
                    Interlocked.Increment(ref oldFinished);
                }
                return value * 111;
            }, StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
            app.Services.BindStream<int, int>(service, "stream", (ctx, value) => Stream(value, fresh.Task, ctx.CancellationToken,
                () => Interlocked.Increment(ref streams), () =>
                {
                    if (value == 1)
                        Interlocked.Increment(ref oldDisposed);
                    else
                        Interlocked.Increment(ref newDisposed);
                }), StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
            page.Evaluate($"kumiko.on('failed-review',v=>kumiko.log('event:'+v));kumiko.call('{service}.held',1).catch(()=>{{}});window.oldStream=kumiko.stream('{service}.stream',1);oldStream.next().then(v=>kumiko.log('old-first:'+v.value)).catch(()=>{{}})");
            Pump(app, () => Volatile.Read(ref started) == 1 && logs.Contains("old-first:1"));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Task<TcpClient>? accepted = mode == "refused" ? null : listener.AcceptTcpClientAsync();
            if (mode == "refused")
                listener.Stop();
            if (mode == "hostfragment")
                page.Load($"http://127.0.0.1:{port}/");
            else
                page.Evaluate($"location.assign('http://127.0.0.1:{port}/')");
            Pump(app, () => Volatile.Read(ref cancelled) == 1);
            if (accepted is not null)
                Pump(app, () => accepted.IsCompleted);
            using var client = accepted?.IsCompletedSuccessfully == true ? accepted.Result : null;
            if (mode == "stopped")
                ObjC.V(native, "stopLoading");
            if (mode == "superseded")
                page.Load("/index.html");
            if (mode is "pendinghistory" or "pendinghash")
            {
                page.Evaluate(mode == "pendinghistory" ? "history.replaceState({},'', '/index.html?state=4#during')" : "location.hash='during'");
                page.Evaluate("document.title=window.retained+':during'");
                Pump(app, () => Title() == mode + ":during");
            }
            if (fragment)
            {
                assertions.Add((prefix + mode + " starts a real held provisional load and invalidates old ownership",
                    client is not null && !page.IsReady && cancelled == 1));
                page.Load(target);
            }
            Pump(app, () => page.IsReady);
            string expectedTitle = fragment ? mode + ":" + expectedHash : mode + "-alive";
            page.Evaluate(fragment ? "document.title=window.retained+':'+location.hash" :
                $"document.title=document.getElementById('original')?'{mode}-alive':'missing'");
            Pump(app, () => Title() == expectedTitle);
            timeline.Add("title:" + Title() + ";ready:" + page.IsReady + ";old-disposed:" + Volatile.Read(ref oldDisposed));
            assertions.Add((prefix + mode + " retains a usable fresh DOM generation and cancels old work",
                Title() == expectedTitle && page.IsReady && Volatile.Read(ref oldDisposed) == 1 && Volatile.Read(ref cancelled) == 1));
            page.Evaluate($"kumiko.on('failed-review',v=>kumiko.log('fresh-event:'+v));kumiko.call('{service}.held',2).then(v=>kumiko.log('fresh-result:'+v));window.freshStream=kumiko.stream('{service}.stream',2);freshStream.next().then(v=>kumiko.log('fresh-stream:'+v.value)).catch(e=>kumiko.log('fresh-stream-error:'+e.message))");
            Pump(app, () => Volatile.Read(ref started) == 2 && Volatile.Read(ref streams) == 2);
            if (mode != "superseded")
                page.Evaluate("oldStream.return().catch(()=>{})");
            old.TrySetResult();
            Pump(app, () => Volatile.Read(ref oldFinished) == 1);
            if (fragment && client is not null)
            {
                try
                {
                    const string body = "<html><title>obsolete-response</title></html>";
                    byte[] response = System.Text.Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body);
                    var write = client.GetStream().WriteAsync(response).AsTask();
                    Pump(app, () => write.IsCompleted);
                    write.GetAwaiter().GetResult();
                    timeline.Add(mode + ":old-response-sent");
                }
                catch (Exception error) when (error is IOException or SocketException)
                {
                    timeline.Add(mode + ":old-response-abandoned");
                }
                client.Dispose();
            }
            app.RunFor(TimeSpan.FromMilliseconds(100));
            if (fragment)
                assertions.Add((prefix + mode + " abandoned native response cannot replace or retire recovered DOM",
                    page.IsReady && Title() == expectedTitle && Volatile.Read(ref newDisposed) == 0));
            assertions.Add((prefix + mode + " old replies/events/generator cleanup cannot act on fresh IDs",
                Volatile.Read(ref newDisposed) == 0 && !logs.Any(s => s.StartsWith("fresh-result:", StringComparison.Ordinal) || s.Contains("event:111", StringComparison.Ordinal))));
            fresh.TrySetResult();
            Pump(app, () => logs.Contains("fresh-result:222") && logs.Contains("fresh-stream:2"));
            assertions.Add((prefix + mode + " fresh unary and stream calls complete after recovery",
                logs.Contains("fresh-result:222") && logs.Contains("fresh-stream:2") && !logs.Any(s => s.StartsWith("fresh-stream-error:", StringComparison.Ordinal))));
            page.Evaluate("freshStream.return().catch(()=>{})");
            Pump(app, () => Volatile.Read(ref newDisposed) == 1);
        }
    }
    static void Pump(App app, Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(3))
            app.RunFor(TimeSpan.FromMilliseconds(20));
    }
    static async IAsyncEnumerable<int> Stream(int value, Task fresh, [EnumeratorCancellation] CancellationToken token, Action started, Action disposed)
    {
        started();
        try
        {
            if (value == 2)
                await fresh.WaitAsync(token);
            yield return value;
        }
        finally
        {
            disposed();
        }
    }
    static nint FindWebView(nint view)
    {
        if (ObjC.B(view, "isKindOfClass:", ObjC.Class("WKWebView")) != 0)
            return view;
        nint children = ObjC.O(view, "subviews");
        for (nuint i = 0; i < ObjC.N(children, "count"); i++)
        {
            nint result = FindWebView(ObjC.O(children, "objectAtIndex:", i));
            if (result != 0)
                return result;
        }
        return 0;
    }
}
