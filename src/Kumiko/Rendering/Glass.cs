// Adapted from mygo's glass plugin, Copyright (c) 2026 MyGo contributors (MIT;
// see third_party/licenses/mygo-MIT.txt).
using System.Numerics;
using Kumiko.UI;

namespace Kumiko.Rendering;

internal static class Glass
{
    internal static Instance Instance(GlassStyle glass, Rect rect, Rect clip, float radius, float opacity, bool dark, float scale)
    {
        float small = Math.Min(rect.Width, rect.Height), bezel = Math.Min(36, small / 2), blur = Math.Clamp(small * .035f, 1, 10);
        var tone = new Vector4(.541f, 1, 1.2f, 1);
        if (glass.Clear)
        {
            blur = 1;
            tone = new(.125f, 1.082f, 1, 1);
        }
        else if (dark)
            tone = new(.15f, .51f, 2, 2.2f);
        var tint = glass.Tint == default ? Color.Transparent : glass.Tint.Alpha(.88f);
        var i = Rendering.Instance.Quad(rect, tint, clip, radius);
        i.Kind = 4;
        i.UV = new(bezel, bezel * 1.6f, opacity, 1 / scale);
        i.BorderColor = tone;
        i.BorderWidth = blur;
        return i;
    }

    internal static void Draw(Pixels output, Instance i, float scale, int x0, int y0, int x1, int y1)
    {
        float blur = i.BorderWidth * scale;
        int down = 1;
        while (down < 8 && blur / (down * 2) >= 2)
            down *= 2;
        float sigma = MathF.Sqrt(Math.Max(0, blur * blur - down * down / 12f)) / down;
        int radius = (int)MathF.Ceiling(sigma * 3);
        int reach = (radius + 1) * down;
        int left = Math.Max(0, x0 - reach), top = Math.Max(0, y0 - reach), right = Math.Min(output.Width, x1 + reach), bottom = Math.Min(output.Height, y1 + reach);
        int w = (right - left + down - 1) / down, h = (bottom - top + down - 1) / down;
        var pixels = new Vector3[w * h];
        var scratch = new Vector3[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector3 sum = default;
                int count = 0;
                for (int yy = top + y * down; yy < Math.Min(bottom, top + (y + 1) * down); yy++)
                    for (int xx = left + x * down; xx < Math.Min(right, left + (x + 1) * down); xx++)
                    {
                        int p = (yy * output.Width + xx) * 4;
                        sum += new Vector3(output.Bytes[p + 2], output.Bytes[p + 1], output.Bytes[p]);
                        count++;
                    }

                pixels[y * w + x] = sum / (255 * count);
            }

        var weights = new float[radius * 2 + 1];
        float total = 0;
        for (int k = -radius; k <= radius; k++)
            total += weights[k + radius] = sigma > 0 ? MathF.Exp(-k * k / (2 * sigma * sigma)) : 1;
        for (int k = 0; k < weights.Length; k++)
            weights[k] /= total;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Vector3 sum = default;
                    for (int k = -radius; k <= radius; k++)
                        sum += pixels[Math.Clamp(y + (pass == 1 ? k : 0), 0, h - 1) * w + Math.Clamp(x + (pass == 0 ? k : 0), 0, w - 1)] * weights[k + radius];
                    scratch[y * w + x] = sum;
                }

            (pixels, scratch) = (scratch, pixels);
        }

        Vector3 Sample(float x, float y)
        {
            float u = (x - left) / down - .5f, v = (y - top) / down - .5f;
            int xx = (int)MathF.Floor(u), yy = (int)MathF.Floor(v);
            float tx = u - xx, ty = v - yy;
            Vector3 At(int a, int b) => pixels[Math.Clamp(b, 0, h - 1) * w + Math.Clamp(a, 0, w - 1)];
            return Vector3.Lerp(Vector3.Lerp(At(xx, yy), At(xx + 1, yy), tx), Vector3.Lerp(At(xx, yy + 1), At(xx + 1, yy + 1), tx), ty);
        }

        float hw = i.Rect.Z / 2, hh = i.Rect.W / 2, r = Math.Min(i.Radius, Math.Min(hw, hh)), inverse = 1 / scale;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                float wx = (x + .5f) * inverse, wy = (y + .5f) * inverse, qx = wx - i.Rect.X - hw, qy = wy - i.Rect.Y - hh;
                var (distance, gradient) = CpuRenderer.Distance(qx, qy, hw, hh, r);
                float aa = Math.Max(gradient * inverse, .0001f) * .5f;
                float coverage = (1 - CpuRenderer.Smooth(-aa, aa, distance)) * CpuRenderer.ClipCoverage(i, wx, wy, inverse) * i.UV.Z;
                if (coverage <= 0)
                    continue;
                float depth = Math.Max(0, -distance), bezel = i.UV.X;
                Vector2 normal = default;
                if (depth < bezel || depth < 3 * i.UV.W)
                {
                    float rr = Math.Min(r * 1.5f, Math.Min(hw, hh));
                    var a = new Vector2(Math.Abs(qx) - hw + rr, Math.Abs(qy) - hh + rr);
                    if (a.X > 0 || a.Y > 0)
                        normal = Vector2.Normalize(Vector2.Max(a, Vector2.Zero)) * new Vector2(Math.Sign(qx), Math.Sign(qy));
                    else
                        normal = a.X > a.Y ? new(Math.Sign(qx), 0) : new(0, Math.Sign(qy));
                }

                float bend = 0;
                if (depth < bezel && bezel > 0)
                {
                    float u = 1 - depth / bezel, v = 1 - u * u * u * u, a = MathF.Sqrt(v * MathF.Sqrt(v)), b = u * u * u, n = MathF.Sqrt(a * a + b * b), sn = b / n, cs = a / n, st = sn / 1.5f, ct = MathF.Sqrt(1 - st * st);
                    bend = i.UV.Y * (sn * ct - cs * st) / (cs * ct + sn * st) / 1.118034f;
                }

                var c = Sample((wx - normal.X * bend) * scale, (wy - normal.Y * bend) * scale);
                float l = Vector3.Dot(c, new(.2126f, .7152f, .0722f));
                var tone = i.BorderColor;
                float level = tone.X + (tone.Y - tone.X) * (1 - MathF.Pow(Math.Max(1 - l, 0), tone.Z));
                c = Vector3.Clamp(new Vector3(level) + (c - new Vector3(l)) * ((tone.Y - tone.X) * tone.W), Vector3.Zero, Vector3.One);
                c = Vector3.Lerp(c, new(i.Color.X, i.Color.Y, i.Color.Z), i.Color.W);
                if (depth < 3 * i.UV.W)
                {
                    float v = depth / i.UV.W;
                    c = Vector3.Lerp(c, new(Math.Abs(normal.Y)), .35f * MathF.Exp(-v * v));
                }

                CpuRenderer.Blend(output.Bytes, (y * output.Width + x) * 4, new(c * coverage, coverage));
            }
    }
}
