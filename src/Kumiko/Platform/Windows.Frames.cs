using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kumiko.Platform;

/// <summary>
/// Display-synchronized frames, as CADisplayLink paces macOS. A worker waits for the next DWM composition only
/// while a frame is requested and posts <see cref="FrameMessage"/>; otherwise it blocks on an event, so an idle
/// window keeps blocking in GetMessageW.
/// </summary>
internal sealed unsafe partial class WindowsWindow
{
    // WM_APP + 0x10. WindowsApp claims 0x8001 for thread dispatch whatever the target window.
    const uint FrameMessage = 0x8010;
    FramePacer? pacer;
    bool pacerUnavailable;

    /// <summary>Frames delivered through <see cref="FrameMessage"/>, for verification.</summary>
    internal int PacedFrames
    {
        get; private set;
    }

    public bool RequestFrame()
    {
        // A hidden or minimized window is not composed; the caller's timer keeps its state advancing.
        if (disposed || window.App.Options.Hidden || !IsVisible || IsMinimized)
            return false;
        return Pace();
    }

    /// <summary>Requests one composition-paced frame without the visibility checks.</summary>
    internal bool Pace()
    {
        if (disposed || pacerUnavailable)
            return false;
        if (DwmIsCompositionEnabled(out var composed) < 0 || composed == 0)
            return false;
        if (pacer is null)
        {
            try
            {
                pacer = new(handle, FrameMessage);
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ThreadStartException)
            {
                pacerUnavailable = true;
                return false;
            }
        }
        if (pacer.Request())
            return true;
        // A worker that failed or stopped delivering must never strand input: draw directly from now on.
        DisposePacer();
        pacerUnavailable = true;
        return false;
    }

    void DeliverFrame()
    {
        pacer?.Delivered();
        PacedFrames++;
        window.DisplayFrame();
    }

    void DisposePacer()
    {
        pacer?.Dispose();
        pacer = null;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmIsCompositionEnabled(out int enabled);
}

/// <summary>
/// The frame worker. The UI thread sets <c>requested</c> and wakes it; the worker waits for a composition, posts
/// one message, and the window clears <c>requested</c> when it handles it, so a burst of requests is one frame.
/// The worker never waits on the UI thread, so disposing it cannot deadlock.
/// </summary>
internal sealed unsafe class FramePacer : IDisposable
{
    // DCompositionWaitForCompositorClock (dcomp.h, Windows 11 build 22000): WAIT_OBJECT_0 + count is a clock tick.
    static readonly delegate* unmanaged[Stdcall]<uint, nint*, uint, uint> waitForClock = LoadClock();
    readonly nint window;
    readonly uint message;
    readonly AutoResetEvent wake = new(false);
    readonly ManualResetEvent stop = new(false);
    readonly WaitHandle[] handles;
    readonly Thread thread;
    int requested;
    volatile bool failed;
    long requestedAt;
    bool disposed;

    internal FramePacer(nint window, uint message)
    {
        this.window = window;
        this.message = message;
        handles = [wake, stop];
        thread = new(Run)
        {
            IsBackground = true,
            Name = "Kumiko frame pacing"
        };
        thread.Start();
    }

    /// <summary>UI thread. False once the worker failed or a request went 250 ms without a frame.</summary>
    internal bool Request()
    {
        if (failed || disposed)
            return false;
        if (Interlocked.Exchange(ref requested, 1) == 0)
        {
            requestedAt = Stopwatch.GetTimestamp();
            wake.Set();
            return true;
        }
        return Stopwatch.GetElapsedTime(requestedAt).TotalMilliseconds <= 250;
    }

    /// <summary>UI thread, on the posted message: the next request starts another frame.</summary>
    internal void Delivered() => Volatile.Write(ref requested, 0);

    void Run()
    {
        var clock = waitForClock;
        var stopHandle = stop.SafeWaitHandle.DangerousGetHandle();
        while (WaitHandle.WaitAny(handles) == 0)
        {
            if (clock != null)
            {
                uint result = clock(1, &stopHandle, 100);
                if (result == 0)
                    return;
                // 1 is a tick and 0x102 a timeout; a frame then follows anyway, at the backstop's pace or faster.
                // Other results (the display is off, or the call failed) pace on DwmFlush from now on.
                if (result is not (1 or 0x102))
                {
                    clock = null;
                    if (DwmFlush() < 0)
                        break;
                }
            }
            else if (DwmFlush() < 0)
                break;
            if (stop.WaitOne(0))
                return;
            if (PostMessageW(window, message, 0, 0) == 0)
                break;
        }
        failed = !stop.WaitOne(0);
    }

    /// <summary>UI thread. Stops and joins the worker; its longest wait is one composition or 100 ms.</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        stop.Set();
        // The events are only released once the worker has exited and can no longer wait on them.
        if (!thread.Join(TimeSpan.FromSeconds(1)))
            return;
        wake.Dispose();
        stop.Dispose();
    }

    static delegate* unmanaged[Stdcall]<uint, nint*, uint, uint> LoadClock()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !NativeLibrary.TryLoad("dcomp.dll", out var library))
            return null;
        return NativeLibrary.TryGetExport(library, "DCompositionWaitForCompositorClock", out var export)
            ? (delegate* unmanaged[Stdcall]<uint, nint*, uint, uint>)export
            : null;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmFlush();
    [DllImport("user32.dll")]
    static extern int PostMessageW(nint window, uint message, nuint w, nint l);
}
