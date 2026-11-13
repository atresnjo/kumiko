using System.Runtime.InteropServices;
using System.Text;
using Kumiko.UI;

namespace Kumiko.Rendering;

/// <summary>Pango/HarfBuzz shaping, bidirectional layout and system font fallback.</summary>
internal sealed unsafe class LinuxText : INativeTextLayout
{
    const string Pango = "libpango-1.0.so.0", PangoCairo = "libpangocairo-1.0.so.0", Cairo = "libcairo.so.2";
    readonly string text;
    nint layout;
    public float Width
    {
        get;
    }
    public float Height
    {
        get;
    }
    public float Ascent
    {
        get;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Box
    {
        public int X, Y, Width, Height;
    }
    [DllImport(PangoCairo)] static extern nint pango_cairo_font_map_get_default();
    [DllImport(Pango)] static extern nint pango_font_map_create_context(nint map);
    [DllImport(Pango)] static extern nint pango_layout_new(nint context);
    [DllImport(Pango)] static extern nint pango_font_description_new();
    [DllImport(Pango)] static extern void pango_font_description_set_family(nint desc, [MarshalAs(UnmanagedType.LPUTF8Str)] string family);
    [DllImport(Pango)] static extern void pango_font_description_set_absolute_size(nint desc, double size);
    [DllImport(Pango)] static extern void pango_font_description_set_weight(nint desc, int weight);
    [DllImport(Pango)] static extern void pango_font_description_set_style(nint desc, int style);
    [DllImport(Pango)] static extern void pango_font_description_free(nint desc);
    [DllImport(Pango)] static extern void pango_layout_set_font_description(nint layout, nint desc);
    [DllImport(Pango)] static extern void pango_layout_set_text(nint layout, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length);
    [DllImport(Pango)] static extern void pango_layout_get_extents(nint layout, out Box ink, out Box logical);
    [DllImport(Pango)] static extern int pango_layout_get_baseline(nint layout);
    [DllImport(Pango)] static extern void pango_layout_get_cursor_pos(nint layout, int index, out Box strong, out Box weak);
    [DllImport(Pango)] static extern int pango_layout_xy_to_index(nint layout, int x, int y, out int index, out int trailing);
    [DllImport(PangoCairo)] static extern void pango_cairo_show_layout(nint cairo, nint layout);
    [DllImport("libgobject-2.0.so.0")] static extern void g_object_unref(nint value);
    [DllImport(Cairo)] static extern nint cairo_image_surface_create_for_data(byte* data, int format, int width, int height, int stride);
    [DllImport(Cairo)] static extern nint cairo_create(nint surface);
    [DllImport(Cairo)] static extern void cairo_scale(nint cairo, double x, double y);
    [DllImport(Cairo)] static extern void cairo_translate(nint cairo, double x, double y);
    [DllImport(Cairo)] static extern void cairo_set_source_rgba(nint cairo, double r, double g, double b, double a);
    [DllImport(Cairo)] static extern void cairo_surface_flush(nint surface);
    [DllImport(Cairo)] static extern void cairo_destroy(nint cairo);
    [DllImport(Cairo)] static extern void cairo_surface_destroy(nint surface);

    public LinuxText(string text, TextStyle style, string? family)
    {
        this.text = text;
        var context = pango_font_map_create_context(pango_cairo_font_map_get_default());
        if (context == 0)
            throw new InvalidOperationException("Pango context creation failed.");
        var description = pango_font_description_new();
        try
        {
            layout = pango_layout_new(context);
            if (layout == 0)
                throw new InvalidOperationException("Pango layout creation failed.");
            pango_font_description_set_family(description, family ?? (style.Mono ? "monospace" : "sans-serif"));
            pango_font_description_set_absolute_size(description, style.Size * 1024d);
            pango_font_description_set_weight(description, style.Weight switch
            {
                Weight.Bold => 700,
                Weight.Semibold => 600,
                Weight.Medium => 500,
                _ => 400
            });
            pango_font_description_set_style(description, style.Italic ? 2 : 0);
            pango_layout_set_font_description(layout, description);
            pango_layout_set_text(layout, text, -1);
            pango_layout_get_extents(layout, out _, out var logical);
            Width = logical.Width / 1024f;
            Height = logical.Height / 1024f * style.Leading;
            Ascent = pango_layout_get_baseline(layout) / 1024f;
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            pango_font_description_free(description);
            g_object_unref(context);
        }
    }

    public float Caret(int position)
    {
        int index = Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Clamp(position, 0, text.Length)));
        pango_layout_get_cursor_pos(layout, index, out var strong, out _);
        return strong.X / 1024f;
    }

    public int HitTest(float x)
    {
        pango_layout_xy_to_index(layout, (int)(x * 1024), (int)(Ascent * 512), out int index, out int trailing);
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        int position = Encoding.UTF8.GetCharCount(utf8.AsSpan(0, Math.Clamp(index, 0, utf8.Length)));
        while (trailing-- > 0 && position < text.Length)
            position += char.IsHighSurrogate(text[position]) && position + 1 < text.Length ? 2 : 1;
        return Math.Min(position, text.Length);
    }

    public Rect[] Selection(int start, int end)
    {
        int from = Encoding.UTF8.GetByteCount(text.AsSpan(0, start));
        int to = Encoding.UTF8.GetByteCount(text.AsSpan(0, end));
        nint line = pango_layout_get_line_readonly(layout, 0);
        pango_layout_line_get_x_ranges(line, from, to, out nint ranges, out int count);
        try
        {
            var result = new Rect[count];
            for (int i = 0; i < count; i++)
            {
                int a = Marshal.ReadInt32(ranges, i * 8), b = Marshal.ReadInt32(ranges, i * 8 + 4);
                result[i] = new(Math.Min(a, b) / 1024f, 0, Math.Abs(b - a) / 1024f, Height);
            }
            return result;
        }
        finally
        {
            g_free(ranges);
        }
    }
    [DllImport("libpango-1.0.so.0")]
    static extern nint pango_layout_get_line_readonly(nint layout, int line);
    [DllImport("libpango-1.0.so.0")]
    static extern void pango_layout_line_get_x_ranges(nint line, int start, int end, out nint ranges, out int count);
    [DllImport("libglib-2.0.so.0")]
    static extern void g_free(nint memory);

    public TextRaster Raster(float scale, int slice)
    {
        float left = slice * NativeText.SliceWidth;
        int width = Math.Max(1, (int)MathF.Ceiling(Math.Min(NativeText.SliceWidth, Width - left) * scale) + 4);
        int height = Math.Max(1, (int)MathF.Ceiling(Height * scale) + 4);
        var bytes = new byte[checked(width * height * 4)];
        fixed (byte* data = bytes)
        {
            var surface = cairo_image_surface_create_for_data(data, 0, width, height, width * 4);
            var cairo = cairo_create(surface);
            try
            {
                cairo_translate(cairo, 2 - left * scale, 2);
                cairo_scale(cairo, scale, scale);
                cairo_set_source_rgba(cairo, 1, 1, 1, 1);
                pango_cairo_show_layout(cairo, layout);
                cairo_surface_flush(surface);
            }
            finally
            {
                cairo_destroy(cairo);
                cairo_surface_destroy(surface);
            }
        }
        return new(bytes, width, height, -2, -Ascent * scale - 2);
    }

    public void Dispose()
    {
        if (layout != 0)
            g_object_unref(layout);
        layout = 0;
    }
}
