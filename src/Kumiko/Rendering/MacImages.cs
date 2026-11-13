using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Rendering;

internal static unsafe class MacImages
{
    const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string IO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    [DllImport(CF)]
    static extern nint CFDataCreate(nint allocator, byte* bytes, nint length);
    [DllImport(CF)]
    static extern void CFRelease(nint value);
    [DllImport(IO)]
    static extern nint CGImageSourceCreateWithData(nint data, nint options);
    [DllImport(IO)]
    static extern nint CGImageSourceCreateImageAtIndex(nint source, nuint index, nint options);
    [DllImport(CG)]
    static extern nuint CGImageGetWidth(nint image);
    [DllImport(CG)]
    static extern nuint CGImageGetHeight(nint image);
    [DllImport(CG)]
    static extern void CGImageRelease(nint image);
    [DllImport(CG)]
    static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(CG)]
    static extern void CGColorSpaceRelease(nint space);
    [DllImport(CG)]
    static extern nint CGBitmapContextCreate(byte* data, nuint w, nuint h, nuint bits, nuint stride, nint space, uint flags);
    [DllImport(CG)]
    static extern void CGContextDrawImage(nint context, NativeRect rect, nint image);
    [DllImport(CG)]
    static extern void CGContextRelease(nint context);
    internal static Pixels Decode(ReadOnlySpan<byte> bytes)
    {
        nint data;
        fixed (byte* p = bytes)
            data = CFDataCreate(0, p, bytes.Length);
        try
        {
            var source = CGImageSourceCreateWithData(data, 0);
            if (source == 0)
                throw new InvalidDataException("Not a supported image.");
            try
            {
                var image = CGImageSourceCreateImageAtIndex(source, 0, 0);
                if (image == 0)
                    throw new InvalidDataException("Image decode failed.");
                try
                {
                    int width = checked((int)CGImageGetWidth(image)), height = checked((int)CGImageGetHeight(image));
                    RasterLimits.Check(width, height);
                    var pixels = Pixels.Create(width, height);
                    Draw(pixels, ctx => CGContextDrawImage(ctx, new(0, 0, pixels.Width, pixels.Height), image));
                    return pixels;
                }
                finally
                {
                    CGImageRelease(image);
                }
            }
            finally
            {
                CFRelease(source);
            }
        }
        finally
        {
            CFRelease(data);
        }
    }

    internal static void Svg(ReadOnlySpan<byte> bytes, Pixels pixels)
    {
        using var pool = new AutoreleasePool();
        nint data;
        fixed (byte* p = bytes)
            data = O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)bytes.Length);
        var image = O.O(O.O(O.Class("NSImage"), "alloc"), "initWithData:", data);
        if (image == 0)
            throw new InvalidDataException("SVG decode failed.");
        try
        {
            var intrinsic = O.S(image, "size");
            var fit = intrinsic.Width > 0 && intrinsic.Height > 0
                ? Math.Min(pixels.Width / intrinsic.Width, pixels.Height / intrinsic.Height)
                : 0;
            double drawWidth = fit > 0 ? intrinsic.Width * fit : pixels.Width, drawHeight = fit > 0 ? intrinsic.Height * fit : pixels.Height;
            // AppKit stretches SVGs to the target rect; center them to keep the document's aspect ratio.
            DrawImage(pixels, image, new((pixels.Width - drawWidth) / 2, (pixels.Height - drawHeight) / 2, drawWidth, drawHeight));
        }
        finally
        {
            O.Release(image);
        }
    }

    internal static Pixels? Symbol(string name, float size, float scale)
    {
        using var pool = new AutoreleasePool();
        var image = O.O(O.Class("NSImage"), "imageWithSystemSymbolName:accessibilityDescription:", O.TempString(name), (nint)0);
        if (image == 0)
            return null;
        var configuration = O.O(O.Class("NSImageSymbolConfiguration"), "configurationWithPointSize:weight:", (double)size, 0d);
        image = O.O(image, "imageWithSymbolConfiguration:", configuration);
        var pixels = Pixels.Create(Math.Max(1, (int)MathF.Ceiling(size * scale)), Math.Max(1, (int)MathF.Ceiling(size * scale)));
        // Symbols such as "minus" or chevrons are not square; fit them centered instead of stretching.
        var intrinsic = O.S(image, "size");
        var fit = Math.Min(pixels.Width / Math.Max(1e-3, intrinsic.Width), pixels.Height / Math.Max(1e-3, intrinsic.Height));
        double width = intrinsic.Width * fit, height = intrinsic.Height * fit;
        DrawImage(pixels, image, new(new((pixels.Width - width) / 2, (pixels.Height - height) / 2), new(width, height)));
        // The glyph shader samples coverage; its tint is chosen at paint time.
        for (int i = 0; i < pixels.Bytes.Length; i += 4)
            pixels.Bytes[i] = pixels.Bytes[i + 1] = pixels.Bytes[i + 2] = 255;
        return pixels;
    }

    internal static Pixels? ApplicationIcon(string bundle, float size, float scale)
    {
        if (string.IsNullOrWhiteSpace(bundle) || !float.IsFinite(size) || !float.IsFinite(scale) || size <= 0 || scale <= 0 || size * scale > 256)
            return null;
        using var pool = new AutoreleasePool();
        var image = O.O(O.O(O.Class("NSWorkspace"), "sharedWorkspace"), "iconForFile:", O.TempString(bundle));
        if (image == 0)
            return null;
        var pixels = Pixels.Create((int)MathF.Ceiling(size * scale), (int)MathF.Ceiling(size * scale));
        DrawImage(pixels, image, new(0, 0, pixels.Width, pixels.Height));
        return pixels;
    }

    static void DrawImage(Pixels pixels, nint image, NativeRect rect) => Draw(pixels, context =>
    {
        var graphics = O.O(O.Class("NSGraphicsContext"), "graphicsContextWithCGContext:flipped:", context, (nuint)0);
        O.V(O.Class("NSGraphicsContext"), "saveGraphicsState");
        try
        {
            O.V(O.Class("NSGraphicsContext"), "setCurrentContext:", graphics);
            O.V(image, "drawInRect:fromRect:operation:fraction:", rect, default(NativeRect), (nuint)2, 1d);
        }
        finally
        {
            O.V(O.Class("NSGraphicsContext"), "restoreGraphicsState");
        }
    });

    static void Draw(Pixels pixels, Action<nint> draw)
    {
        var space = CGColorSpaceCreateDeviceRGB();
        try
        {
            fixed (byte* p = pixels.Bytes)
            {
                var context = CGBitmapContextCreate(p, (nuint)pixels.Width, (nuint)pixels.Height, 8, (nuint)(pixels.Width * 4), space, 2 | (2 << 12));
                if (context == 0)
                    throw new InvalidOperationException("Image bitmap creation failed.");
                try
                {
                    draw(context);
                }
                finally
                {
                    CGContextRelease(context);
                }
            }
        }
        finally
        {
            CGColorSpaceRelease(space);
        }
    }
}
