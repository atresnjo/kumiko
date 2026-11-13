using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using CoreGraphics;

namespace Kumiko.iOS;

/// <summary>One drawable element of a monochrome icon, in viewBox units.</summary>
internal sealed record SvgIconShape(CGPath Path, bool Fill, bool Stroke, float StrokeWidth, string LineCap, string LineJoin,
    bool EvenOdd, float Opacity);

/// <summary>A tintable SVG icon document parsed into native paths.</summary>
internal sealed record SvgIconGeometry(CGRect ViewBox, IReadOnlyList<SvgIconShape> Shapes)
{
    static readonly ConcurrentDictionary<string, SvgIconGeometry> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Parses the subset monochrome icon sets use: path (absolute and relative M, L, H, V, C, S, Q, T, Z), circle,
    /// ellipse and rect, with currentColor fill/stroke, stroke width, caps, joins, fill rule, opacity and matrix
    /// transforms. Anything else throws, naming the icon, instead of drawing nothing.
    /// </summary>
    internal static SvgIconGeometry Parse(string name, string svg) => Cache.GetOrAdd(svg, document =>
    {
        try
        {
            return Read(document);
        }
        catch (Exception error) when (error is FormatException or XmlException or NotSupportedException)
        {
            throw new NotSupportedException($"Icon '{name}' uses SVG the native path cannot draw: {error.Message}", error);
        }
    });

    static SvgIconGeometry Read(string svg)
    {
        using var text = new StringReader(svg);
        using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var shapes = new List<SvgIconShape>();
        var inherited = new Dictionary<string, string>(StringComparer.Ordinal);
        CGRect? box = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            var attributes = new Dictionary<string, string>(inherited, StringComparer.Ordinal);
            if (reader.MoveToFirstAttribute())
            {
                do
                    attributes[reader.LocalName] = reader.Value;
                while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
            switch (reader.LocalName)
            {
                case "svg":
                    box = ReadViewBox(attributes);
                    foreach (var key in (string[])["fill", "stroke", "stroke-width", "stroke-linecap", "stroke-linejoin", "fill-rule"])
                        if (attributes.TryGetValue(key, out var value))
                            inherited[key] = value;
                    break;
                case "path":
                    shapes.Add(Shape(attributes, PathData(Required(attributes, "d"))));
                    break;
                case "circle":
                    var r = Number(attributes, "r");
                    shapes.Add(Shape(attributes, Ellipse(Number(attributes, "cx"), Number(attributes, "cy"), r, r)));
                    break;
                case "ellipse":
                    shapes.Add(Shape(attributes, Ellipse(Number(attributes, "cx"), Number(attributes, "cy"),
                        Number(attributes, "rx"), Number(attributes, "ry"))));
                    break;
                case "rect":
                    shapes.Add(Shape(attributes, Rect(attributes)));
                    break;
                default:
                    throw new NotSupportedException($"element <{reader.LocalName}>");
            }
        }
        return new(box ?? throw new FormatException("missing <svg> element"), shapes);
    }

    static CGRect ReadViewBox(Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("viewBox", out var box))
        {
            var values = box.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();
            if (values.Length != 4 || values[2] <= 0 || values[3] <= 0)
                throw new FormatException($"invalid viewBox '{box}'");
            return new(values[0], values[1], values[2], values[3]);
        }
        return new(0, 0, Number(attributes, "width"), Number(attributes, "height"));
    }

    static SvgIconShape Shape(Dictionary<string, string> attributes, CGPath path)
    {
        var fill = Paint(attributes, "fill", "currentColor");
        var stroke = Paint(attributes, "stroke", "none");
        if (attributes.TryGetValue("transform", out var transform))
        {
            var matrix = Matrix(transform);
            var moved = new CGPath();
            moved.AddPath(matrix, path);
            path = moved;
        }
        var opacity = attributes.TryGetValue("opacity", out var alpha) ? Parse(alpha) : 1;
        return new(path, fill, stroke, attributes.TryGetValue("stroke-width", out var width) ? Parse(width) : 1,
            attributes.GetValueOrDefault("stroke-linecap", "butt"), attributes.GetValueOrDefault("stroke-linejoin", "miter"),
            attributes.GetValueOrDefault("fill-rule") == "evenodd", opacity);
    }

    // Icons are tinted, so only currentColor and none are meaningful paints.
    static bool Paint(Dictionary<string, string> attributes, string key, string fallback) =>
        attributes.GetValueOrDefault(key, fallback) switch
        {
            "none" => false,
            "currentColor" => true,
            var other => throw new NotSupportedException($"{key}=\"{other}\"; tinted icons use currentColor or none")
        };

    static CGAffineTransform Matrix(string transform)
    {
        var text = transform.Trim();
        if (!text.StartsWith("matrix(", StringComparison.Ordinal) || !text.EndsWith(')'))
            throw new NotSupportedException($"transform \"{transform}\"");
        var values = text[7..^1].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(Parse).ToArray();
        if (values.Length != 6)
            throw new FormatException($"transform \"{transform}\"");
        return new(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    static CGPath Ellipse(float cx, float cy, float rx, float ry)
    {
        var path = new CGPath();
        path.AddEllipseInRect(new CGRect(cx - rx, cy - ry, rx * 2, ry * 2));
        return path;
    }

    static CGPath Rect(Dictionary<string, string> attributes)
    {
        var bounds = new CGRect(Optional(attributes, "x"), Optional(attributes, "y"), Number(attributes, "width"), Number(attributes, "height"));
        var rx = attributes.ContainsKey("rx") ? Number(attributes, "rx") : attributes.ContainsKey("ry") ? Number(attributes, "ry") : 0;
        var ry = attributes.ContainsKey("ry") ? Number(attributes, "ry") : rx;
        var path = new CGPath();
        if (rx > 0 || ry > 0)
            path.AddRoundedRect(bounds, Math.Min(rx, (float)bounds.Width / 2), Math.Min(ry, (float)bounds.Height / 2));
        else
            path.AddRect(bounds);
        return path;
    }

    static string Required(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : throw new FormatException($"missing {key}");
    static float Number(Dictionary<string, string> attributes, string key) => Parse(Required(attributes, key));
    static float Optional(Dictionary<string, string> attributes, string key) => attributes.TryGetValue(key, out var value) ? Parse(value) : 0;
    static float Parse(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    static CGPath PathData(string data)
    {
        var path = new CGPath();
        var tokens = new SvgPathTokens(data);
        float x = 0, y = 0, startX = 0, startY = 0, controlX = 0, controlY = 0;
        var command = '\0';
        var previous = '\0';
        while (tokens.Next(ref command))
        {
            var relative = char.IsLower(command);
            float ox = relative ? x : 0, oy = relative ? y : 0;
            var smooth = char.ToUpperInvariant(previous);
            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    x = ox + tokens.Number();
                    y = oy + tokens.Number();
                    path.MoveToPoint(x, y);
                    startX = x;
                    startY = y;
                    // Further coordinate pairs after a move are implicit line segments.
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    x = ox + tokens.Number();
                    y = oy + tokens.Number();
                    path.AddLineToPoint(x, y);
                    break;
                case 'H':
                    x = ox + tokens.Number();
                    path.AddLineToPoint(x, y);
                    break;
                case 'V':
                    y = oy + tokens.Number();
                    path.AddLineToPoint(x, y);
                    break;
                case 'C':
                case 'S':
                    float x1, y1;
                    if (char.ToUpperInvariant(command) == 'C')
                    {
                        x1 = ox + tokens.Number();
                        y1 = oy + tokens.Number();
                    }
                    else
                    {
                        x1 = smooth is 'C' or 'S' ? 2 * x - controlX : x;
                        y1 = smooth is 'C' or 'S' ? 2 * y - controlY : y;
                    }
                    controlX = ox + tokens.Number();
                    controlY = oy + tokens.Number();
                    x = ox + tokens.Number();
                    y = oy + tokens.Number();
                    path.AddCurveToPoint(x1, y1, controlX, controlY, x, y);
                    break;
                case 'Q':
                case 'T':
                    if (char.ToUpperInvariant(command) == 'Q')
                    {
                        controlX = ox + tokens.Number();
                        controlY = oy + tokens.Number();
                    }
                    else
                    {
                        controlX = smooth is 'Q' or 'T' ? 2 * x - controlX : x;
                        controlY = smooth is 'Q' or 'T' ? 2 * y - controlY : y;
                    }
                    x = ox + tokens.Number();
                    y = oy + tokens.Number();
                    path.AddQuadCurveToPoint(controlX, controlY, x, y);
                    break;
                case 'Z':
                    path.CloseSubpath();
                    x = startX;
                    y = startY;
                    break;
                default:
                    throw new NotSupportedException($"path command '{command}'");
            }
            previous = command;
        }
        return path;
    }
}

/// <summary>Reads SVG path data: command letters, then comma/space separated numbers with implicit repetition.</summary>
internal ref struct SvgPathTokens(string data)
{
    readonly string data = data;
    int index;

    /// <summary>Advances to the next command; a number in place of a letter repeats the current command.</summary>
    internal bool Next(ref char command)
    {
        Skip();
        if (index >= data.Length)
            return false;
        var next = data[index];
        if (char.IsAsciiLetter(next))
        {
            index++;
            command = next;
            return true;
        }
        if (command is '\0' or 'Z' or 'z')
            throw new FormatException($"number without a command at {index}");
        return true;
    }

    internal float Number()
    {
        Skip();
        var start = index;
        if (index < data.Length && data[index] is '-' or '+')
            index++;
        var dot = false;
        while (index < data.Length && (char.IsAsciiDigit(data[index]) || data[index] == '.' && !dot))
            dot |= data[index++] == '.';
        if (index < data.Length && data[index] is 'e' or 'E')
        {
            index++;
            if (index < data.Length && data[index] is '-' or '+')
                index++;
            while (index < data.Length && char.IsAsciiDigit(data[index]))
                index++;
        }
        if (index == start)
            throw new FormatException($"expected a number at {start}");
        return float.Parse(data.AsSpan(start, index - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    void Skip()
    {
        while (index < data.Length && (char.IsWhiteSpace(data[index]) || data[index] == ','))
            index++;
    }
}
