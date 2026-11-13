using System.Runtime.InteropServices;

namespace Kumiko.Desktop;

/// <summary>A native cancellation object, retained until its managed cancellation callback has stopped.</summary>
internal sealed class GioCancellation : IDisposable
{
    readonly CancellationTokenRegistration registration;
    internal nint Handle
    {
        get;
    }
    internal GioCancellation(CancellationToken token)
    {
        Handle = g_cancellable_new();
        if (Handle == 0)
            throw new OutOfMemoryException();
        registration = token.Register(() => g_cancellable_cancel(Handle));
    }
    public void Dispose()
    {
        registration.Dispose();
        g_object_unref(Handle);
    }
    [DllImport(SecretVariant.Gio)]
    static extern nint g_cancellable_new();
    [DllImport(SecretVariant.Gio)]
    static extern void g_cancellable_cancel(nint cancellation);
    [DllImport(SecretVariant.GObject)]
    static extern void g_object_unref(nint value);
}
