using Kumiko.Platform;
using Kumiko.UI;

namespace Kumiko.Rendering;

/// <summary>Owned, windowless GPU renderer for repeatable inspection and verification.</summary>
public sealed class OffscreenGpu : IDisposable
{
    readonly IGpuContext context;
    readonly OpenGl renderer;
    bool disposed;
    public string Driver => renderer.Driver;
    internal int ReadbackCount => renderer.ReadbackCount;
    public OffscreenGpu()
    {
        context = OperatingSystem.IsMacOS() ? new CglContext() : OperatingSystem.IsWindows() ? new WglContext() : new EglContext();
        try
        {
            renderer = new(context.Load);
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }
    public Pixels Capture(Context view)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        view.CheckThread();
        context.MakeCurrent();
        renderer.Render(view, false);
        return renderer.Readback();
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        context.MakeCurrent();
        renderer.Dispose();
        context.Dispose();
    }
}
