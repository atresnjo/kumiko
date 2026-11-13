using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kumiko.UI;

namespace Kumiko.Rendering;

public static class CpuRenderer
{
    public static Pixels Render(IReadOnlyList<Instance> instances, Atlas atlas, Size size, float scale, Color clear)
    {
        var pixels = Pixels.Create(Math.Max(1, (int)MathF.Ceiling(size.Width * scale)), Math.Max(1, (int)MathF.Ceiling(size.Height * scale)));
        Draw(pixels, instances, atlas, scale, clear);
        return pixels;
    }

    public static void Draw(Pixels output, IReadOnlyList<Instance> instances, Atlas atlas, float scale, Color clear) => DrawRegion(output, instances, atlas, scale, clear, PixelRegion.Whole(output), parallel: false);
    internal static void DrawRegion(Pixels output, IReadOnlyList<Instance> instances, Atlas atlas, float scale, Color clear, PixelRegion region, bool parallel = true)
    {
        uint color = (uint)(Unit(clear.B * clear.A) | (Unit(clear.G * clear.A) << 8) | (Unit(clear.R * clear.A) << 16) | (Unit(clear.A) << 24));
        int bands = parallel ? Math.Min(Math.Min(Environment.ProcessorCount, 8), (int)(region.Area / 131072)) : 1;
        bands = Math.Max(1, Math.Min(bands, region.Y1 - region.Y0));
        int from = 0;
        bool first = true;
        while (true)
        {
            int end = from;
            while (end < instances.Count && instances[end].Kind != 4)
                end++;
            void Rows(int band)
            {
                int y0 = region.Y0 + (region.Y1 - region.Y0) * band / bands;
                int y1 = region.Y0 + (region.Y1 - region.Y0) * (band + 1) / bands;
                var rows = new PixelRegion(region.X0, y0, region.X1, y1);
                if (first)
                    for (int y = y0; y < y1; y++)
                        MemoryMarshal.Cast<byte, uint>(output.Bytes.AsSpan((y * output.Width + region.X0) * 4, (region.X1 - region.X0) * 4)).Fill(color);
                for (int i = from; i < end; i++)
                    DrawOne(output, instances[i], atlas, scale, rows);
            }

            if (bands > 1)
                Parallel.For(0, bands, Rows);
            else
                Rows(0);
            first = false;
            if (end == instances.Count)
                break;
            // Joining the bands before each pane gives glass a complete backdrop.
            DrawOne(output, instances[end], atlas, scale, region);
            from = end + 1;
        }
    }

    internal static byte Unit(float f) => (byte)Math.Clamp((int)(f * 255 + .5f), 0, 255);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    internal static (float Distance, float Gradient) Distance(float x, float y, float hw, float hh, float r)
    {
        float qx = Math.Abs(x) - hw + r, qy = Math.Abs(y) - hh + r, mx = Math.Max(qx, 0), my = Math.Max(qy, 0), length = MathF.Sqrt(mx * mx + my * my);
        return (length + Math.Min(Math.Max(qx, qy), 0) - r, length > 0 ? (mx + my) / length : 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Blend(byte[] bytes, int at, Vector4 color)
    {
        float a = Math.Clamp(color.W, 0, 1), inverse = 1 - a;
        if (a <= 0)
            return;
        bytes[at] = Unit(color.Z + bytes[at] / 255f * inverse);
        bytes[at + 1] = Unit(color.Y + bytes[at + 1] / 255f * inverse);
        bytes[at + 2] = Unit(color.X + bytes[at + 2] / 255f * inverse);
        bytes[at + 3] = Unit(a + bytes[at + 3] / 255f * inverse);
    }

    internal static float ClipCoverage(Instance i, float x, float y, float inverse)
    {
        if (i.ClipRound <= 0)
            return 1;
        float corners = MathF.Floor(i.ClipRound / 4096), radius = i.ClipRound - corners * 4096;
        float hw = (i.Clip.Z - i.Clip.X) / 2, hh = (i.Clip.W - i.Clip.Y) / 2, qx = x - i.Clip.X - hw, qy = y - i.Clip.Y - hh;
        int bit = qy < 0 ? (qx < 0 ? 1 : 2) : (qx < 0 ? 8 : 4);
        if (((int)corners & bit) == 0)
            return 1;
        var (distance, gradient) = Distance(qx, qy, hw, hh, Math.Min(radius, Math.Min(hw, hh)));
        float aa = Math.Max(gradient * inverse, .0001f) * .5f;
        return 1 - Smooth(-aa, aa, distance);
    }

    internal static PixelRegion Bounds(Pixels output, Instance i, float scale) => Bounds(output.Width, output.Height, i, scale);
    internal static PixelRegion Bounds(int width, int height, Instance i, float scale)
    {
        float grow = i.Kind == 2 ? i.BorderWidth * 3 : 0;
        float left = Math.Max(i.Rect.X - grow, i.Clip.X), top = Math.Max(i.Rect.Y - grow, i.Clip.Y);
        float right = Math.Min(i.Rect.X + i.Rect.Z + grow, i.Clip.Z), bottom = Math.Min(i.Rect.Y + i.Rect.W + grow, i.Clip.W);
        if (i.Turn.X != 0 && i.Kind != 4 && left < right && top < bottom)
        {
            // A turned instance covers the box around its turned corners.
            float sin = MathF.Sin(i.Turn.X), cos = MathF.Cos(i.Turn.X), px = i.Turn.Y, py = i.Turn.Z;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var (cx, cy) in new[]
            {
                (left, top),
                (right, top),
                (left, bottom),
                (right, bottom)
            }

            )
            {
                float dx = cx - px, dy = cy - py, tx = px + dx * cos - dy * sin, ty = py + dx * sin + dy * cos;
                minX = Math.Min(minX, tx);
                minY = Math.Min(minY, ty);
                maxX = Math.Max(maxX, tx);
                maxY = Math.Max(maxY, ty);
            }

            return new(Math.Clamp((int)MathF.Floor(minX * scale), 0, width), Math.Clamp((int)MathF.Floor(minY * scale), 0, height), Math.Clamp((int)MathF.Ceiling(maxX * scale), 0, width), Math.Clamp((int)MathF.Ceiling(maxY * scale), 0, height));
        }

        int x0 = Math.Clamp((int)MathF.Round(left * scale), 0, width);
        int y0 = Math.Clamp((int)MathF.Round(top * scale), 0, height);
        int x1 = Math.Clamp((int)MathF.Round(right * scale), 0, width);
        int y1 = Math.Clamp((int)MathF.Round(bottom * scale), 0, height);
        return new(x0, y0, x1, y1);
    }

    static void DrawOne(Pixels output, Instance i, Atlas atlas, float scale, PixelRegion region)
    {
        if (i.Turn.X != 0 && i.Kind != 4)
        {
            DrawTurned(output, i, atlas, scale, region);
            return;
        }

        var bounds = Bounds(output, i, scale);
        int x0 = Math.Max(bounds.X0, region.X0), y0 = Math.Max(bounds.Y0, region.Y0);
        int x1 = Math.Min(bounds.X1, region.X1), y1 = Math.Min(bounds.Y1, region.Y1);
        if (x0 >= x1 || y0 >= y1)
            return;
        if (i.Kind == 4)
        {
            Glass.Draw(output, i, scale, x0, y0, x1, y1);
            return;
        }

        float inverse = 1 / scale, hw = i.Rect.Z / 2, hh = i.Rect.W / 2, r = Math.Min(i.Radius, Math.Min(hw, hh));
        if (i.Kind == 0 && i.Radius == 0 && i.BorderWidth == 0 && i.Color.W >= 1 && i.ClipRound == 0)
        {
            uint color = (uint)(Unit(i.Color.Z) | (Unit(i.Color.Y) << 8) | (Unit(i.Color.X) << 16) | unchecked((int)0xff000000));
            for (int y = y0; y < y1; y++)
                MemoryMarshal.Cast<byte, uint>(output.Bytes.AsSpan((y * output.Width + x0) * 4, (x1 - x0) * 4)).Fill(color);
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            float wy = (y + .5f) * inverse;
            for (int x = x0; x < x1; x++)
            {
                var color = Shade(i, atlas, (x + .5f) * inverse, wy, inverse, hw, hh, r);
                if (color.W > 0)
                    Blend(output.Bytes, (y * output.Width + x) * 4, color);
            }
        }
    }

    /// <summary>
    /// Draws a turned instance: each pixel maps back into the upright frame, where clipping and shading happen as for
    /// any other instance.
    /// </summary>
    static void DrawTurned(Pixels output, Instance i, Atlas atlas, float scale, PixelRegion region)
    {
        var bounds = Bounds(output, i, scale);
        int x0 = Math.Max(bounds.X0, region.X0), y0 = Math.Max(bounds.Y0, region.Y0);
        int x1 = Math.Min(bounds.X1, region.X1), y1 = Math.Min(bounds.Y1, region.Y1);
        float inverse = 1 / scale, sin = MathF.Sin(i.Turn.X), cos = MathF.Cos(i.Turn.X), px = i.Turn.Y, py = i.Turn.Z;
        float hw = i.Rect.Z / 2, hh = i.Rect.W / 2, r = Math.Min(i.Radius, Math.Min(hw, hh));
        for (int y = y0; y < y1; y++)
        {
            float wy = (y + .5f) * inverse - py;
            for (int x = x0; x < x1; x++)
            {
                float wx = (x + .5f) * inverse - px;
                float ux = px + wx * cos + wy * sin, uy = py - wx * sin + wy * cos;
                if (ux < i.Clip.X || uy < i.Clip.Y || ux > i.Clip.Z || uy > i.Clip.W)
                    continue;
                var color = Shade(i, atlas, ux, uy, inverse, hw, hh, r);
                if (color.W > 0)
                    Blend(output.Bytes, (y * output.Width + x) * 4, color);
            }
        }
    }

    /// <summary>The premultiplied color an instance gives the upright point (<paramref name = "wx"/>, <paramref name = "wy"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Vector4 Shade(Instance i, Atlas atlas, float wx, float wy, float inverse, float hw, float hh, float r)
    {
        float cut = ClipCoverage(i, wx, wy, inverse);
        if (cut <= 0)
            return default;
        if (i.Kind == 1)
        {
            float u = i.UV.X + (wx - i.Rect.X) / i.Rect.Z * (i.UV.Z - i.UV.X), v = i.UV.Y + (wy - i.Rect.Y) / i.Rect.W * (i.UV.W - i.UV.Y);
            float a = Sample(atlas, u, v).W * i.Color.W * cut;
            return new(i.Color.X * a, i.Color.Y * a, i.Color.Z * a, a);
        }

        if (i.Kind == 2)
        {
            float a = Shadow(wx - i.Rect.X - hw, wy - i.Rect.Y - hh, hw, hh, r, Math.Max(.25f, i.BorderWidth)) * i.Color.W * cut;
            return new(i.Color.X * a, i.Color.Y * a, i.Color.Z * a, a);
        }

        var (distance, gradient) = Distance(wx - i.Rect.X - hw, wy - i.Rect.Y - hh, hw, hh, r);
        float aa = Math.Max(gradient * inverse, .0001f) * .5f, coverage = (1 - Smooth(-aa, aa, distance)) * cut;
        if (coverage <= 0)
            return default;
        if (i.Kind == 3)
        {
            float u = i.UV.X + (wx - i.Rect.X) / i.Rect.Z * (i.UV.Z - i.UV.X), v = i.UV.Y + (wy - i.Rect.Y) / i.Rect.W * (i.UV.W - i.UV.Y);
            return Sample(atlas, u, v) * (coverage * i.Color.W);
        }

        var c = i.Color;
        if (i.BorderWidth > 0 && i.BorderColor.W > 0)
            c = Vector4.Lerp(c, i.BorderColor, Smooth(-i.BorderWidth - aa, -i.BorderWidth + aa, distance));
        float alpha = c.W * coverage;
        return new(c.X * alpha, c.Y * alpha, c.Z * alpha, alpha);
    }

    internal static Vector4 Sample(Atlas atlas, float x, float y)
    {
        x -= .5f;
        y -= .5f;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        Vector4 At(int xx, int yy)
        {
            int p = (Math.Clamp(yy, 0, atlas.Width - 1) * atlas.Width + Math.Clamp(xx, 0, atlas.Width - 1)) * 4;
            return new(atlas.Bytes[p + 2] / 255f, atlas.Bytes[p + 1] / 255f, atlas.Bytes[p] / 255f, atlas.Bytes[p + 3] / 255f);
        }

        return Vector4.Lerp(Vector4.Lerp(At(x0, y0), At(x0 + 1, y0), tx), Vector4.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx), ty);
    }

    static float Erf(float x)
    {
        float a = Math.Abs(x), y = 1 + (.278393f + (.230389f + .078108f * a * a) * a) * a;
        y *= y;
        return MathF.CopySign(1 - 1 / (y * y), x);
    }

    static float Shadow(float x, float y, float hw, float hh, float corner, float sigma)
    {
        float start = Math.Clamp(-3 * sigma, y - hh, y + hh), end = Math.Clamp(3 * sigma, y - hh, y + hh), step = (end - start) / 4, alpha = 0;
        for (int k = 0; k < 4; k++)
        {
            float yy = start + step * (k + .5f), dy = y - yy, delta = Math.Min(hh - corner - Math.Abs(dy), 0);
            float curved = hw - corner + MathF.Sqrt(Math.Max(0, corner * corner - delta * delta)), f = .70710678f / sigma;
            float integral = .5f * (Erf((x + curved) * f) - Erf((x - curved) * f));
            alpha += integral * MathF.Exp(-yy * yy / (2 * sigma * sigma)) / (2.5066283f * sigma) * step;
        }

        return alpha;
    }
}
