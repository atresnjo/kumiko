using System.Security.Cryptography;
using System.Text.Json;
using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Web;

internal interface IPageAdapter : IDisposable
{
    string Url
    {
        get;
    }
    void Load(string url);
    void Stop();
    void Evaluate(string script);
    void Place(Rect bounds, bool visible);
}

public sealed partial class Page
{
    readonly IPageAdapter? adapter;
    readonly string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    internal string Script => Bridge.Source.Replace("__KUMIKO_NONCE__", JsonSerializer.Serialize(nonce, WebJson.Default.String), StringComparison.Ordinal)
        .Replace("__KUMIKO_PLATFORM__", OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux", StringComparison.Ordinal);
    internal Window Owner => window;
    internal PageOptions Options => options;
    internal bool Allows(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;
        var allowed = new Uri(FullUrl(options.Url));
        return uri.Scheme == allowed.Scheme && uri.Host == allowed.Host && uri.Port == allowed.Port;
    }
    internal static string FullUrl(string url)
    {
        string full = url.Contains("://", StringComparison.Ordinal) ? url : "kumiko://localhost/" + url.TrimStart('/');
        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "kumiko") || uri.UserInfo.Length > 0)
            throw new ArgumentException("Web pages require http(s) URLs or local Kumiko assets.", nameof(url));
        return full;
    }
    internal Asset? FindAsset(string path)
    {
        if (path is "" or "/")
            path = "/index.html";
        return path == "/kumiko.js" ? Asset.Text(path, window.App.Services.JavaScript(), "text/javascript; charset=utf-8") : options.Assets?.FirstOrDefault(a => a.Path == path);
    }
    internal void NativeMessage(string body, string source)
    {
        window.App.CheckThread();
        if (!disposed && Allows(source))
            Receive(body);
    }
    internal void NativeError(Exception error) => window.App.Report(error);
    internal void Place(Rect bounds, bool visible)
    {
        window.App.CheckThread();
        if (disposed)
            return;
        if (adapter is not null)
        {
            adapter.Place(bounds, visible);
            return;
        }
        using var pool = new AutoreleasePool();
        ObjC.V(webview, "setAutoresizingMask:", (nuint)0);
        ObjC.V(webview, "setFrame:", new NativeRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        ObjC.V(webview, "setHidden:", (byte)(visible ? 0 : 1));
    }
}
