using System.Runtime.InteropServices;

namespace Kumiko.Rendering;

internal static unsafe class LinuxImages
{
    const string Pixbuf = "libgdk_pixbuf-2.0.so.0";
    const string GObject = "libgobject-2.0.so.0";
    const string GLib = "libglib-2.0.so.0";
    sealed class Limit
    {
        internal bool Rejected;
    }
    [UnmanagedCallersOnly]
    static void Prepared(nint loader, int width, int height, nint data)
    {
        try
        {
            if (RasterLimits.Valid(width, height))
                return;
            ((Limit)GCHandle.FromIntPtr(data).Target!).Rejected = true;
            gdk_pixbuf_loader_set_size(loader, 1, 1);
        }
        catch
        {
            gdk_pixbuf_loader_set_size(loader, 1, 1);
        }
    }
    internal static Pixels Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 64 * 1024 * 1024)
            throw new InvalidDataException("Raster data exceeds the import limit.");
        nint loader = gdk_pixbuf_loader_new();
        if (loader == 0)
            throw new InvalidDataException("Could not create image decoder.");
        var limit = new Limit();
        var context = GCHandle.Alloc(limit);
        nuint signal = g_signal_connect_data(loader, "size-prepared", (nint)(delegate* unmanaged<nint, int, int, nint, void>)&Prepared, GCHandle.ToIntPtr(context), 0, 0);
        nint oriented = 0;
        try
        {
            nint error;
            fixed (byte* pointer = bytes)
                Check(gdk_pixbuf_loader_write(loader, pointer, (nuint)bytes.Length, out error), error);
            Check(gdk_pixbuf_loader_close(loader, out error), error);
            if (limit.Rejected)
                throw new InvalidDataException("Raster dimensions exceed the import limit.");
            nint original = gdk_pixbuf_loader_get_pixbuf(loader);
            if (original == 0)
                throw new InvalidDataException("Image contains no raster frame.");
            oriented = gdk_pixbuf_apply_embedded_orientation(original);
            if (oriented == 0)
                throw new InvalidDataException("Could not orient raster image.");
            int width = gdk_pixbuf_get_width(oriented), height = gdk_pixbuf_get_height(oriented);
            RasterLimits.Check(width, height);
            int channels = gdk_pixbuf_get_n_channels(oriented), stride = gdk_pixbuf_get_rowstride(oriented);
            if (gdk_pixbuf_get_bits_per_sample(oriented) != 8 || channels is not (3 or 4) || stride < checked(width * channels))
                throw new InvalidDataException("Unsupported native raster layout.");
            var pixels = Pixels.Create(width, height);
            byte* source = (byte*)gdk_pixbuf_get_pixels(oriented);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int target = (y * width + x) * 4;
                    byte* pixel = source + y * stride + x * channels;
                    byte alpha = channels == 4 ? pixel[3] : (byte)255;
                    pixels.Bytes[target] = (byte)(pixel[2] * alpha / 255);
                    pixels.Bytes[target + 1] = (byte)(pixel[1] * alpha / 255);
                    pixels.Bytes[target + 2] = (byte)(pixel[0] * alpha / 255);
                    pixels.Bytes[target + 3] = alpha;
                }
            return pixels;
        }
        finally
        {
            if (oriented != 0)
                g_object_unref(oriented);
            g_signal_handler_disconnect(loader, signal);
            g_object_unref(loader);
            context.Free();
        }
    }
    static void Check(int success, nint error)
    {
        if (error != 0)
            g_error_free(error);
        if (success == 0)
            throw new InvalidDataException("Native image decoding failed.");
    }
    [DllImport(Pixbuf)] static extern nint gdk_pixbuf_loader_new();
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_loader_write(nint loader, byte* bytes, nuint length, out nint error);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_loader_close(nint loader, out nint error);
    [DllImport(Pixbuf)] static extern void gdk_pixbuf_loader_set_size(nint loader, int width, int height);
    [DllImport(Pixbuf)] static extern nint gdk_pixbuf_loader_get_pixbuf(nint loader);
    [DllImport(Pixbuf)] static extern nint gdk_pixbuf_apply_embedded_orientation(nint pixbuf);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_get_width(nint pixbuf);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_get_height(nint pixbuf);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_get_n_channels(nint pixbuf);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_get_rowstride(nint pixbuf);
    [DllImport(Pixbuf)] static extern int gdk_pixbuf_get_bits_per_sample(nint pixbuf);
    [DllImport(Pixbuf)] static extern nint gdk_pixbuf_get_pixels(nint pixbuf);
    [DllImport(GObject)] static extern nuint g_signal_connect_data(nint obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nint callback, nint data, nint destroy, int flags);
    [DllImport(GObject)] static extern void g_signal_handler_disconnect(nint obj, nuint id);
    [DllImport(GObject)] static extern void g_object_unref(nint obj);
    [DllImport(GLib)] static extern void g_error_free(nint error);
}

internal static class RasterLimits
{
    internal static bool Valid(int width, int height) => width is > 0 and <= 8192 && height is > 0 and <= 8192 && (long)width * height <= 32 * 1024 * 1024;
    internal static void Check(int width, int height)
    {
        if (!Valid(width, height))
            throw new InvalidDataException("Raster dimensions exceed the import limit.");
    }
}
