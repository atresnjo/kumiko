using System.Runtime.InteropServices;
using Kumiko.Platform;
using Kumiko.Rendering;

namespace Kumiko.Desktop;

internal static unsafe class LauncherIcons
{
    internal static Image? Load(string path, int pixels)
    {
        if (OperatingSystem.IsMacOS())
            return MacImages.ApplicationIcon(path, pixels, 1) is { } image ? new(image) : null;
        if (OperatingSystem.IsWindows())
            return Windows(path, pixels);
        return Linux(path, pixels);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShellInfo
    {
        public nint Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string Type;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int X, Y;
        public uint Used, Important;
    }

    internal static string WindowsName(string path)
    {
        return SHGetFileInfoW(path, 0, out var info, (uint)Marshal.SizeOf<ShellInfo>(), 0x200) != 0 && !string.IsNullOrWhiteSpace(info.Name)
            ? info.Name : Path.GetFileNameWithoutExtension(path);
    }

    static Image? Windows(string path, int size)
    {
        if (SHGetFileInfoW(path, 0, out var info, (uint)Marshal.SizeOf<ShellInfo>(), 0x100) == 0 || info.Icon == 0)
            return null;
        nint dc = 0, bitmap = 0, previous = 0;
        try
        {
            dc = CreateCompatibleDC(0);
            var format = new BitmapInfo { Size = (uint)sizeof(BitmapInfo), Width = size, Height = -size, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(dc, ref format, 0, out var data, 0, 0);
            if (dc == 0 || bitmap == 0)
                return null;
            previous = SelectObject(dc, bitmap);
            new Span<byte>((void*)data, size * size * 4).Clear();
            if (!DrawIconEx(dc, 0, 0, info.Icon, size, size, 0, 0, 3))
                return null;
            var pixels = Pixels.Create(size, size);
            Marshal.Copy(data, pixels.Bytes, 0, pixels.Bytes.Length);
            for (var i = 0; i < pixels.Bytes.Length; i += 4)
                (pixels.Bytes[i], pixels.Bytes[i + 2]) = (pixels.Bytes[i + 2], pixels.Bytes[i]);
            return new(pixels);
        }
        finally
        {
            if (previous != 0)
                SelectObject(dc, previous);
            if (bitmap != 0)
                DeleteObject(bitmap);
            if (dc != 0)
                DeleteDC(dc);
            DestroyIcon(info.Icon);
        }
    }

    static Image? Linux(string path, int size)
    {
        nint owner = 0, file = 0, iconInfo = 0, pixbuf = 0;
        try
        {
            nint icon;
            if (path.EndsWith(".desktop", StringComparison.Ordinal))
            {
                owner = ApplicationCatalog.g_desktop_app_info_new_from_filename(path);
                icon = owner == 0 ? 0 : g_app_info_get_icon(owner);
            }
            else
            {
                file = g_file_new_for_path(path);
                owner = g_file_query_info(file, "standard::icon", 0, 0, out var error);
                if (error != 0)
                    _ = ApplicationCatalog.GioError(error);
                icon = owner == 0 ? 0 : g_file_info_get_icon(owner);
            }
            if (icon == 0)
                return null;
            iconInfo = gtk_icon_theme_lookup_by_gicon(gtk_icon_theme_get_default(), icon, size, 16);
            if (iconInfo == 0)
                return null;
            pixbuf = gtk_icon_info_load_icon(iconInfo, out var loadError);
            if (loadError != 0)
                _ = ApplicationCatalog.GioError(loadError);
            if (pixbuf == 0)
                return null;
            int width = gdk_pixbuf_get_width(pixbuf), height = gdk_pixbuf_get_height(pixbuf);
            int stride = gdk_pixbuf_get_rowstride(pixbuf), channels = gdk_pixbuf_get_n_channels(pixbuf);
            if (width > 256 || height > 256 || channels is < 3 or > 4)
                return null;
            var source = (byte*)gdk_pixbuf_get_pixels(pixbuf);
            var pixels = Pixels.Create(width, height);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    int target = (y * width + x) * 4;
                    var pixel = source + y * stride + x * channels;
                    pixels.Bytes[target] = pixel[0];
                    pixels.Bytes[target + 1] = pixel[1];
                    pixels.Bytes[target + 2] = pixel[2];
                    pixels.Bytes[target + 3] = channels == 4 ? pixel[3] : (byte)255;
                }
            return new(pixels);
        }
        finally
        {
            foreach (var value in new[] { pixbuf, iconInfo, owner, file })
                if (value != 0)
                    ApplicationCatalog.g_object_unref(value);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint SHGetFileInfoW(string path, uint attributes, out ShellInfo info, uint size, uint flags);
    [DllImport("gdi32.dll")]
    static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [DllImport(ApplicationCatalog.Gio)]
    static extern nint g_app_info_get_icon(nint info);
    [DllImport(ApplicationCatalog.Gio)]
    static extern nint g_file_new_for_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(ApplicationCatalog.Gio)]
    static extern nint g_file_query_info(nint file, [MarshalAs(UnmanagedType.LPUTF8Str)] string attributes, int flags, nint cancellation, out nint error);
    [DllImport(ApplicationCatalog.Gio)]
    static extern nint g_file_info_get_icon(nint info);
    [DllImport(Gtk.Library)]
    static extern nint gtk_icon_theme_get_default();
    [DllImport(Gtk.Library)]
    static extern nint gtk_icon_theme_lookup_by_gicon(nint theme, nint icon, int size, int flags);
    [DllImport(Gtk.Library)]
    static extern nint gtk_icon_info_load_icon(nint info, out nint error);
    [DllImport("libgdk_pixbuf-2.0.so.0")]
    static extern int gdk_pixbuf_get_width(nint buffer);
    [DllImport("libgdk_pixbuf-2.0.so.0")]
    static extern int gdk_pixbuf_get_height(nint buffer);
    [DllImport("libgdk_pixbuf-2.0.so.0")]
    static extern int gdk_pixbuf_get_rowstride(nint buffer);
    [DllImport("libgdk_pixbuf-2.0.so.0")]
    static extern int gdk_pixbuf_get_n_channels(nint buffer);
    [DllImport("libgdk_pixbuf-2.0.so.0")]
    static extern nint gdk_pixbuf_get_pixels(nint buffer);
}
