using System.Runtime.InteropServices;

namespace Kumiko.Rendering;

internal static unsafe class WindowsImages
{
    [StructLayout(LayoutKind.Sequential)]
    struct Startup
    {
        internal uint Version;
        internal nint Callback;
        internal int SuppressThread, SuppressCodecs;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Rectangle
    {
        internal int X, Y, Width, Height;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapData
    {
        internal uint Width, Height;
        internal int Stride;
        internal uint Format;
        internal nint Scan, Reserved;
    }
    // PropertyItem from gdiplusimaging.h: PROPID id, ULONG length, WORD type, VOID* value.
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyItem
    {
        internal uint Id, Length;
        internal ushort Type;
        internal void* Value;
    }
    internal static Pixels Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 64 * 1024 * 1024)
            throw new InvalidDataException("Raster data exceeds the import limit.");
        var startup = new Startup { Version = 1 };
        Check(GdiplusStartup(out nuint token, ref startup, 0));
        nint stream = 0, image = 0;
        try
        {
            fixed (byte* data = bytes)
                stream = SHCreateMemStream(data, (uint)bytes.Length);
            if (stream == 0)
                throw new OutOfMemoryException();
            Check(GdipCreateBitmapFromStream(stream, out image));
            // GDI+ keeps the encoded orientation; turn the bitmap upright before measuring and reading it.
            var turn = RotateFlip(Orientation(image));
            if (turn != 0)
                Check(GdipImageRotateFlip(image, turn));
            Check(GdipGetImageWidth(image, out uint width));
            Check(GdipGetImageHeight(image, out uint height));
            if (width > int.MaxValue || height > int.MaxValue)
                throw new InvalidDataException("Raster dimensions overflow.");
            RasterLimits.Check((int)width, (int)height);
            var rect = new Rectangle { Width = (int)width, Height = (int)height };
            Check(GdipBitmapLockBits(image, ref rect, 1, 0xe200b, out var bitmap));
            try
            {
                var pixels = Pixels.Create((int)width, (int)height);
                if (Math.Abs((long)bitmap.Stride) < (long)width * 4)
                    throw new InvalidDataException("Invalid native bitmap stride.");
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        byte* source = (byte*)bitmap.Scan + y * bitmap.Stride + x * 4;
                        int offset = checked((y * (int)width + x) * 4);
                        pixels.Bytes[offset] = source[0];
                        pixels.Bytes[offset + 1] = source[1];
                        pixels.Bytes[offset + 2] = source[2];
                        pixels.Bytes[offset + 3] = source[3];
                    }
                return pixels;
            }
            finally
            {
                GdipBitmapUnlockBits(image, ref bitmap);
            }
        }
        finally
        {
            if (image != 0)
                GdipDisposeImage(image);
            if (stream != 0)
                ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)stream)[2])(stream);
            GdiplusShutdown(token);
        }
    }
    /// <summary>The EXIF/TIFF Orientation tag (PropertyTagOrientation, 0x0112), or 1 when absent or malformed.</summary>
    static int Orientation(nint image)
    {
        // PropertyNotFound (19) and other statuses mean the image carries no usable orientation.
        if (GdipGetPropertyItemSize(image, 0x0112, out uint size) != 0 || size < (uint)sizeof(PropertyItem) || size > 4096)
            return 1;
        var buffer = stackalloc byte[(int)size];
        var item = (PropertyItem*)buffer;
        if (GdipGetPropertyItem(image, 0x0112, size, item) != 0 || item->Value == null)
            return 1;
        // PropertyTagTypeShort (3) per EXIF; some writers store a LONG (4).
        if (item->Type == 3 && item->Length >= 2)
            return *(ushort*)item->Value;
        if (item->Type == 4 && item->Length >= 4)
            return (int)Math.Min(*(uint*)item->Value, 9);
        return 1;
    }

    /// <summary>
    /// The GDI+ RotateFlipType (rotation clockwise, then flip) that displays an EXIF orientation upright:
    /// 2 mirrors, 3 turns 180°, 4 flips vertically, 5 transposes, 6 turns 90° clockwise, 7 transverses and
    /// 8 turns 90° counter-clockwise.
    /// </summary>
    static int RotateFlip(int orientation) => orientation switch
    {
        2 => 4, // RotateNoneFlipX
        3 => 2, // Rotate180FlipNone
        4 => 6, // RotateNoneFlipY
        5 => 5, // Rotate90FlipX
        6 => 1, // Rotate90FlipNone
        7 => 7, // Rotate270FlipX
        8 => 3, // Rotate270FlipNone
        _ => 0
    };

    static void Check(int status)
    {
        if (status != 0)
            throw new InvalidDataException("GDI+ raster decoding failed: " + status);
    }
    [DllImport("gdiplus.dll")] static extern int GdiplusStartup(out nuint token, ref Startup startup, nint output);
    [DllImport("gdiplus.dll")] static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll")] static extern int GdipCreateBitmapFromStream(nint stream, out nint bitmap);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageWidth(nint image, out uint width);
    [DllImport("gdiplus.dll")] static extern int GdipGetImageHeight(nint image, out uint height);
    [DllImport("gdiplus.dll")] static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll")] static extern int GdipGetPropertyItemSize(nint image, uint id, out uint size);
    [DllImport("gdiplus.dll")] static extern int GdipGetPropertyItem(nint image, uint id, uint size, PropertyItem* item);
    [DllImport("gdiplus.dll")] static extern int GdipImageRotateFlip(nint image, int type);
    [DllImport("gdiplus.dll")] static extern int GdipBitmapLockBits(nint bitmap, ref Rectangle rect, uint flags, int format, out BitmapData data);
    [DllImport("gdiplus.dll")] static extern int GdipBitmapUnlockBits(nint bitmap, ref BitmapData data);
    [DllImport("shlwapi.dll")] static extern nint SHCreateMemStream(byte* data, uint length);
}
