using System.Runtime.InteropServices;

namespace Kumiko.Platform;
/// <summary>Copyable Objective-C block with an explicitly retained managed callback context.</summary>
internal sealed unsafe class NativeBlock : IDisposable
{
    const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    static readonly nint library = NativeLibrary.Load(SystemLibrary);
    static readonly nint stackClass = NativeLibrary.GetExport(library, "_NSConcreteStackBlock");
    static readonly nint descriptor = CreateDescriptor();
    nint block;
    [StructLayout(LayoutKind.Sequential)]
    struct Layout
    {
        public nint Class;
        public int Flags, Reserved;
        public nint Invoke, Descriptor, Context;
    }

    static nint CreateDescriptor()
    {
        var values = (nint*)NativeMemory.Alloc((nuint)(4 * sizeof(nint)));
        values[0] = 0;
        values[1] = sizeof(Layout);
        values[2] = (nint)(delegate* unmanaged<Layout*, Layout*, void>)&Copy;
        values[3] = (nint)(delegate* unmanaged<Layout*, void>)&Release;
        return (nint)values; // Process-lifetime immutable block ABI descriptor.
    }

    NativeBlock(object callback, nint invoke)
    {
        var handle = GCHandle.Alloc(callback);
        var layout = new Layout
        {
            Class = stackClass,
            Flags = 1 << 25,
            Invoke = invoke,
            Descriptor = descriptor,
            Context = GCHandle.ToIntPtr(handle)
        };
        try
        {
            block = _Block_copy((nint)(&layout));
        }
        finally
        {
            handle.Free();
        }

        if (block == 0)
            throw new OutOfMemoryException();
    }

    internal nint Handle => block;

    internal static NativeBlock ResultPointer(Func<nint, nint> callback) => new(callback, (nint)(delegate* unmanaged<Layout*, nint, nint>)&ResultInvoked);
    [UnmanagedCallersOnly]
    static nint ResultInvoked(Layout* block, nint value)
    {
        try
        {
            return ((Func<nint, nint>)GCHandle.FromIntPtr(block->Context).Target!)(value);
        }
        catch
        {
            return 200;
        }
    }

    internal static NativeBlock Pointer(Action<nint> callback) => new(callback, (nint)(delegate* unmanaged<Layout*, nint, void>)&PointerInvoked);
    [UnmanagedCallersOnly]
    static void PointerInvoked(Layout* block, nint value)
    {
        try
        {
            ((Action<nint>)GCHandle.FromIntPtr(block->Context).Target!)(value);
        }
        catch
        {
            // Native owners dispatch guarded callbacks; exceptions never cross the block ABI.
        }
    }

    internal static NativeBlock Permission(Action<bool, string?> callback) => new(callback, (nint)(delegate* unmanaged<Layout*, byte, nint, void>)&PermissionInvoked);
    internal static NativeBlock Completion(Action<string?> callback) => new(callback, (nint)(delegate* unmanaged<Layout*, nint, void>)&CompletionInvoked);
    internal static NativeBlock ApplicationLaunch(Action<string?> callback) => new(callback, (nint)(delegate* unmanaged<Layout*, nint, nint, void>)&ApplicationLaunched);
    [UnmanagedCallersOnly]
    static void ApplicationLaunched(Layout* block, nint application, nint error)
    {
        try
        {
            ((Action<string?>)GCHandle.FromIntPtr(block->Context).Target!)(Error(error) ?? (application == 0 ? "No application was launched." : null));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
        }
    }

    [UnmanagedCallersOnly]
    static void Copy(Layout* destination, Layout* source) => destination->Context = GCHandle.ToIntPtr(GCHandle.Alloc(GCHandle.FromIntPtr(source->Context).Target));
    [UnmanagedCallersOnly]
    static void Release(Layout* block) => GCHandle.FromIntPtr(block->Context).Free();
    static string? Error(nint error) => error == 0 ? null : ObjC.Text(ObjC.O(error, "localizedDescription"));
    [UnmanagedCallersOnly]
    static void PermissionInvoked(Layout* block, byte granted, nint error)
    {
        try
        {
            ((Action<bool, string?>)GCHandle.FromIntPtr(block->Context).Target!)(granted != 0, Error(error));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    [UnmanagedCallersOnly]
    static void CompletionInvoked(Layout* block, nint error)
    {
        try
        {
            ((Action<string?>)GCHandle.FromIntPtr(block->Context).Target!)(Error(error));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
        }
    }

    internal static void Complete(nint block)
    {
        if (block != 0)
            ((delegate* unmanaged<nint, void>)((Layout*)block)->Invoke)(block);
    }

    internal static void Complete(nint block, nuint options)
    {
        if (block != 0)
            ((delegate* unmanaged<nint, nuint, void>)((Layout*)block)->Invoke)(block, options);
    }

    public void Dispose()
    {
        if (block == 0)
            return;
        _Block_release(block);
        block = 0;
    }

    [DllImport(SystemLibrary)]
    static extern nint _Block_copy(nint block);
    [DllImport(SystemLibrary)]
    static extern void _Block_release(nint block);
}
