using System.Runtime.InteropServices;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

/// <summary>
/// Display-synchronized frames from the view's CADisplayLink (macOS 14 and later). The link follows the screen
/// the window is on, including 120 Hz ProMotion panels, and stays paused unless a frame was requested, so an idle
/// window keeps blocking in the native event loop.
/// </summary>
internal sealed unsafe partial class MacWindow
{
    nint displayLink;
    bool linkRunning, linkUnavailable;
    long requested;

    /// <summary>
    /// The run-loop mode set that keeps frames coming during live resize, menu tracking and scroller drags.
    /// CFRunLoop recognises it by identity, so it must be the exported constant: an equal string names a
    /// separate mode that never runs.
    /// </summary>
    static readonly nint CommonModes = *(nint*)NativeLibrary.GetExport(
        NativeLibrary.Load("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation"), "kCFRunLoopCommonModes");

    public bool RequestFrame()
    {
        if (disposed || renderer is null || window.App.Options.Hidden || linkUnavailable || !IsVisible)
            return false;
        // An occluded window would not be refreshed; the fallback timer keeps its state advancing.
        if ((O.N(Handle, "occlusionState") & 2) == 0)
            return false;
        if (displayLink == 0 && !CreateDisplayLink())
            return false;
        if (!linkRunning)
        {
            O.V(displayLink, "setPaused:", (byte)0);
            linkRunning = true;
            requested = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        else if (System.Diagnostics.Stopwatch.GetElapsedTime(requested).TotalMilliseconds > 250)
        {
            // A link that stops delivering frames must never strand input: draw directly from now on.
            DisposeDisplayLink();
            linkUnavailable = true;
            return false;
        }
        return true;
    }

    bool CreateDisplayLink()
    {
        if (O.B(view, "respondsToSelector:", O.Sel("displayLinkWithTarget:selector:")) == 0)
        {
            linkUnavailable = true;
            return false;
        }
        var link = O.O(view, "displayLinkWithTarget:selector:", view, O.Sel("kumikoDisplayFrame:"));
        if (link == 0)
        {
            linkUnavailable = true;
            return false;
        }
        displayLink = O.O(link, "retain");
        O.V(displayLink, "setPaused:", (byte)1);
        O.V(displayLink, "addToRunLoop:forMode:", O.O(O.Class("NSRunLoop"), "mainRunLoop"), CommonModes);
        return true;
    }

    [UnmanagedCallersOnly]
    static void DisplayFrame(nint self, nint cmd, nint link) => Guard(self, w =>
    {
        if (w.linkRunning)
        {
            O.V(w.displayLink, "setPaused:", (byte)1);
            w.linkRunning = false;
        }
        w.window.DisplayFrame();
    });

    /// <summary>The link retains the view as its target; invalidating it breaks that cycle.</summary>
    void DisposeDisplayLink()
    {
        if (displayLink == 0)
            return;
        O.V(displayLink, "invalidate");
        O.Release(displayLink);
        displayLink = 0;
        linkRunning = false;
    }
}
