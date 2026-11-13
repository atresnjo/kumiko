using System.Runtime.InteropServices;
using Kumiko.Audio;
using Kumiko.UI;

namespace Kumiko.Web;

/// <summary>Direct WebView2 COM adapter; requires WebView2 Runtime and the matching WebView2Loader.dll.</summary>
internal sealed unsafe class WindowsPageAdapter : IPageAdapter
{
    [StructLayout(LayoutKind.Sequential)]
    struct Rectangle
    {
        internal int Left, Top, Right, Bottom;
    }
    readonly Page page;
    readonly nint window;
    readonly List<MediaComHandler> handlers = [];
    nint environment, controller, webview;
    long messageToken, resourceToken, navigationToken, completedToken;
    string? pendingUrl;
    string? profile;
    Rect bounds;
    bool visible = true, disposed;
    static nint Slot(nint obj, int index) => (*(nint**)obj)[index];
    static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    static void Retain(nint obj) => ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 1))(obj);
    static void Release(nint obj)
    {
        if (obj != 0)
            ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);
    }
    static nint Property(nint obj, int index)
    {
        nint value;
        Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(obj, index))(obj, &value));
        return value;
    }
    static string Text(nint obj, int index)
    {
        nint value = Property(obj, index);
        try
        {
            return Marshal.PtrToStringUni(value) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(value);
        }
    }
    MediaComHandler Handler(string id, Action<nint, nint> callback)
    {
        var handler = new MediaComHandler(new Guid(id), (a, b) =>
        {
            if (disposed)
                return;
            try
            {
                callback(a, b);
            }
            catch (Exception error)
            {
                page.NativeError(error);
            }
        });
        handlers.Add(handler);
        return handler;
    }
    internal WindowsPageAdapter(Page page, nint window)
    {
        this.page = page;
        this.window = window;
        bounds = new(0, 0, page.Owner.Options.Width, page.Owner.Options.Height);
        // Every hidden instance has a fresh temporary profile, never the user's browser profile.
        profile = page.Owner.App.Options.Hidden ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kumiko-webview-" + Guid.NewGuid().ToString("N")) :
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kumiko", "WebView2", page.Owner.App.Options.Name);
        var ready = Handler("4e8a3389-c9d8-4bd2-b6b5-124fee6cc14d", (result, value) =>
        {
            Check((int)result);
            environment = value;
            Retain(value);
            var created = Handler("6c4819f3-c9b7-4260-8127-c9f5bde7f68c", ControllerReady);
            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint, int>)Slot(environment, 3))(environment, window, created.Handle));
        });
        try
        {
            Check(CreateCoreWebView2EnvironmentWithOptions(null, profile, 0, ready.Handle));
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    void ControllerReady(nint result, nint value)
    {
        Check((int)result);
        controller = value;
        Retain(value);
        webview = Property(controller, 25);
        var messages = Handler("57213f19-00e6-49fa-8e07-898ea01ecbd2", (_, args) =>
        {
            string source = Text(args, 3), body = Text(args, 5);
            page.NativeMessage(body, source.StartsWith("https://kumiko.invalid/", StringComparison.Ordinal) ? "kumiko://localhost/" + source[23..] : source);
        });
        long token;
        Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(webview, 34))(webview, messages.Handle, &token));
        messageToken = token;
        var navigation = Handler("9adbe429-f36d-432b-9ddc-f8881fbd76e3", (_, args) =>
        {
            long id;
            Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(args, 9))(args, &id));
            page.NativeNavigationStarted(id);
        });
        Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(webview, 7))(webview, navigation.Handle, &token));
        navigationToken = token;
        var completed = Handler("d33a35bf-1c49-4f98-93ab-006e0533fe1c", (_, args) =>
        {
            int success;
            long id;
            Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(args, 3))(args, &success));
            Check(((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(args, 5))(args, &id));
            if (success == 0)
                page.NativeNavigationFailed(id);
        });
        Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(webview, 15))(webview, completed.Handle, &token));
        completedToken = token;
        var resources = Handler("ab00b74c-15f1-4646-80e8-e76341d25d71", (_, args) => Serve(args));
        Check(((delegate* unmanaged[Stdcall]<nint, nint, long*, int>)Slot(webview, 55))(webview, resources.Handle, &token));
        resourceToken = token;
        fixed (char* filter = "https://kumiko.invalid/*")
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Slot(webview, 57))(webview, filter, 0));
        var scripted = Handler("b99369f3-9b11-47b5-bc6f-8e7895fcea17", (status, _) =>
        {
            Check((int)status);
            Place(bounds, visible);
            if (pendingUrl is { } url)
                Load(url);
        });
        fixed (char* script = page.Script)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Slot(webview, 27))(webview, script, scripted.Handle));
    }
    void Serve(nint args)
    {
        nint request = Property(args, 3);
        string url;
        try
        {
            url = Text(request, 3);
        }
        finally
        {
            Release(request);
        }
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Host != "kumiko.invalid")
            return;
        var asset = page.FindAsset(uri.AbsolutePath);
        byte[] data = asset?.Data ?? "Not found"u8.ToArray();
        nint stream;
        fixed (byte* pointer = data)
            stream = SHCreateMemStream(pointer, (uint)data.Length);
        if (stream == 0)
            throw new OutOfMemoryException();
        nint response = 0;
        try
        {
            string mime = asset?.Mime ?? (uri.AbsolutePath.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript" : "text/html");
            string headers = "Content-Type: " + mime + "\r\nX-Content-Type-Options: nosniff\r\n";
            fixed (char* reason = asset is null ? "Not Found" : "OK")
            fixed (char* nativeHeaders = headers)
            {
                nint created;
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int, char*, char*, nint*, int>)Slot(environment, 4))(environment,
                    stream, asset is null ? 404 : 200, reason, nativeHeaders, &created));
                response = created;
            }
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(args, 5))(args, response));
        }
        finally
        {
            Release(response);
            Release(stream);
        }
    }
    public string Url
    {
        get
        {
            string source = webview == 0 ? "" : Text(webview, 4);
            return source.StartsWith("https://kumiko.invalid/", StringComparison.Ordinal) ? "kumiko://localhost/" + source[23..] : source;
        }
    }
    public void Stop()
    {
        if (webview != 0)
            Check(((delegate* unmanaged[Stdcall]<nint, int>)Slot(webview, 43))(webview));
    }
    public void Load(string url)
    {
        pendingUrl = url;
        if (webview == 0)
            return;
        string target = url.StartsWith("kumiko://localhost/", StringComparison.Ordinal) ? "https://kumiko.invalid/" + url[19..] : url;
        fixed (char* text = target)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Slot(webview, 5))(webview, text));
    }
    public void Evaluate(string script)
    {
        if (webview == 0)
            return;
        fixed (char* text = script)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, nint, int>)Slot(webview, 29))(webview, text, 0));
    }
    public void Place(Rect bounds, bool visible)
    {
        this.bounds = bounds;
        this.visible = visible;
        if (controller == 0)
            return;
        float scale = GetDpiForWindow(window) / 96f;
        var rect = new Rectangle { Left = (int)(bounds.X * scale), Top = (int)(bounds.Y * scale), Right = (int)(bounds.Right * scale), Bottom = (int)(bounds.Bottom * scale) };
        Check(((delegate* unmanaged[Stdcall]<nint, Rectangle, int>)Slot(controller, 6))(controller, rect));
        Check(((delegate* unmanaged[Stdcall]<nint, int, int>)Slot(controller, 4))(controller, visible ? 1 : 0));
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (webview != 0)
        {
            ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(webview, 8))(webview, navigationToken);
            ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(webview, 16))(webview, completedToken);
            ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(webview, 35))(webview, messageToken);
            ((delegate* unmanaged[Stdcall]<nint, long, int>)Slot(webview, 56))(webview, resourceToken);
        }
        if (controller != 0)
            ((delegate* unmanaged[Stdcall]<nint, int>)Slot(controller, 24))(controller);
        Release(webview);
        Release(controller);
        Release(environment);
        foreach (var handler in handlers)
            handler.Dispose();
        handlers.Clear();
        // WebView2 processes can retain this profile until they exit. The host owns deferred cleanup.
    }
    [DllImport("WebView2Loader.dll", CharSet = CharSet.Unicode)]
    static extern int CreateCoreWebView2EnvironmentWithOptions(string? browserFolder, string? profile, nint options, nint completed);
    [DllImport("shlwapi.dll")] static extern nint SHCreateMemStream(byte* data, uint length);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint window);
}
