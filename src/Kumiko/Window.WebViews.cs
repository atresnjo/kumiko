using Kumiko.UI;
using Kumiko.Web;

namespace Kumiko;

public sealed partial class Window
{
    internal nint LinuxWebLayer;
    readonly Dictionary<string, Page> webViews = new(StringComparer.Ordinal);
    /// <summary>Creates an explicitly owned embedded browser outside View/Update. A pure view refers to it by ID.
    /// Dispose the page to stop it; closing the window disposes every owned page.</summary>
    public Page NewWebView(string id, PageOptions options)
    {
        App.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (Context is null || webViews.ContainsKey(id))
            throw new InvalidOperationException("Embedded webviews require a native-UI window and a unique ID.");
        var page = new Page(this, options, NativeHandle);
        webViews.Add(id, page);
        page.Place(new(0, 0, 1, 1), false);
        Invalidate();
        return page;
    }
    void PresentWebViews()
    {
        if (Context is null || webViews.Count == 0)
            return;
        var nodes = Context.Nodes.ToArray();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.NativeContent is not WebViewContent web)
                continue;
            if (!placed.Add(web.Id))
                throw new InvalidOperationException("A webview can be placed only once per window.");
            if (!webViews.TryGetValue(web.Id, out var page))
                throw new InvalidOperationException("Create the webview in the window host before referencing its ID.");
            Rect rect = node.Rect.Intersect(node.ClipRect);
            bool covered = nodes.Any(n => n.IsOverlay && n.Rect.Intersect(rect) is { Width: > 0, Height: > 0 });
            // Native child surfaces cannot share the GPU z-buffer. Hide them while a native-UI overlay intersects.
            page.Place(rect, !node.LayoutHidden && !covered && rect.Width > 0 && rect.Height > 0);
        }
        foreach (var (id, page) in webViews)
            if (!placed.Contains(id))
                page.Place(new(0, 0, 1, 1), false);
    }
}
