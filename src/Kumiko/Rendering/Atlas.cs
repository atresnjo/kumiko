namespace Kumiko.Rendering;

public readonly record struct Sprite(int X, int Y, int Width, int Height, float BearingX, float BearingY);
public sealed class Atlas
{
    int x = 1, y = 1, rowHeight;
    public int Width { get; private set; } = 256;
    public int Height => Width;
    public byte[] Bytes { get; private set; } = new byte[256 * 256 * 4];
    public int Version
    {
        get; private set;
    }

    /// <summary>The version at which every texel last changed (growth or reset); renderers older than it upload all rows.</summary>
    internal int Rebuilt
    {
        get; private set;
    }

    /// <summary>The version that last wrote each row, so a renderer can upload only rows newer than its copy.</summary>
    int[] rowVersions = new int[256];

    /// <summary>The rows written after <paramref name="since"/>, as [Top, Bottom); empty when none changed.</summary>
    internal (int Top, int Bottom) Changed(int since)
    {
        int top = Width, bottom = 0;
        for (int row = 0; row < rowVersions.Length; row++)
        {
            if (rowVersions[row] <= since)
                continue;
            top = Math.Min(top, row);
            bottom = row + 1;
        }
        return top < bottom ? (top, bottom) : (0, 0);
    }

    internal void Reset()
    {
        x = y = 1;
        rowHeight = 0;
        Width = 256;
        Bytes = new byte[256 * 256 * 4];
        rowVersions = new int[256];
        Version++;
        Rebuilt = Version;
    }

    public Sprite Insert(ReadOnlySpan<byte> rgba, int width, int height, float bearingX = 0, float bearingY = 0)
    {
        if (width < 0 || height < 0 || rgba.Length != checked(width * height * 4))
            throw new ArgumentException("Invalid image dimensions.");
        while (width + 2 > Width)
            Grow();
        if (x + width + 1 > Width)
        {
            x = 1;
            y += rowHeight + 1;
            rowHeight = 0;
        }

        while (y + height + 1 > Width)
            Grow();
        var sprite = new Sprite(x, y, width, height, bearingX, bearingY);
        for (int row = 0; row < height; row++)
            rgba.Slice(row * width * 4, width * 4).CopyTo(Bytes.AsSpan(((y + row) * Width + x) * 4));
        x += width + 1;
        rowHeight = Math.Max(rowHeight, height);
        Version++;
        rowVersions.AsSpan(y, height).Fill(Version);
        return sprite;
    }

    void Grow()
    {
        if (Width >= 8192)
            throw new InvalidOperationException("Atlas capacity exhausted (8192 × 8192). Create a new text system to reset the atlas.");
        int size = Width * 2;
        var next = new byte[size * size * 4];
        for (int row = 0; row < Width; row++)
            Bytes.AsSpan(row * Width * 4, Width * 4).CopyTo(next.AsSpan(row * size * 4));
        Width = size;
        Bytes = next;
        Array.Resize(ref rowVersions, size);
        Version++;
        Rebuilt = Version;
    }
}

public sealed class Image
{
    public Pixels Pixels
    {
        get;
    }

    public Image(Pixels pixels) => Pixels = pixels;
    public static Image Load(string path) => Decode(File.ReadAllBytes(path));
    public static Image? ApplicationIcon(
        string bundle,
        float size = 32,
        float scale = 2
    ) => OperatingSystem.IsMacOS() && MacImages.ApplicationIcon(bundle, size, scale) is { } pixels ? new(pixels) : null;
    public static Image Decode(ReadOnlySpan<byte> bytes)
    {
        if (OperatingSystem.IsMacOS())
            return new(MacImages.Decode(bytes));
        return OperatingSystem.IsWindows() ? new(WindowsImages.Decode(bytes)) : OperatingSystem.IsLinux() ? new(LinuxImages.Decode(bytes)) : new(Png.Read(bytes));
    }

    /// <summary>Loads an SVG file. See <see cref="Svg"/>.</summary>
    public static Image LoadSvg(
        string path,
        float width,
        float height,
        float scale = 2
    ) => Svg(File.ReadAllBytes(path), width, height, scale);
    /// <summary>
    /// Rasterizes an SVG document to width × height points at the given pixel scale, fitted and
    /// centered with its aspect ratio preserved. Render at the display scale you draw with. Uses
    /// AppKit on macOS, librsvg on Linux and Direct2D on Windows 10 1703 or later.
    /// </summary>
    public static Image Svg(
        ReadOnlySpan<byte> bytes,
        float width,
        float height,
        float scale = 2
    ) => new(SvgRaster.Render(bytes, width, height, scale));
}

public sealed record Pixels(byte[] Bytes, int Width, int Height)
{
    public static Pixels Create(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(width));
        return new(new byte[checked(width * height * 4)], width, height);
    }
}
