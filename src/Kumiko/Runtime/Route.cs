using System.Collections.Immutable;
using System.Text;

namespace Kumiko.Runtime;

/// <summary>Generates route metadata, typed page dispatch and relative path parsing for a partial record union.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RouteUnionAttribute(string discriminatorProperty = "route") : Attribute
{
    public string DiscriminatorProperty { get; } = discriminatorProperty;
}

/// <summary>Declares one route's path, persistent JSON tag and stable numeric kind.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RouteAttribute(string template, string discriminator, int kind) : Attribute
{
    public string Template { get; } = template;
    public string Discriminator { get; } = discriminator;
    public int Kind { get; } = kind;
    public string? Name
    {
        get; set;
    }
}

/// <summary>Strict relative path encoding used by generated routes. It performs no navigation or I/O.</summary>
public static class RoutePath
{
    public static string Encode(string value) => Uri.EscapeDataString(value);

    /// <summary>Returns default for malformed input. Parameters are decoded exactly once.</summary>
    public static ImmutableArray<string> Segments(string? path)
    {
        if (path is null || path.IndexOfAny(['?', '#', '\\']) >= 0)
            return default;
        if (path.StartsWith('/'))
            path = path[1..];
        if (path.Length == 0)
            return [];
        var segments = ImmutableArray.CreateBuilder<string>();
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0)
                return default;
            var text = new StringBuilder();
            for (var i = 0; i < part.Length; i++)
            {
                if (part[i] != '%')
                {
                    text.Append(part[i]);
                    continue;
                }
                var bytes = new List<byte>();
                while (i < part.Length && part[i] == '%')
                {
                    if (i + 2 >= part.Length || Hex(part[i + 1]) is not (>= 0 and var high) || Hex(part[i + 2]) is not (>= 0 and var low))
                        return default;
                    bytes.Add((byte)(high * 16 + low));
                    i += 3;
                }
                i--;
                try
                {
                    text.Append(new UTF8Encoding(false, true).GetString([.. bytes]));
                }
                catch (DecoderFallbackException)
                {
                    return default;
                }
            }
            var decoded = text.ToString();
            if (decoded.Any(char.IsControl))
                return default;
            segments.Add(decoded);
        }
        return segments.ToImmutable();
    }

    static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'F' ? c - 'A' + 10 : c is >= 'a' and <= 'f' ? c - 'a' + 10 : -1;
}
