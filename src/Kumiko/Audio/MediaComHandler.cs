using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Kumiko.Audio;

/// <summary>NativeAOT-safe WinRT TypedEventHandler with COM-owned callback lifetime.</summary>
internal sealed unsafe class MediaComHandler : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct Object
    {
        internal nint Vtable;
        internal nint Context;
        internal int References;
    }
    sealed record Context(Guid Id, Action<nint, nint> Invoke);
    static readonly nint vtable = Vtable();
    nint handle;
    internal nint Handle => handle;
    static nint Vtable()
    {
        var table = (nint*)NativeMemory.Alloc(4, (nuint)sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<Object*, Guid*, nint*, int>)&Query;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<Object*, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<Object*, uint>)&Release;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<Object*, nint, nint, int>)&Invoke;
        return (nint)table;
    }
    internal MediaComHandler(string argsName, string argsId, Action<nint> invoke) : this(HandlerId(argsName, argsId), (_, args) => invoke(args)) { }
    static Guid HandlerId(string argsName, string argsId)
    {
        string signature = "pinterface({9de1c534-6ae1-11e0-84e1-18a905bcc53f};rc(Windows.Media.SystemMediaTransportControls;{99fa3ff4-1742-42a6-902e-087d41f965ec});rc(Windows.Media." + argsName + ";{" + argsId + "}))";
        byte[] ns = new Guid("11f47ad5-7b73-42c0-abae-878b1e16adee").ToByteArray(bigEndian: true);
        byte[] hash = SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes(signature)).ToArray());
        hash[6] = (byte)((hash[6] & 15) | 0x50);
        hash[8] = (byte)((hash[8] & 63) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
    internal MediaComHandler(Guid id, Action<nint, nint> invoke)
    {
        var context = GCHandle.Alloc(new Context(id, invoke));
        var instance = (Object*)NativeMemory.AllocZeroed((nuint)sizeof(Object));
        instance->Vtable = vtable;
        instance->Context = GCHandle.ToIntPtr(context);
        instance->References = 1;
        handle = (nint)instance;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int Query(Object* self, Guid* iid, nint* result)
    {
        *result = 0;
        try
        {
            var context = (Context)GCHandle.FromIntPtr(self->Context).Target!;
            if (*iid != context.Id && *iid != new Guid("00000000-0000-0000-c000-000000000046") && *iid != new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"))
                return unchecked((int)0x80004002);
            Interlocked.Increment(ref self->References);
            *result = (nint)self;
            return 0;
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint AddRef(Object* self) => (uint)Interlocked.Increment(ref self->References);
    static uint ReleaseCore(Object* self)
    {
        int count = Interlocked.Decrement(ref self->References);
        if (count == 0)
        {
            GCHandle.FromIntPtr(self->Context).Free();
            NativeMemory.Free(self);
        }
        return (uint)count;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static uint Release(Object* self) => ReleaseCore(self);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    static int Invoke(Object* self, nint sender, nint args)
    {
        try
        {
            ((Context)GCHandle.FromIntPtr(self->Context).Target!).Invoke(sender, args);
            return 0;
        }
        catch
        {
            return unchecked((int)0x80004005);
        }
    }
    public void Dispose()
    {
        nint current = Interlocked.Exchange(ref handle, 0);
        if (current != 0)
            ReleaseCore((Object*)current);
    }
}
