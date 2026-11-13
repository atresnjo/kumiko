using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Kumiko.Rendering;
// TrueType outlines, advances and cmap 4/12. All bounds checked by Span.
internal sealed class TrueType
{
    readonly byte[] data;
    readonly Dictionary<string, int> tables = new(StringComparer.Ordinal);
    readonly int units, hmetrics, glyphs, locaFormat, cmap;
    internal float Ascent
    {
        get;
    }
    internal float Descent
    {
        get;
    }
    internal float LineGap
    {
        get;
    }

    int U16(int p) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p, 2));
    int I16(int p) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(p, 2));
    int U32(int p) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4)));
    internal TrueType(string path)
    {
        data = File.ReadAllBytes(path);
        int face = Encoding.ASCII.GetString(data, 0, 4) == "ttcf" ? U32(12) : 0;
        for (int i = 0; i < U16(face + 4); i++)
        {
            int p = face + 12 + i * 16;
            tables[Encoding.ASCII.GetString(data, p, 4)] = U32(p + 8);
        }

        if (!tables.ContainsKey("glyf"))
            throw new NotSupportedException("The font has no TrueType outlines.");
        int head = tables["head"], hhea = tables["hhea"];
        units = U16(head + 18);
        locaFormat = I16(head + 50);
        hmetrics = U16(hhea + 34);
        glyphs = U16(tables["maxp"] + 4);
        Ascent = I16(hhea + 4) / (float)units;
        Descent = -I16(hhea + 6) / (float)units;
        LineGap = I16(hhea + 8) / (float)units;
        int cm = tables["cmap"], selected = 0;
        for (int i = 0; i < U16(cm + 2); i++)
        {
            int p = cm + 4 + i * 8, sub = cm + U32(p + 4), format = U16(sub);
            if (format == 12)
            {
                selected = sub;
                break;
            }

            if (format == 4)
                selected = sub;
        }

        cmap = selected != 0 ? selected : throw new NotSupportedException("Font has no Unicode character map.");
    }

    internal ushort Glyph(int code)
    {
        if (U16(cmap) == 12)
        {
            int low = 0, high = U32(cmap + 12) - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2, p = cmap + 16 + mid * 12, start = U32(p), end = U32(p + 4);
                if (code < start)
                    high = mid - 1;
                else if (code > end)
                    low = mid + 1;
                else
                    return (ushort)(U32(p + 8) + code - start);
            }

            return 0;
        }

        if (code > 65535)
            return 0;
        int count = U16(cmap + 6) / 2, ends = cmap + 14, starts = ends + 2 * count + 2, deltas = starts + 2 * count, ranges = deltas + 2 * count;
        for (int i = 0; i < count; i++)
        {
            if (code > U16(ends + 2 * i))
                continue;
            if (code < U16(starts + 2 * i))
                return 0;
            int delta = I16(deltas + 2 * i), range = U16(ranges + 2 * i);
            if (range == 0)
                return (ushort)(code + delta);
            int glyph = U16(ranges + 2 * i + range + 2 * (code - U16(starts + 2 * i)));
            return glyph == 0 ? (ushort)0 : (ushort)(glyph + delta);
        }

        return 0;
    }

    internal float Advance(ushort glyph) => U16(tables["hmtx"] + 4 * Math.Min(glyph, hmetrics - 1)) / (float)units;
    internal float Kern(ushort left, ushort right)
    {
        if (!tables.TryGetValue("kern", out int table))
            return 0;
        int p = table + 4;
        for (int n = 0; n < U16(table + 2); n++)
        {
            int length = U16(p + 2), coverage = U16(p + 4);
            if ((coverage & 0xff01) == 1)
            {
                uint key = ((uint)left << 16) | right;
                int low = 0, high = U16(p + 6) - 1;
                while (low <= high)
                {
                    int mid = (low + high) / 2, at = p + 14 + mid * 6;
                    uint pair = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at, 4));
                    if (pair < key)
                        low = mid + 1;
                    else if (pair > key)
                        high = mid - 1;
                    else
                        return I16(at + 4) / (float)units;
                }
            }

            if (length < 6)
                break;
            p += length;
        }

        return 0;
    }

    record struct Vertex(Vector2 Point, bool On);
    internal (byte[] Rgba, int Width, int Height, float X, float Y) Raster(ushort glyph, float size)
    {
        var contours = new List<List<Vertex>>();
        ReadOutline(glyph, Matrix3x2.Identity, contours, 0);
        if (contours.Count == 0)
            return ([], 0, 0, 0, 0);
        float scale = size / units;
        var points = contours.SelectMany(c => c).Select(v => v.Point).ToArray();
        int x0 = (int)MathF.Floor(points.Min(p => p.X) * scale) - 1, x1 = (int)MathF.Ceiling(points.Max(p => p.X) * scale) + 1;
        int y0 = (int)MathF.Floor(-points.Max(p => p.Y) * scale) - 1, y1 = (int)MathF.Ceiling(-points.Min(p => p.Y) * scale) + 1;
        int width = x1 - x0, height = y1 - y0;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            return ([], 0, 0, 0, 0);
        var edges = new List<(Vector2 A, Vector2 B)>();
        foreach (var contour in contours)
        {
            var expanded = new List<Vertex>();
            for (int i = 0; i < contour.Count; i++)
            {
                var a = contour[i];
                var b = contour[(i + 1) % contour.Count];
                expanded.Add(a);
                if (!a.On && !b.On)
                    expanded.Add(new((a.Point + b.Point) / 2, true));
            }

            int first = expanded.FindIndex(v => v.On);
            if (first < 0)
                continue;
            Vector2 Transform(Vector2 p) => new(p.X * scale - x0, -p.Y * scale - y0);
            var pen = Transform(expanded[first].Point);
            var start = pen;
            for (int k = 1; k <= expanded.Count; k++)
            {
                var v = expanded[(first + k) % expanded.Count];
                if (v.On)
                {
                    var next = Transform(v.Point);
                    edges.Add((pen, next));
                    pen = next;
                }
                else
                {
                    var control = Transform(v.Point);
                    var end = Transform(expanded[(first + k + 1) % expanded.Count].Point);
                    var origin = pen;
                    int steps = Math.Clamp((int)MathF.Ceiling((Vector2.Distance(origin, control) + Vector2.Distance(control, end)) / 3), 2, 32);
                    for (int j = 1; j <= steps; j++)
                    {
                        float t = j / (float)steps;
                        var next = (1 - t) * (1 - t) * origin + 2 * (1 - t) * t * control + t * t * end;
                        edges.Add((pen, next));
                        pen = next;
                    }

                    k++;
                }
            }

            if (pen != start)
                edges.Add((pen, start));
        }

        var coverage = new float[width * height];
        var intersections = new List<(float X, int Winding)>();
        const int samples = 4;
        for (int sy = 0; sy < height * samples; sy++)
        {
            float yy = (sy + .5f) / samples;
            intersections.Clear();
            foreach (var (a, b) in edges)
            {
                if ((a.Y <= yy && b.Y > yy) || (b.Y <= yy && a.Y > yy))
                    intersections.Add((a.X + (yy - a.Y) * (b.X - a.X) / (b.Y - a.Y), b.Y > a.Y ? 1 : -1));
            }

            intersections.Sort(static (a, b) => a.X.CompareTo(b.X));
            int winding = 0;
            float start = 0;
            foreach (var hit in intersections)
            {
                if (winding == 0)
                    start = hit.X;
                winding += hit.Winding;
                if (winding != 0)
                    continue;
                for (int xx = Math.Max(0, (int)MathF.Floor(start)); xx < Math.Min(width, (int)MathF.Ceiling(hit.X)); xx++)
                    coverage[sy / samples * width + xx] += Math.Max(0, Math.Min(xx + 1, hit.X) - Math.Max(xx, start)) / samples;
            }
        }

        var rgba = new byte[width * height * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = (byte)Math.Clamp((int)(coverage[i] * 255 + .5f), 0, 255);
        }

        return (rgba, width, height, x0, y0);
    }

    void ReadOutline(int glyph, Matrix3x2 transform, List<List<Vertex>> output, int depth)
    {
        if (depth > 12 || glyph >= glyphs)
            return;
        int loca = tables["loca"];
        int Offset(int g) => locaFormat == 0 ? U16(loca + g * 2) * 2 : U32(loca + g * 4);
        int offset = Offset(glyph);
        if (offset == Offset(glyph + 1))
            return;
        int p = tables["glyf"] + offset, contourCount = I16(p);
        p += 10;
        if (contourCount < 0)
        {
            int flags;
            do
            {
                flags = U16(p);
                int component = U16(p + 2);
                p += 4;
                int a, b;
                if ((flags & 1) != 0)
                {
                    a = I16(p);
                    b = I16(p + 2);
                    p += 4;
                }
                else
                {
                    a = (sbyte)data[p];
                    b = (sbyte)data[p + 1];
                    p += 2;
                }

                var m = Matrix3x2.Identity;
                if ((flags & 2) != 0)
                {
                    m.M31 = a;
                    m.M32 = b;
                }

                if ((flags & 8) != 0)
                {
                    m.M11 = m.M22 = I16(p) / 16384f;
                    p += 2;
                }

                if ((flags & 64) != 0)
                {
                    m.M11 = I16(p) / 16384f;
                    m.M22 = I16(p + 2) / 16384f;
                    p += 4;
                }

                if ((flags & 128) != 0)
                {
                    m.M11 = I16(p) / 16384f;
                    m.M12 = I16(p + 2) / 16384f;
                    m.M21 = I16(p + 4) / 16384f;
                    m.M22 = I16(p + 6) / 16384f;
                    p += 8;
                }

                ReadOutline(component, m * transform, output, depth + 1);
            }
            while ((flags & 32) != 0);
            return;
        }

        if (contourCount == 0)
            return;
        var ends = new int[contourCount];
        for (int i = 0; i < ends.Length; i++)
        {
            ends[i] = U16(p);
            p += 2;
        }

        int count = ends[^1] + 1;
        int instructions = U16(p);
        p += 2 + instructions;
        var flagsArray = new byte[count];
        for (int i = 0; i < count; i++)
        {
            byte f = data[p++];
            flagsArray[i] = f;
            if ((f & 8) != 0)
            {
                int repeat = data[p++];
                for (int j = 0; j < repeat; j++)
                    flagsArray[++i] = f;
            }
        }

        var xs = new int[count];
        var ys = new int[count];
        int value = 0;
        for (int i = 0; i < count; i++)
        {
            int f = flagsArray[i];
            value += (f & 2) != 0 ? ((f & 16) != 0 ? data[p++] : -data[p++]) : (f & 16) != 0 ? 0 : ReadShort();
            xs[i] = value;
        }

        value = 0;
        for (int i = 0; i < count; i++)
        {
            int f = flagsArray[i];
            value += (f & 4) != 0 ? ((f & 32) != 0 ? data[p++] : -data[p++]) : (f & 32) != 0 ? 0 : ReadShort();
            ys[i] = value;
        }

        int firstPoint = 0;
        foreach (int end in ends)
        {
            var vertices = new List<Vertex>(end - firstPoint + 1);
            for (int i = firstPoint; i <= end; i++)
                vertices.Add(new(Vector2.Transform(new(xs[i], ys[i]), transform), (flagsArray[i] & 1) != 0));
            output.Add(vertices);
            firstPoint = end + 1;
        }

        int ReadShort()
        {
            int v = I16(p);
            p += 2;
            return v;
        }
    }

    internal static string FindFont(bool mono, bool bold)
    {
        string[] candidates = OperatingSystem.IsMacOS() ? [mono ? "/System/Library/Fonts/Menlo.ttc" : bold ? "/System/Library/Fonts/Supplemental/Arial Bold.ttf" : "/System/Library/Fonts/Supplemental/Arial.ttf"] : OperatingSystem.IsWindows() ? [Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts),
            mono ? "consola.ttf" : bold ? "arialbd.ttf" : "arial.ttf"
        )] : [mono ? "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf" : bold ? "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf"];
        return candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Install a TrueType font (DejaVu Sans on Linux).");
    }
}
