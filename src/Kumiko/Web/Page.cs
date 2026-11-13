using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Web;

public sealed record Asset(string Path, byte[] Data, string? Mime = null)
{
    public static Asset Text(string path, string text, string? mime = null) => new(path, Encoding.UTF8.GetBytes(text), mime);
}

public sealed record PageOptions(string Url = "/", Asset[]? Assets = null, bool Inspectable = true);
[JsonSerializable(typeof(string))]
internal partial class WebJson : JsonSerializerContext;
public sealed partial class Page : IDisposable
{
    static readonly Dictionary<nint, Page> handlers = new();
    readonly Window window;
    readonly PageOptions options;
    readonly nint webview, handler;
    readonly List<string> outbox = new(), held = new();
    bool scheduled, disposed;
    public bool IsReady
    {
        get; private set;
    }

    public event Action<string>? Logged;
    public event Action? Ready;
    internal bool UsesPersistentDataStore
    {
        get
        {
            window.App.CheckThread();
            if (adapter is not null)
                return !window.App.Options.Hidden;
            using var pool = new AutoreleasePool();
            return O.B(O.O(O.O(webview, "configuration"), "websiteDataStore"), "isPersistent") != 0;
        }
    }
    internal unsafe Page(Window window, PageOptions options, nint nativeWindow)
    {
        this.window = window;
        this.options = options;
        _ = FullUrl(options.Url);
        if (!OperatingSystem.IsMacOS())
        {
            adapter = OperatingSystem.IsWindows() ? new WindowsPageAdapter(this, nativeWindow) : new LinuxPageAdapter(this, nativeWindow);
            Load(options.Url);
            return;
        }
        NativeLibrary.Load("/System/Library/Frameworks/WebKit.framework/WebKit");
        using var pool = new AutoreleasePool();
        var cls = O.Define(
            "NanoCSharpPageHandler",
            "NSObject",
            ("userContentController:didReceiveScriptMessage:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Message, "v@:@@"),
            ("webView:startURLSchemeTask:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Serve, "v@:@@"),
            ("webView:didFailProvisionalNavigation:withError:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&NavigationFailed, "v@:@@@"),
            ("webView:didFailNavigation:withError:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&NavigationFailed, "v@:@@@"),
            ("webView:didStartProvisionalNavigation:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&NavigationStarted, "v@:@@"),
            ("webView:stopURLSchemeTask:", (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&Stop, "v@:@@")
        );
        handler = O.O(cls, "new");
        handlers[handler] = this;
        var config = O.New("WKWebViewConfiguration");
        try
        {
            if (window.App.Options.Hidden)
                O.V(config, "setWebsiteDataStore:", O.O(O.Class("WKWebsiteDataStore"), "nonPersistentDataStore"));
            var controller = O.O(config, "userContentController");
            var script = O.O(
                O.O(O.Class("WKUserScript"), "alloc"),
                "initWithSource:injectionTime:forMainFrameOnly:",
                O.TempString(Script),
                (nuint)0,
                (nuint)1
            );
            O.V(controller, "addUserScript:", script);
            O.Release(script);
            O.V(controller, "addScriptMessageHandler:name:", handler, O.TempString("kumiko"));
            O.V(config, "setURLSchemeHandler:forURLScheme:", handler, O.TempString("kumiko"));
            var content = O.O(nativeWindow, "contentView");
            webview = O.O(O.O(O.Class("WKWebView"), "alloc"), "initWithFrame:configuration:", O.R(content, "bounds"), config);
            O.V(webview, "setNavigationDelegate:", handler);
            O.V(webview, "setAutoresizingMask:", (nuint)18);
            if (options.Inspectable)
                O.V(webview, "setInspectable:", (byte)1);
            O.V(content, "addSubview:", webview);
            Load(options.Url);
        }
        finally
        {
            O.Release(config);
        }
    }

    public void Load(string url)
    {
        window.App.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        string full = FullUrl(url);
        bool sameDocument = SameDocumentTarget(full, out bool unchanged);
        bool restore = sameDocument && navigating;
        if (unchanged && !restore)
            return;
        if (adapter is not null)
        {
            if (!sameDocument)
                NativeNavigationStarted();
            try
            {
                if (restore)
                    StopRetainedNavigation();
                if (!unchanged)
                    adapter.Load(full);
                if (restore)
                    RecoverRetainedDocument(0);
            }
            catch
            {
                NativeNavigationFailed();
                throw;
            }
            return;
        }
        using var pool = new AutoreleasePool();
        var uri = O.O(O.Class("NSURL"), "URLWithString:", O.TempString(full));
        if (uri == 0)
            throw new ArgumentException("Invalid page URL.", nameof(url));
        var request = O.O(O.Class("NSURLRequest"), "requestWithURL:", uri);
        if (restore)
            StopRetainedNavigation();
        else if (!sameDocument)
            NativeNavigationStarted();
        if (!unchanged && O.O(webview, "loadRequest:", request) == 0)
            NativeNavigationFailed();
        if (restore)
            RecoverRetainedDocument(0);
    }

    public void Evaluate(string script)
    {
        window.App.CheckThread();
        if (disposed)
            return;
        if (adapter is not null)
        {
            adapter.Evaluate(script);
            return;
        }
        using var pool = new AutoreleasePool();
        O.V(webview, "evaluateJavaScript:completionHandler:", O.TempString(script), (nint)0);
    }

    internal void Enqueue(string json, bool hold)
    {
        if (disposed || (!hold && documentIdentity is null))
            return;
        if (hold && !IsReady)
        {
            if (held.Count < 1024)
                held.Add(json);
            return;
        }

        outbox.Add(json);
        if (scheduled)
            return;
        scheduled = true;
        long generation = epoch;
        string? identity = documentIdentity;
        window.App.Dispatch(() =>
        {
            if (generation != epoch || identity != documentIdentity)
                return;
            scheduled = false;
            if (disposed || outbox.Count == 0)
                return;
            var script = "__kumiko.receive(" + JsonSerializer.Serialize(identity, WebJson.Default.String) + ",[" + string.Join(',', outbox) + "])";
            outbox.Clear();
            Evaluate(script);
        });
    }

    [UnmanagedCallersOnly]
    static void Message(nint self, nint cmd, nint controller, nint message)
    {
        if (!handlers.TryGetValue(self, out var page))
            return;
        try
        {
            var frame = O.O(message, "frameInfo");
            if (O.B(frame, "isMainFrame") == 0)
                return;
            var source = O.Text(O.O(O.O(O.O(frame, "request"), "URL"), "absoluteString"));
            if (!page.Allows(source))
                return;
            var body = O.O(message, "body");
            if (O.B(body, "isKindOfClass:", O.Class("NSString")) == 0)
                return;
            page.Receive(O.Text(body));
        }
        catch (Exception ex)
        {
            page.window.App.Report(ex);
        }
    }

    void Receive(string body)
    {
        if (body.Length > 1024 * 1024)
            throw new InvalidDataException("IPC envelope exceeds 1 MiB.");
        using var doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        var env = doc.RootElement;
        if (!env.TryGetProperty("nonce", out var key) || key.GetString() != nonce)
            return;
        var type = env.GetProperty("t").GetString();
        if (!env.TryGetProperty("document", out var document) || document.ValueKind != JsonValueKind.String)
            return;
        string? identity = document.GetString();
        if (type == "document-start")
        {
            string? root = env.TryGetProperty("root", out var sourceRoot) ? sourceRoot.GetString() : null;
            string? recovery = env.TryGetProperty("recovery", out var ticket) ? ticket.GetString() : null;
            AcceptDocument(identity, root, recovery);
            return;
        }
        if (identity != documentIdentity || identity is null)
            return;
        switch (type)
        {
            case "dom-ready":
                IsReady = true;
                foreach (var message in held)
                    Enqueue(message, false);
                held.Clear();
                Ready?.Invoke();
                break;
            case "log":
                var text = env.GetProperty("v").GetString() ?? "";
                Logged?.Invoke(text);
                Console.WriteLine("page: " + text);
                break;
            case "call":
                var id = env.GetProperty("id").GetInt64();
                var method = env.GetProperty("m").GetString() ?? "";
                var args = env.GetProperty("a");
                if (args.GetArrayLength() != 1)
                {
                    Reply(id, null, "Expected one argument object.");
                    break;
                }

                var argument = args[0].Clone();
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, navigation.Token);
                long generation = epoch;
                _ = Call(id, method, argument, generation, cancellation);
                break;
            case "stream-open":
                long streamId = env.GetProperty("id").GetInt64();
                try
                {
                    StreamOpen(streamId, env.GetProperty("m").GetString() ?? "", env.GetProperty("a"));
                }
                catch (Exception error)
                {
                    Enqueue("{\"t\":\"stream-item\",\"id\":" + streamId + ",\"error\":" + JsonSerializer.Serialize(error.Message, WebJson.Default.String) + "}", false);
                }
                break;
            case "stream-next":
                StreamNext(env.GetProperty("id").GetInt64());
                break;
            case "stream-cancel":
                StreamCancel(env.GetProperty("id").GetInt64());
                break;
            case "win":
                switch (env.GetProperty("op").GetString())
                {
                    case "close":
                        window.Close();
                        break;
                    case "minimize":
                        window.Minimize();
                        break;
                    case "zoom":
                        window.ToggleMaximize();
                        break;
                    case "title":
                        window.SetTitle(env.GetProperty("v").GetString() ?? "");
                        break;
                }

                break;
            case "drag":
                window.BeginMove();
                break;
            case "dblclick":
                window.ToggleMaximize();
                break;
        }
    }

    async Task Call(long id, string method, JsonElement argument, long generation, CancellationTokenSource cancellation)
    {
        using (cancellation)
        using (EnterCall(generation))
        {
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                string result = await window.App.Services.InvokeAsync(method, argument, new(window, cancellation.Token)).ConfigureAwait(false);
                PostReply(id, result, null, generation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                PostReply(id, null, error.Message, generation);
            }
        }
    }
    void PostReply(long id, string? result, string? error, long generation)
    {
        try
        {
            window.App.Dispatch(() =>
            {
                if (!disposed && generation == epoch)
                    Reply(id, result, error);
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void Reply(
        long id,
        string? result,
        string? error
    ) => Enqueue(
        "{\"t\":\"reply\",\"id\":" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + (error is null ? ",\"ok\":true,\"v\":" + result : ",\"ok\":false,\"e\":" + JsonSerializer.Serialize(error, WebJson.Default.String)) + "}",
        false
    );
    [UnmanagedCallersOnly]
    static void Serve(nint self, nint cmd, nint webview, nint task)
    {
        if (!handlers.TryGetValue(self, out var page))
            return;
        try
        {
            page.Serve(task);
        }
        catch (Exception ex)
        {
            page.window.App.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static void Stop(nint self, nint cmd, nint webview, nint task)
    {
    }

    unsafe void Serve(nint task)
    {
        using var pool = new AutoreleasePool();
        var url = O.O(O.O(task, "request"), "URL");
        var path = O.Text(O.O(url, "path"));
        if (path is "" or "/")
            path = "/index.html";
        byte[]? data = null;
        string mime = Mime(path);
        if (path == "/kumiko.js")
            data = Encoding.UTF8.GetBytes(window.App.Services.JavaScript());
        else if (options.Assets?.FirstOrDefault(a => a.Path == path) is { } asset)
        {
            data = asset.Data;
            mime = asset.Mime ?? mime;
        }

        nint key = O.TempString("Content-Type"), value = O.TempString(mime);
        var headers = O.O(O.Class("NSDictionary"), "dictionaryWithObjects:forKeys:count:", (nint)(&value), (nint)(&key), (nuint)1);
        var response = O.O(
            O.O(O.Class("NSHTTPURLResponse"), "alloc"),
            "initWithURL:statusCode:HTTPVersion:headerFields:",
            url,
            (nuint)(data is null ? 404 : 200),
            O.TempString("HTTP/1.1"),
            headers
        );
        data ??= Encoding.UTF8.GetBytes("Not Found");
        fixed (byte* p = data)
        {
            var bytes = O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)data.Length);
            O.V(task, "didReceiveResponse:", response);
            O.V(task, "didReceiveData:", bytes);
            O.V(task, "didFinish");
        }

        O.Release(response);
    }

    static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" => "image/jpeg",
        ".woff2" => "font/woff2",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream"
    };
    public void Dispose()
    {
        window.App.CheckThread();
        if (disposed)
            return;
        disposed = true;
        lifetime.Cancel();
        CancelStreams();
        navigation.Dispose();
        lifetime.Dispose();
        if (adapter is not null)
        {
            adapter.Dispose();
            outbox.Clear();
            held.Clear();
            return;
        }
        handlers.Remove(handler);
        var controller = O.O(O.O(webview, "configuration"), "userContentController");
        O.V(controller, "removeScriptMessageHandlerForName:", O.TempString("kumiko"));
        O.V(webview, "setNavigationDelegate:", (nint)0);
        O.V(webview, "stopLoading");
        O.V(webview, "removeFromSuperview");
        O.Release(webview);
        O.Release(handler);
        outbox.Clear();
        held.Clear();
    }
}
