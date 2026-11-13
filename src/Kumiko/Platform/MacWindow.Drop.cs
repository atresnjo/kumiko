using System.Runtime.InteropServices;
using Kumiko.UI;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

/// <summary>Files dragged in from Finder and other apps, as NSDraggingDestination on the view.</summary>
internal sealed unsafe partial class MacWindow
{
    /// <summary>File paths on a dragging pasteboard; other URL and data types are ignored.</summary>
    static string[] DraggedFiles(nint info)
    {
        var pasteboard = O.O(info, "draggingPasteboard");
        var classes = O.O(O.Class("NSArray"), "arrayWithObject:", O.Class("NSURL"));
        var urls = O.O(pasteboard, "readObjectsForClasses:options:", classes, (nint)0);
        if (urls == 0)
            return [];
        var files = new List<string>();
        for (nuint i = 0; i < O.N(urls, "count"); i++)
        {
            var url = O.O(urls, "objectAtIndex:", i);
            if (O.B(url, "isFileURL") != 0 && O.Text(O.O(url, "path")) is { Length: > 0 } path)
                files.Add(path);
        }
        return [.. files];
    }

    static Point Location(MacWindow w, nint info)
    {
        var p = O.P(w.view, "convertPoint:fromView:", O.P(info, "draggingLocation"), 0);
        return new((float)p.X, (float)p.Y);
    }

    [UnmanagedCallersOnly]
    static nuint DragUpdated(nint self, nint cmd, nint info)
    {
        nuint operation = 0;
        Guard(self, w =>
        {
            using var pool = new AutoreleasePool();
            var files = DraggedFiles(info);
            var point = Location(w, info);
            w.window.Input(i =>
            {
                i.Pointer = point;
                i.DragFiles = files.Length > 0 ? files : null;
            });
            operation = files.Length > 0 ? 1u : 0u;
        });
        return operation;
    }

    [UnmanagedCallersOnly]
    static void DragExited(nint self, nint cmd, nint info) => Guard(self, w => w.window.Input(i => i.DragFiles = null));

    [UnmanagedCallersOnly]
    static byte PerformDrag(nint self, nint cmd, nint info)
    {
        byte accepted = 0;
        Guard(self, w =>
        {
            using var pool = new AutoreleasePool();
            var files = DraggedFiles(info);
            var point = Location(w, info);
            w.window.Input(i =>
            {
                i.Pointer = point;
                i.DragFiles = null;
                i.DroppedFiles = files.Length > 0 ? files : null;
            });
            accepted = files.Length > 0 ? (byte)1 : (byte)0;
        });
        return accepted;
    }
}
