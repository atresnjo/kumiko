using O = Kumiko.Platform.ObjC;

namespace Kumiko.Platform;

internal sealed class MacWindowProgress : IDisposable
{
    readonly nint tile, image;
    nint root, indicator;
    bool disposed;
    internal MacWindowProgress(nint tile, nint image)
    {
        this.tile = O.O(tile, "retain");
        this.image = O.O(image, "retain");
    }

    internal void Set(double? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var pool = new AutoreleasePool();
        if (value is null)
        {
            if (root != 0 && O.O(tile, "contentView") == root)
            {
                O.V(tile, "setContentView:", (nint)0);
                O.V(tile, "display");
            }

            O.Release(indicator);
            O.Release(root);
            indicator = root = 0;
            return;
        }

        if (root == 0)
        {
            root = O.O(O.O(O.Class("NSView"), "alloc"), "initWithFrame:", new NativeRect(0, 0, 128, 128));
            var imageView = O.O(O.O(O.Class("NSImageView"), "alloc"), "initWithFrame:", new NativeRect(0, 0, 128, 128));
            try
            {
                O.V(imageView, "setImage:", image);
                O.V(imageView, "setImageScaling:", (nuint)3);
                O.V(imageView, "setAutoresizingMask:", (nuint)18);
                O.V(root, "addSubview:", imageView);
            }
            finally
            {
                O.Release(imageView);
            }

            indicator = O.O(O.O(O.Class("NSProgressIndicator"), "alloc"), "initWithFrame:", new NativeRect(8, 6, 112, 18));
            O.V(indicator, "setIndeterminate:", (byte)0);
            O.V(indicator, "setMaxValue:", 1d);
            O.V(indicator, "setAutoresizingMask:", (nuint)2);
            O.V(root, "addSubview:", indicator);
        }

        O.V(indicator, "setDoubleValue:", value.Value);
        O.V(tile, "setContentView:", root);
        O.V(tile, "display");
    }

    public void Dispose()
    {
        if (disposed)
            return;
        Set(null);
        disposed = true;
        O.Release(image);
        O.Release(tile);
    }
}
