using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed partial class MacWindow
{
    int nativeOperations;

    // Native callers may still be unwinding after a managed subscriber closes the
    // window. Keep Objective-C receivers alive through the enclosing AppKit pool,
    // and defer renderer/native teardown until our outermost operation returns.
    NativeScope NativeOperation(bool callback = false) => new(this, callback);

    readonly struct NativeScope : IDisposable
    {
        readonly MacWindow owner;
        readonly AutoreleasePool? pool;
        internal NativeScope(MacWindow owner, bool callback)
        {
            this.owner = owner;
            pool = owner.nativeOperations == 0 && !callback ? new AutoreleasePool() : null;
            if (owner.nativeOperations++ != 0)
                return;
            O.O(O.O(owner.Handle, "retain"), "autorelease");
            O.O(O.O(owner.view, "retain"), "autorelease");
            O.O(O.O(owner.windowDelegate, "retain"), "autorelease");
            if (owner.window.Options.Parent is { } parent)
                O.O(O.O(parent.NativeHandle, "retain"), "autorelease");
        }

        public void Dispose()
        {
            try
            {
                if (--owner.nativeOperations == 0 && owner.disposed)
                    owner.DisposeNative();
            }
            finally
            {
                pool?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        instances.Remove(Handle);
        instances.Remove(view);
        instances.Remove(windowDelegate);
        O.V(Handle, "setDelegate:", (nint)0);
        if (nativeOperations == 0)
            DisposeNative();
    }

    void DisposeNative()
    {
        if (observing)
            O.V(O.O(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "notificationCenter"), "removeObserver:", windowDelegate);
        try
        {
            DisposeShell();
        }
        finally
        {
            try
            {
                DisposeDisplayLink();
                accessibility?.Dispose();
                O.V(Handle, "close");
                renderer?.Dispose();
            }
            finally
            {
                O.Release(view);
                O.Release(windowDelegate);
                O.Release(Handle);
            }
        }
    }
}
