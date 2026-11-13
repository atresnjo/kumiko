using System.Diagnostics;
using System.Runtime.InteropServices;
using Kumiko.Web;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed unsafe partial class MacApp : IAppBackend
{
    const string DispatchLib = "/usr/lib/system/libdispatch.dylib";
    readonly nint app;
    readonly App owner;
    static readonly nint mainQueue = NativeLibrary.GetExport(NativeLibrary.Load(DispatchLib), "_dispatch_main_q");
    [DllImport(DispatchLib)]
    static extern void dispatch_async_f(nint queue, nint context, delegate* unmanaged<nint, void> callback);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    static extern int pthread_main_np();
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern int CFRunLoopRunInMode(nint mode, double seconds, byte returnAfterSource);
    internal MacApp(App owner, bool awaitLaunchEvent = false)
    {
        if (pthread_main_np() == 0)
            throw new InvalidOperationException("AppKit must be initialized on the process main thread.");
        this.owner = owner;
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        using var pool = new AutoreleasePool();
        app = O.O(O.Class("NSApplication"), "sharedApplication");
        O.V(app, "setActivationPolicy:", (nuint)(owner.Options.Hidden ? 2 : (int)owner.Options.ActivationPolicy));
        InitializeLaunchProvenance(awaitLaunchEvent);
        InstallDelegate();
        O.V(app, "finishLaunching");
        InstallOpenEvents();
    }

    public IWindowBackend CreateWindow(Window window, PageOptions? page) => new MacWindow(window, page);
    public void Dispatch(Action action)
    {
        var handle = GCHandle.Alloc((owner, action));
        dispatch_async_f(mainQueue, GCHandle.ToIntPtr(handle), &Invoke);
    }

    [UnmanagedCallersOnly]
    static void Invoke(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        var (owner, action) = ((App, Action))handle.Target!;
        handle.Free();
        using var pool = new AutoreleasePool();
        try
        {
            action();
        }
        catch (Exception e)
        {
            owner.Report(e);
        }
    }

    public void Run()
    {
        using var pool = new AutoreleasePool();
        O.V(app, "run");
    }

    public void RunFor(TimeSpan duration)
    {
        using var pool = new AutoreleasePool();
        var mode = O.TempString("kCFRunLoopDefaultMode");
        var stop = Stopwatch.StartNew();
        do
        {
            CFRunLoopRunInMode(mode, Math.Max(0, (duration - stop.Elapsed).TotalSeconds), 0);
        }
        while (stop.Elapsed < duration);
    }

    public void Quit()
    {
        O.V(app, "stop:", (nint)0);
        O.V(app, "postEvent:atStart:", O.WakeEvent(O.Class("NSEvent")), (nuint)1);
    }

    public void Activate() => O.V(app, "activateIgnoringOtherApps:", (byte)1);
    public void Dispose() => DisposeDelegate();
}
