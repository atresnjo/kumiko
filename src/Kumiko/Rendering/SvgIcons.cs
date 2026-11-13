using System.Collections.Concurrent;
using System.Text;

namespace Kumiko.Rendering;

/// <summary>Namespaced monochrome SVG sources used by Context.Icon and Kit icon parameters.</summary>
public static class SvgIcons
{
    static readonly ConcurrentDictionary<string, Func<string, string?>> Sources = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a process-lifetime source once, before its first use. Names have the form
    /// prefix:name. The resolver receives the local name and returns SVG or null when absent.
    /// Resolvers must be thread safe and keep their source data available for the process lifetime.
    /// SVG alpha coverage is tinted at paint time; multicolour documents should use Image.Svg.
    /// </summary>
    public static void Register(string prefix, Func<string, string?> resolve)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(resolve);
        if (prefix.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new ArgumentException("An icon prefix must contain only ASCII letters, digits, '-' or '_'.", nameof(prefix));
        if (!Sources.TryAdd(prefix, resolve))
            throw new InvalidOperationException($"SVG icon prefix '{prefix}' is already registered.");
    }

    internal static Pixels? Raster(string name, float size, float scale) =>
        Document(name) is { } svg ? SvgRaster.Render(Encoding.UTF8.GetBytes(svg), size, size, scale) : null;

    /// <summary>The registered SVG document for prefix:name, or null for an unprefixed platform symbol name.</summary>
    internal static string? Document(string name)
    {
        int separator = name.IndexOf(':');
        if (separator < 0)
            return null;
        string prefix = name[..separator];
        if (!Sources.TryGetValue(prefix, out var source))
            throw new KeyNotFoundException($"No SVG icon source is registered for '{prefix}'.");
        return source(name[(separator + 1)..]) ?? throw new KeyNotFoundException($"Unknown SVG icon '{name}'.");
    }
}
