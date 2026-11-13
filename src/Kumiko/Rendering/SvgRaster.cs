namespace Kumiko.Rendering;

internal static class SvgRaster
{
    internal static Pixels Render(ReadOnlySpan<byte> bytes, float width, float height, float scale)
    {
        if (!float.IsFinite(width) || !float.IsFinite(height) || !float.IsFinite(scale) || width <= 0 || height <= 0 || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "SVG size and scale must be positive and finite.");
        float pixelWidth = MathF.Ceiling(width * scale), pixelHeight = MathF.Ceiling(height * scale);
        if (pixelWidth * pixelHeight > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "SVG pixel size is too large.");
        // Native loaders also accept raster formats; only XML documents with an svg element are SVG.
        if (bytes.IndexOf("<svg"u8) < 0)
            throw new InvalidDataException("Not an SVG document.");
        var pixels = Pixels.Create((int)pixelWidth, (int)pixelHeight);
        if (OperatingSystem.IsMacOS())
            MacImages.Svg(bytes, pixels);
        else if (OperatingSystem.IsLinux())
            LinuxSvg.Render(bytes, pixels);
        else if (OperatingSystem.IsWindows())
            WindowsSvg.Render(bytes, pixels);
        else
            throw new PlatformNotSupportedException("SVG rendering is unavailable on this platform.");
        return pixels;
    }
}
