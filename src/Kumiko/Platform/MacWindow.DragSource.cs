using System.Runtime.InteropServices;
using Kumiko.Desktop;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacWindow
{
    MacDragSource? dragSource;
    public void StartDrag(TransferData data, Rect bounds)
    {
        var previous = dragSource;
        dragSource = null;
        previous?.Dispose();
        dragSource = new(window.App, data, bounds, MacDragSource.Session(view));
    }
}

/// <summary>
/// One drag session. The snapshot cache, fresh unbound pasteboard writers and the native source
/// share its lifetime: the session's own dragging pasteboard adopts the writers, nothing is written
/// to a private pasteboard first, and ending or cancelling the session never fetches lazy data.
/// </summary>
internal sealed unsafe class MacDragSource : IDisposable
{
    static readonly Dictionary<nint, MacDragSource> instances = new();
    readonly App app;
    readonly TransferSnapshot snapshot;
    readonly MacPasteboardWriters writers;
    readonly nint source;
    bool disposed;

    internal static int Live => instances.Count;
    internal nint Source => source;

    // begin receives an NSArray of NSDraggingItems and the NSDraggingSource. release runs once on
    // the App thread when the session ends or fails to start.
    internal MacDragSource(App app, TransferData data, Rect bounds, Action<nint, nint> begin, Action? release = null)
    {
        app.CheckThread();
        this.app = app;
        using var pool = new AutoreleasePool();
        snapshot = new(app, data, release);
        try
        {
            writers = new(app, snapshot);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }

        var cls = O.Define("KumikoDragSource", "NSObject", ("draggingSession:sourceOperationMaskForDraggingContext:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nuint>)&Operations, "Q@:@q"), ("draggingSession:endedAtPoint:operation:", (nint)(delegate* unmanaged<nint, nint, nint, NativePoint, nuint, void>)&Ended, "v@:@{CGPoint=dd}Q"));
        O.class_addProtocol(cls, O.objc_getProtocol("NSDraggingSource"));
        source = O.O(cls, "new");
        instances.Add(source, this);
        try
        {
            var items = O.O(O.Class("NSMutableArray"), "array");
            var fresh = writers.Items;
            for (nuint i = 0; i < O.N(fresh, "count"); i++)
            {
                var item = O.O(O.O(O.Class("NSDraggingItem"), "alloc"), "initWithPasteboardWriter:", O.O(fresh, "objectAtIndex:", i));
                var image = O.O(O.Class("NSImage"), "imageNamed:", O.TempString("NSMultipleDocuments"));
                O.V(item, "setDraggingFrame:contents:", new NativeRect(bounds.X, bounds.Y, Math.Max(24, Math.Min(64, bounds.Width)), 32), image);
                O.V(items, "addObject:", item);
                O.Release(item);
            }

            begin(items, source);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The production endpoint: AppKit adopts the writers into its dragging pasteboard.</summary>
    internal static Action<nint, nint> Session(nint view) => (items, source) =>
    {
        var ev = O.O(O.O(O.Class("NSApplication"), "sharedApplication"), "currentEvent");
        if (ev == 0)
            throw new InvalidOperationException("A native drag requires a current pointer event.");
        O.O(view, "beginDraggingSessionWithItems:event:source:", items, ev, source);
    };

    [UnmanagedCallersOnly]
    static nuint Operations(nint self, nint cmd, nint session, nint context) => 1;
    [UnmanagedCallersOnly]
    static void Ended(nint self, nint cmd, nint session, NativePoint point, nuint operation)
    {
        if (!instances.TryGetValue(self, out var owner))
            return;
        try
        {
            owner.Dispose();
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    /// <summary>Releases providers without fetching, then the snapshot; the release hook may throw after cleanup.</summary>
    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        instances.Remove(source);
        try
        {
            writers.Dispose();
        }
        finally
        {
            O.Release(source);
            snapshot.Dispose();
        }
    }
}
