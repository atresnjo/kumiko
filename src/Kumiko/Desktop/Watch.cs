using System.Diagnostics;
using System.Runtime.InteropServices;
using Kumiko.Platform;

namespace Kumiko.Desktop;

public sealed unsafe class Watch : IDisposable
{
    readonly App app;
    readonly Action handler;
    nint source;
    uint glibSource;
    GCHandle handle;
    bool disposed;
    Process? process;
    const string Dispatch = "/usr/lib/system/libdispatch.dylib";
    [DllImport(Dispatch)]
    static extern nint dispatch_source_create(nint type, nuint value, nuint mask, nint queue);
    [DllImport(Dispatch)]
    static extern void dispatch_set_context(nint source, nint context);
    [DllImport(Dispatch)]
    static extern void dispatch_source_set_event_handler_f(nint source, delegate* unmanaged<nint, void> handler);
    [DllImport(Dispatch)]
    static extern void dispatch_source_set_cancel_handler_f(nint source, delegate* unmanaged<nint, void> handler);
    [DllImport(Dispatch)]
    static extern void dispatch_resume(nint source);
    [DllImport(Dispatch)]
    static extern void dispatch_source_cancel(nint source);
    [DllImport(Dispatch)]
    static extern void dispatch_release(nint source);
    [DllImport(Gtk.GLib)]
    static extern uint g_unix_fd_add_full(
        int priority,
        int fd,
        uint condition,
        delegate* unmanaged<int, uint, nint, int> callback,
        nint data,
        delegate* unmanaged<nint, void> destroy
    );
    Watch(App app, Action handler)
    {
        this.app = app;
        this.handler = handler;
    }

    public static Watch Readable(App app, int descriptor, Action handler)
    {
        app.CheckThread();
        var watch = new Watch(app, handler);
        watch.handle = GCHandle.Alloc(watch);
        if (OperatingSystem.IsMacOS())
        {
            var lib = NativeLibrary.Load(Dispatch);
            var kind = NativeLibrary.GetExport(lib, "_dispatch_source_type_read");
            var queue = NativeLibrary.GetExport(lib, "_dispatch_main_q");
            watch.source = dispatch_source_create(kind, (nuint)descriptor, 0, queue);
            if (watch.source == 0)
            {
                watch.handle.Free();
                throw new IOException("Could not watch the file descriptor.");
            }

            dispatch_set_context(watch.source, GCHandle.ToIntPtr(watch.handle));
            dispatch_source_set_event_handler_f(watch.source, &OnRead);
            dispatch_source_set_cancel_handler_f(watch.source, &OnCancel);
            dispatch_resume(watch.source);
        }
        else if (OperatingSystem.IsLinux())
            watch.glibSource = g_unix_fd_add_full(0, descriptor, 1 | 8 | 16, &OnUnixRead, GCHandle.ToIntPtr(watch.handle), &OnCancel);
        else
        {
            watch.handle.Free();
            throw new PlatformNotSupportedException("POSIX descriptor watches require macOS or Linux. Use Stream.ReadAsync on Windows.");
        }

        return watch;
    }

    public static Watch Exited(App app, int processId, Action handler)
    {
        var watch = new Watch(app, handler);
        watch.process = Process.GetProcessById(processId);
        watch.process.EnableRaisingEvents = true;
        watch.process.Exited += watch.ProcessExited;
        if (watch.process.HasExited)
            watch.ProcessExited(watch.process, EventArgs.Empty);
        return watch;
    }

    int notified;
    void ProcessExited(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref notified, 1) == 0 && !disposed)
            app.Dispatch(() =>
            {
                if (!disposed)
                    handler();
            });
    }

    [UnmanagedCallersOnly]
    static void OnRead(nint context)
    {
        var watch = (Watch)GCHandle.FromIntPtr(context).Target!;
        if (watch.disposed)
            return;
        try
        {
            watch.handler();
        }
        catch (Exception ex)
        {
            watch.app.Report(ex);
        }
    }

    [UnmanagedCallersOnly]
    static int OnUnixRead(int fd, uint condition, nint context)
    {
        var watch = (Watch)GCHandle.FromIntPtr(context).Target!;
        if (watch.disposed)
            return 0;
        try
        {
            watch.handler();
        }
        catch (Exception ex)
        {
            watch.app.Report(ex);
        }

        return watch.disposed ? 0 : 1;
    }

    [UnmanagedCallersOnly]
    static void OnCancel(nint context)
    {
        var handle = GCHandle.FromIntPtr(context);
        handle.Free();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (source != 0)
        {
            dispatch_source_cancel(source);
            dispatch_release(source);
            source = 0;
        }

        if (glibSource != 0)
        {
            Gtk.g_source_remove(glibSource);
            glibSource = 0;
        }

        if (process is not null)
        {
            process.Exited -= ProcessExited;
            process.Dispose();
        }
    }
}
