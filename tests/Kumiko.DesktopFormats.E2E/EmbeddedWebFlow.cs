using System.Text.Json;
using Kumiko;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;
using Kumiko.Web;

static class EmbeddedWebFlow
{
    internal static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        using var app = new App(new(Name: "Embedded web fixture", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        bool slot = false, overlay = false;
        using var window = app.NewWindow(new(Width: 600, Height: 400, Visible: false), c =>
        {
            using var root = c.Div(key: "page").SizeFull().Padding(20).Gap(12);
            c.Text("Embedded browser host", key: "title");
            if (slot)
                c.WebView("preview", 420, 240, "slot");
            if (overlay)
            {
                using var popup = c.Overlay("cover");
                c.Text("Framework overlay", key: "overlay-label");
            }
        });
        using var page = window.NewWebView("preview", new(Assets: [Asset.Text("/index.html",
            "<!doctype html><html><body><script>kumiko.log('embedded-ready')</script></body></html>")]));
        var logs = new List<string>();
        page.Logged += logs.Add;
        slot = true;
        window.Draw();
        nint host = ObjC.O(window.NativeHandle, "contentView");
        nint native = FindWebView(host);
        nuint children = ObjC.N(ObjC.O(host, "subviews"), "count");
        try
        {
            using var invalid = window.NewWebView("invalid", new(Url: "file:///untrusted-document.html"));
            check(false, "untrusted file URL rejected");
        }
        catch (ArgumentException)
        {
            check(ObjC.N(ObjC.O(host, "subviews"), "count") == children,
                "invalid browser URL is rejected before allocating a native child");
        }
        check(native != 0 && ObjC.B(native, "isHidden") == 0, "hidden native host embeds a visible child without showing its window");
        var node = window.Context!.Nodes.ToArray().Single(n => n.Key == "slot");
        var frame = ObjC.R(native, "frame");
        check(Math.Abs(frame.Origin.X - node.Rect.X) < .1 && Math.Abs(frame.Origin.Y - node.Rect.Y) < .1 &&
            Math.Abs(frame.Size.Width - node.Rect.Width) < .1 && Math.Abs(frame.Size.Height - node.Rect.Height) < .1,
            "embedded WKWebView native frame follows its declarative layout slot");
        check(!page.UsesPersistentDataStore, "hidden embedded browser uses ephemeral website storage");
        bool wrongThreadRejected = Task.Run(() =>
        {
            try
            {
                page.Dispose();
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }).GetAwaiter().GetResult();
        check(wrongThreadRejected && ObjC.O(native, "superview") == host,
            "worker disposal rejects before touching the owner-thread native browser");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(10) && !logs.Contains("embedded-ready"))
            app.RunFor(TimeSpan.FromMilliseconds(30));
        check(logs.Contains("embedded-ready"), "embedded native browser executes local assets and typed bridge bootstrap");
        File.WriteAllText(Path.Combine(output, "embedded-web.txt"), window.Context.DumpTree());
        Png.Write(Path.Combine(output, "embedded-web.png"), CpuRenderer.Render(window.Context.Instances,
            window.Context.TextSystem.Atlas, window.Context.Size, window.Context.Scale, window.Context.Theme.Background));
        overlay = true;
        window.Draw();
        check(ObjC.B(native, "isHidden") != 0, "overlapping framework overlay hides the native child surface");
        overlay = false;
        slot = false;
        window.Draw();
        check(ObjC.B(native, "isHidden") != 0, "removing pure layout reference hides the owned browser");
        ObjC.V(native, "retain");
        try
        {
            page.Dispose();
            check(ObjC.O(native, "superview") == 0 && !window.IsVisible && errors.Count == 0,
                "embedded browser disposal detaches native view with no callback errors or visible window");
        }
        finally
        {
            ObjC.Release(native);
        }
        using var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "embedded-web.json")), new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("boundary", "native hidden WKWebView geometry/lifetime; PNG is framework placeholder, excludes browser pixels");
        json.WriteNumber("x", frame.Origin.X);
        json.WriteNumber("y", frame.Origin.Y);
        json.WriteNumber("width", frame.Size.Width);
        json.WriteNumber("height", frame.Size.Height);
        json.WriteBoolean("overlayHidden", true);
        json.WriteBoolean("removedSlotHidden", true);
        json.WriteBoolean("disposedDetached", true);
        json.WriteEndObject();
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
