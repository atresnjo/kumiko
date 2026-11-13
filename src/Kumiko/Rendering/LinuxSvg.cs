using System.Runtime.InteropServices;

namespace Kumiko.Rendering;

internal static unsafe class LinuxSvg
{
    const string Rsvg = "librsvg-2.so.2", Cairo = "libcairo.so.2", GLib = "libglib-2.0.so.0", GObject = "libgobject-2.0.so.0";
    // rsvg_handle_render_document needs librsvg 2.46; older or missing libraries are unsupported.
    static readonly bool available = NativeLibrary.TryLoad(Rsvg, out var library) && NativeLibrary.TryGetExport(library, "rsvg_handle_render_document", out _);
    [StructLayout(LayoutKind.Sequential)]
    readonly record struct Viewport(double X, double Y, double Width, double Height);

    [DllImport(Rsvg)]
    static extern nint rsvg_handle_new_from_data(byte* data, nuint length, out nint error);
    [DllImport(Rsvg)]
    static extern int rsvg_handle_render_document(nint handle, nint cairo, Viewport* viewport, out nint error);
    [DllImport(Cairo)]
    static extern nint cairo_image_surface_create_for_data(byte* data, int format, int width, int height, int stride);
    [DllImport(Cairo)]
    static extern int cairo_surface_status(nint surface);
    [DllImport(Cairo)]
    static extern void cairo_surface_flush(nint surface);
    [DllImport(Cairo)]
    static extern void cairo_surface_destroy(nint surface);
    [DllImport(Cairo)]
    static extern nint cairo_create(nint surface);
    [DllImport(Cairo)]
    static extern void cairo_destroy(nint cairo);
    [DllImport(GObject)]
    static extern void g_object_unref(nint value);
    [DllImport(GLib)]
    static extern void g_error_free(nint error);
    internal static void Render(ReadOnlySpan<byte> bytes, Pixels pixels)
    {
        if (!available)
            throw new PlatformNotSupportedException("SVG rendering on Linux needs librsvg 2.46 or later (librsvg2-2).");
        nint handle, error;
        fixed (byte* p = bytes)
            handle = rsvg_handle_new_from_data(p, (nuint)bytes.Length, out error);
        if (handle == 0)
            throw new InvalidDataException(Message(error, "SVG decode failed."));
        try
        {
            fixed (byte* p = pixels.Bytes)
            {
                // Cairo ARGB32 is premultiplied BGRA in memory on little-endian hosts, like Pixels.
                var surface = cairo_image_surface_create_for_data(p, 0, pixels.Width, pixels.Height, pixels.Width * 4);
                try
                {
                    if (cairo_surface_status(surface) != 0)
                        throw new InvalidOperationException("SVG surface creation failed.");
                    var cairo = cairo_create(surface);
                    try
                    {
                        // librsvg fits the document into the viewport using its preserveAspectRatio.
                        var viewport = new Viewport(0, 0, pixels.Width, pixels.Height);
                        if (rsvg_handle_render_document(handle, cairo, &viewport, out error) == 0)
                            throw new InvalidDataException(Message(error, "SVG render failed."));
                    }
                    finally
                    {
                        cairo_destroy(cairo);
                    }

                    cairo_surface_flush(surface);
                }
                finally
                {
                    cairo_surface_destroy(surface);
                }
            }
        }
        finally
        {
            g_object_unref(handle);
        }
    }

    static string Message(nint error, string fallback)
    {
        if (error == 0)
            return fallback;
        try
        {
            // GError is { GQuark domain; gint code; gchar* message }.
            var message = Marshal.PtrToStringUTF8(*(nint*)((byte*)error + 8));
            return string.IsNullOrEmpty(message) ? fallback : $"{fallback} {message}";
        }
        finally
        {
            g_error_free(error);
        }
    }
}
