using System.Runtime.InteropServices;
using Kumiko.Rendering;
using Kumiko.UI;

namespace Kumiko.Platform;

internal sealed class LinuxGpu : IDisposable
{
    readonly nint context;
    readonly nint library;
    readonly OpenGl renderer;
    bool disposed;
    public string Driver => renderer.Driver;
    [DllImport(Gtk.Gdk)] static extern nint gdk_window_create_gl_context(nint window, out nint error);
    [DllImport(Gtk.Gdk)] static extern void gdk_gl_context_set_required_version(nint context, int major, int minor);
    [DllImport(Gtk.Gdk)] static extern void gdk_gl_context_set_use_es(nint context, int useEs);
    [DllImport(Gtk.Gdk)] static extern int gdk_gl_context_realize(nint context, out nint error);
    [DllImport(Gtk.Gdk)] static extern void gdk_gl_context_make_current(nint context);
    [DllImport(Gtk.Gdk)] static extern void gdk_gl_context_clear_current();
    [DllImport(Gtk.Gdk)] static extern void gdk_cairo_draw_from_gl(nint cairo, nint window, int source, int sourceType, int scale, int x, int y, int width, int height);
    [DllImport(Gtk.GLib)] static extern void g_error_free(nint error);
    public LinuxGpu(nint view)
    {
        context = gdk_window_create_gl_context(Gtk.gtk_widget_get_window(view), out var error);
        if (error != 0)
            g_error_free(error);
        if (context == 0)
            throw new InvalidOperationException("GDK OpenGL context is unavailable.");
        try
        {
            gdk_gl_context_set_required_version(context, 3, 3);
            gdk_gl_context_set_use_es(context, 0);
            if (gdk_gl_context_realize(context, out error) == 0)
            {
                if (error != 0)
                    g_error_free(error);
                throw new InvalidOperationException("GDK OpenGL 3.3 context could not be realized.");
            }
            gdk_gl_context_make_current(context);
            library = NativeLibrary.Load("libGL.so.1");
            renderer = new(name => NativeLibrary.GetExport(library, name));
        }
        catch
        {
            Gtk.g_object_unref(context);
            if (library != 0)
                NativeLibrary.Free(library);
            throw;
        }
    }
    public void Draw(Context view, nint cairo, nint widget)
    {
        gdk_gl_context_make_current(context);
        renderer.Render(view, false);
        gdk_cairo_draw_from_gl(cairo, Gtk.gtk_widget_get_window(widget), (int)renderer.FrameTexture, 0x1702,
            Math.Max(1, Gtk.gtk_widget_get_scale_factor(widget)), 0, 0, renderer.Width, renderer.Height);
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        gdk_gl_context_make_current(context);
        renderer.Dispose();
        gdk_gl_context_clear_current();
        Gtk.g_object_unref(context);
        NativeLibrary.Free(library);
    }
}
