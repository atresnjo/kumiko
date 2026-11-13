using System.Numerics;

namespace Kumiko.Rendering;

internal static class Icons
{
    internal static (byte[] Bytes, int Width, int Height) Raster(string name, float size, float scale)
    {
        int width = Math.Max(1, (int)MathF.Ceiling(size * scale));
        var bytes = new byte[width * width * 4];
        if (!IconData.All.TryGetValue(name, out var ops))
            ops = [new(IconOpKind.Box, [3, 3, 18, 18, 3])];
        var prepared = ops.Select(op => op.Kind == IconOpKind.Arc ? new IconOp(IconOpKind.Line, Arc(op.Values)) : op).ToArray();
        const int samples = 4;
        for (int y = 0; y < width; y++)
            for (int x = 0; x < width; x++)
            {
                int coverage = 0;
                for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        var p = new Vector2((x + (sx + .5f) / samples) * 24 / width, (y + (sy + .5f) / samples) * 24 / width);
                        bool ink = false;
                        foreach (var op in prepared)
                            if (Contains(op, p))
                                ink = op.Kind != IconOpKind.Cut;
                        if (ink)
                            coverage++;
                    }

                int at = (y * width + x) * 4;
                bytes[at] = bytes[at + 1] = bytes[at + 2] = 255;
                bytes[at + 3] = (byte)(coverage * 255 / (samples * samples));
            }

        return (bytes, width, width);
    }

    static float[] Arc(float[] v)
    {
        var points = new float[66];
        for (int i = 0; i <= 32; i++)
        {
            float a = (v[3] + (v[4] - v[3]) * i / 32) * MathF.PI / 180;
            points[i * 2] = v[0] + v[2] * MathF.Cos(a);
            points[i * 2 + 1] = v[1] + v[2] * MathF.Sin(a);
        }

        return points;
    }

    static bool Contains(IconOp op, Vector2 p)
    {
        var v = op.Values;
        const float half = .9f;
        if (op.Kind is IconOpKind.Circle or IconOpKind.Disc)
        {
            float d = Vector2.Distance(p, new(v[0], v[1]));
            return op.Kind == IconOpKind.Disc ? d <= v[2] : Math.Abs(d - v[2]) <= half;
        }

        if (op.Kind is IconOpKind.Box or IconOpKind.Solid)
        {
            var (d, _) = CpuRenderer.Distance(p.X - v[0] - v[2] / 2, p.Y - v[1] - v[3] / 2, v[2] / 2, v[3] / 2, v[4]);
            return op.Kind == IconOpKind.Solid ? d <= 0 : Math.Abs(d) <= half;
        }

        if (op.Kind == IconOpKind.Fill)
        {
            bool inside = false;
            int count = v.Length / 2;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                var a = new Vector2(v[i * 2], v[i * 2 + 1]);
                var b = new Vector2(v[j * 2], v[j * 2 + 1]);
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }

            return inside;
        }

        int length = v.Length / 2, segments = op.Kind == IconOpKind.Loop ? length : length - 1;
        for (int i = 0; i < segments; i++)
        {
            int j = (i + 1) % length;
            var a = new Vector2(v[i * 2], v[i * 2 + 1]);
            var b = new Vector2(v[j * 2], v[j * 2 + 1]);
            var ab = b - a;
            float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
            if (Vector2.DistanceSquared(p, a + ab * t) <= half * half)
                return true;
        }

        return false;
    }
}
