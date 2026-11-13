using Kumiko.UI;

namespace Kumiko.Rendering;

/// <summary>Owned, windowless Direct3D 11 renderer for repeatable verification, as OffscreenGpu.</summary>
internal sealed class OffscreenD3D11 : IDisposable
{
    readonly D3D11 renderer;
    bool disposed;
    public string Driver => renderer.Driver;
    public bool IsWarp => renderer.IsWarp;
    internal int ReadbackCount => renderer.ReadbackCount;
    internal uint RemainingDeviceReferences => renderer.RemainingDeviceReferences;

    /// <summary>Prefers a hardware adapter unless <paramref name="warp"/> forces the WARP rasterizer.</summary>
    public OffscreenD3D11(bool warp)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Direct3D 11 requires Windows.");
        renderer = new(warp);
    }

    public Pixels Capture(Context view)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        view.CheckThread();
        renderer.Render(view, false);
        return renderer.Readback();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        renderer.Dispose();
    }
}
