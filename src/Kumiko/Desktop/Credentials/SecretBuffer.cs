using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kumiko.Desktop;

/// <summary>
/// An owned unmanaged secret buffer, cleared on Dispose (finalizer as a fallback).
/// Bytes must not be retained across disposal or used concurrently with disposal. ToString never exposes content.
/// </summary>
public sealed unsafe class SecretBuffer : IDisposable
{
    nint pointer;
    readonly int length;
    internal SecretBuffer(ReadOnlySpan<byte> value)
    {
        length = value.Length;
        pointer = (nint)NativeMemory.Alloc((nuint)Math.Max(1, length));
        if (pointer == 0)
            throw new OutOfMemoryException();
        value.CopyTo(new Span<byte>((void*)pointer, length));
    }
    public ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(pointer == 0, this);
            return new((void*)pointer, length);
        }
    }
    public override string ToString() => "[secret]";
    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }
    void Clear()
    {
        var owned = Interlocked.Exchange(ref pointer, 0);
        if (owned == 0)
            return;
        CryptographicOperations.ZeroMemory(new Span<byte>((void*)owned, length));
        NativeMemory.Free((void*)owned);
    }
    ~SecretBuffer() => Clear();
}
