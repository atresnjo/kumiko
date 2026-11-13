using System.Runtime.CompilerServices;

namespace Kumiko.UI;

internal sealed record WebViewContent(string Id);
public sealed partial class Context
{
    /// <summary>Pure layout reference to Window.NewWebView. Tester renders its placeholder and never creates a browser.
    /// Rectangular clipping is supported; intersecting native-UI overlays temporarily hide the browser surface.</summary>
    public void WebView(string id, float width, float height, NodeKey key, string label = "Web content",
        [CallerFilePath] string source = "", [CallerLineNumber] int line = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        using var box = Div(key, source, line).Width(width).Height(height).Clip().Background(Theme.Background)
            .Semantics(Role.Group, label).Native(new WebViewContent(id));
    }
}
