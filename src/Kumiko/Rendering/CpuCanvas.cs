using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Rendering;

internal readonly record struct PixelRegion(int X0, int Y0, int X1, int Y1)
{
    internal long Area => (long)Math.Max(0, X1 - X0) * Math.Max(0, Y1 - Y0);
    internal static PixelRegion Whole(Pixels pixels) => new(0, 0, pixels.Width, pixels.Height);
    internal PixelRegion Union(PixelRegion other) => Area == 0 ? other : other.Area == 0 ? this :
        new(Math.Min(X0, other.X0), Math.Min(Y0, other.Y0), Math.Max(X1, other.X1), Math.Max(Y1, other.Y1));
}

/// <summary>Window-owned software pixels and the immutable inputs they represent.</summary>
internal sealed class CpuCanvas
{
    Pixels? pixels;
    Instance[] drawn = [];
    int count;
    Atlas? previousAtlas;
    byte[]? previousTexels;
    float previousScale;
    Size previousSize;
    Color previousClear;
    internal PixelRegion LastDamage
    {
        get; private set;
    }

    internal Pixels Capture(Context context)
    {
        var frame = Render(context);
        return frame with
        {
            Bytes = (byte[])frame.Bytes.Clone()
        };
    }

    internal Pixels Render(Context context)
    {
        context.CheckThread();
        return Render(context.Instances, context.TextSystem.Atlas, context.Size, context.Scale, context.Theme.Background);
    }

    internal Pixels Render(IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear)
    {
        int width = Math.Max(1, (int)MathF.Ceiling(size.Width * scale));
        int height = Math.Max(1, (int)MathF.Ceiling(size.Height * scale));
        bool whole = pixels is null || pixels.Width != width || pixels.Height != height ||
            previousSize != size || previousScale != scale || previousClear != clear ||
            previousAtlas != atlas || previousTexels != atlas.Bytes;
        if (pixels is null || pixels.Width != width || pixels.Height != height)
            pixels = Pixels.Create(width, height);
        LastDamage = whole ? PixelRegion.Whole(pixels) : Damage(instances, scale);
        if (LastDamage.Area == 0)
            return pixels;
        // Copy before drawing: workers only see a stable instance/atlas snapshot.
        if (drawn.Length < instances.Count)
            Array.Resize(ref drawn, Math.Max(instances.Count, drawn.Length * 2));
        for (int i = 0; i < instances.Count; i++)
            drawn[i] = instances[i];
        count = instances.Count;
        previousAtlas = null;
        CpuRenderer.DrawRegion(pixels, new ArraySegment<Instance>(drawn, 0, count), atlas, scale, clear, LastDamage);
        previousSize = size;
        previousScale = scale;
        previousClear = clear;
        previousAtlas = atlas;
        // Insert only appends texels. Reset/growth replace the backing storage;
        // new glyphs in the same storage are covered by changed instances.
        previousTexels = atlas.Bytes;
        return pixels;
    }

    PixelRegion Damage(IReadOnlyList<Instance> instances, float scale)
    {
        PixelRegion region = default;
        bool glass = false;
        int length = Math.Max(count, instances.Count);
        for (int i = 0; i < length; i++)
        {
            if (i < count && drawn[i].Kind == 4 || i < instances.Count && instances[i].Kind == 4)
                glass = true;
            if (i < count && i < instances.Count && Same(drawn[i], instances[i]))
                continue;
            if (i < count)
                region = region.Union(CpuRenderer.Bounds(pixels!, drawn[i], scale));
            if (i < instances.Count)
                region = region.Union(CpuRenderer.Bounds(pixels!, instances[i], scale));
        }
        // Glass depends on pixels outside its bounds, including earlier panes.
        return glass && region.Area > 0 ? PixelRegion.Whole(pixels!) : region;
    }

    static bool Same(Instance a, Instance b) =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref a, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref b, 1)));
}
