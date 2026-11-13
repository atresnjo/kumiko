using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Web;

static class SameDocumentFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Same-document navigation fixture", Hidden: true));
        const string html = """
            <!doctype html><html><body id='private'>Private document<script>
            window.sentinel='same-dom';
            const title=()=>document.title=sentinel+':'+location.pathname+location.search+location.hash;
            addEventListener('hashchange',title);addEventListener('popstate',title);title();
            </script></body></html>
            """;
        using var window = app.NewPage(new(Visible: false), new(Url: "/index.html", Assets: [Asset.Text("/index.html", html)]));
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
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0, cancelled = 0, disposed = 0, streams = 0;
        app.Services.BindAsync<int, int>("Same", "held", async (ctx, value) =>
        {
            Interlocked.Increment(ref started);
            using var registration = ctx.CancellationToken.Register(() => Interlocked.Increment(ref cancelled));
            await release.Task;
            return value;
        }, StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        app.Services.Bind<int, int>("Same", "echo", static value => value,
            StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        app.Services.BindStream<int, int>("Same", "stream", (ctx, value) => Held(value, release.Task, ctx.CancellationToken,
            () => Interlocked.Increment(ref streams), () => Interlocked.Increment(ref disposed)),
            StreamJson.Default.Int32, StreamJson.Default.Int32, "number", "number");
        Pump(app, () => page.IsReady);
        page.Evaluate("kumiko.call('Same.held',111).then(v=>kumiko.log('held:'+v)).catch(()=>kumiko.log('held-cancelled'));window.live=kumiko.stream('Same.stream',222);live.next().then(v=>kumiko.log('first:'+v.value)).catch(()=>{});window.sentinel='retained'");
        Pump(app, () => Volatile.Read(ref started) == 1 && logs.Contains("first:0"));
        page.Evaluate("live.next().then(v=>kumiko.log('second:'+v.value)).catch(()=>kumiko.log('stream-cancelled'))");
        int sequence = 0;
        Step("host-add", () => page.Load("/index.html#host-one"), "/index.html#host-one");
        Step("host-change", () => page.Load("/index.html#host-two"), "/index.html#host-two");
        Step("host-empty", () => page.Load("/index.html#"), "/index.html");
        Step("script-hash", () => page.Evaluate("location.hash='script'"), "/index.html#script");
        Step("push-state", () => page.Evaluate("history.pushState({},'', '/index.html?state=1#push')"), "/index.html?state=1#push");
        Step("host-after-push", () => page.Load("/index.html?state=1#host"), "/index.html?state=1#host");
        Step("replace-state", () => page.Evaluate("history.replaceState({},'', '/index.html?state=2#replace')"), "/index.html?state=2#replace");
        Step("host-after-replace", () => page.Load("/index.html?state=2#host"), "/index.html?state=2#host");
        Step("history-back", () => page.Evaluate("history.back()"), "/index.html?state=2#replace");
        Step("history-forward", () => page.Evaluate("history.forward()"), "/index.html?state=2#host");
        Step("same-fragment", () => page.Load("/index.html?state=2#host"), "/index.html?state=2#host");
        release.TrySetResult();
        Pump(app, () => logs.Contains("held:111") && logs.Contains("second:222"));
        assertions.Add(("R3-P3 original unary/stream complete through same-document navigation", logs.Contains("held:111") && logs.Contains("second:222") && cancelled == 0));
        page.Evaluate("live.return().catch(()=>{})");
        Pump(app, () => Volatile.Read(ref disposed) == 1);
        assertions.Add(("R3-P3 preserved stream is disposed once by its owner", disposed == 1 && streams == 1 && !window.IsVisible && errors.Count == 0));
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r3-same-document.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "actual hidden WKWebView, local assets/private services, native title independent of bridge, no OS input");
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

        void Step(string name, Action navigate, string location)
        {
            navigate();
            page.Evaluate("document.title=window.sentinel+':'+location.pathname+location.search+location.hash");
            Pump(app, () => Title() == "retained:" + location);
            int id = ++sequence;
            page.Evaluate($"kumiko.call('Same.echo',{id}).then(v=>kumiko.log('echo:'+v)).catch(()=>{{}})");
            Pump(app, () => logs.Contains("echo:" + id));
            timeline.Add(name + ";title:" + Title() + ";ready:" + page.IsReady + ";cancelled:" + cancelled + ";disposed:" + disposed);
            assertions.Add(("R3-P3 " + name + " preserves DOM bridge and active ownership", Title() == "retained:" + location &&
                page.IsReady && logs.Contains("echo:" + id) && cancelled == 0 && disposed == 0 && streams == 1));
        }
    }
    static void Pump(App app, Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(2))
            app.RunFor(TimeSpan.FromMilliseconds(20));
    }
    static async IAsyncEnumerable<int> Held(int value, Task release, [EnumeratorCancellation] CancellationToken token, Action started, Action disposed)
    {
        started();
        try
        {
            yield return 0;
            await release.WaitAsync(token);
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
