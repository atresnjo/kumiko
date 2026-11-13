using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

/// <summary>Private blocking native loop so signal destroy notifications do not depend on the App loop during shutdown.</summary>
internal sealed unsafe class GioSignalContext : IDisposable
{
    readonly Thread thread;
    readonly Action cleanup;
    readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    nint context;
    nint loop;
    GCHandle handle;
    Exception? error;
    int disposed;
    internal GioSignalContext(Action setup, Action cleanup)
    {
        this.cleanup = cleanup;
        thread = new(() => Run(setup))
        {
            IsBackground = true,
            Name = "Kumiko power signals"
        };
        thread.Start();
        ready.Task.GetAwaiter().GetResult(); // Only called by the D-Bus worker, never the App thread.
    }
    void Run(Action setup)
    {
        context = g_main_context_new();
        loop = g_main_loop_new(context, 0);
        g_main_context_push_thread_default(context);
        handle = GCHandle.Alloc(this);
        try
        {
            try
            {
                setup();
                ready.TrySetResult();
            }
            catch (Exception failure)
            {
                error = failure;
                Queue(&Stop, 0);
            }
            g_main_loop_run(loop);
        }
        finally
        {
            g_main_context_pop_thread_default(context);
            g_main_loop_unref(loop);
            g_main_context_unref(context);
            handle.Free();
            if (error is not null)
                ready.TrySetException(error);
        }
    }
    void Queue(delegate* unmanaged<nint, int> callback, int priority)
    {
        // Attach a real source: invoke_full would execute inline on the context thread and bypass the drain barrier.
        nint source = g_idle_source_new();
        g_source_set_priority(source, priority);
        g_source_set_callback(source, callback, GCHandle.ToIntPtr(handle), 0);
        g_source_attach(source, context);
        g_source_unref(source);
    }
    [UnmanagedCallersOnly]
    static int Stop(nint data)
    {
        var owner = (GioSignalContext)GCHandle.FromIntPtr(data).Target!;
        try
        {
            owner.cleanup();
        }
        catch (Exception failure)
        {
            owner.error = failure;
        }
        finally
        {
            // GIO guarantees a lower-priority idle after unsubscribe runs after subscription resource destruction.
            owner.Queue(&Quit, 200);
        }
        return 0;
    }
    [UnmanagedCallersOnly]
    static int Quit(nint data)
    {
        g_main_loop_quit(((GioSignalContext)GCHandle.FromIntPtr(data).Target!).loop);
        return 0;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        Queue(&Stop, 0);
        if (!thread.Join(TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Native power signal cleanup did not finish.");
        if (error is not null)
            throw new InvalidOperationException("Native power signal cleanup failed.", error);
    }
    [DllImport(SecretVariant.GLib)]
    static extern nint g_main_context_new();
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_context_push_thread_default(nint context);
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_context_pop_thread_default(nint context);
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_context_unref(nint context);
    [DllImport(SecretVariant.GLib)]
    static extern nint g_main_loop_new(nint context, int running);
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_loop_run(nint loop);
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_loop_quit(nint loop);
    [DllImport(SecretVariant.GLib)]
    static extern void g_main_loop_unref(nint loop);
    [DllImport(SecretVariant.GLib)]
    static extern nint g_idle_source_new();
    [DllImport(SecretVariant.GLib)]
    static extern void g_source_set_priority(nint source, int priority);
    [DllImport(SecretVariant.GLib)]
    static extern void g_source_set_callback(nint source, delegate* unmanaged<nint, int> callback, nint data, nint destroy);
    [DllImport(SecretVariant.GLib)]
    static extern uint g_source_attach(nint source, nint context);
    [DllImport(SecretVariant.GLib)]
    static extern void g_source_unref(nint source);
}
