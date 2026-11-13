using System.Collections.Immutable;
using Kumiko.Rendering;

namespace Kumiko.Icons;

/// <summary>The MIT licensed Hugeicons free Stroke Rounded catalogue.</summary>
public static partial class Hugeicons
{
    const string ResourcePrefix = "Hugeicons.";
    // Set by KumikoHugeicon items (buildTransitive); the same switches drive resource removal when trimming.
    const string SubsetSwitch = "Kumiko.Icons.Hugeicons.Subset";
    const string IconSwitchPrefix = "Kumiko.Icons.Hugeicons.Icon.";

    static readonly bool Subset = Enabled(SubsetSwitch);

    /// <summary>Sorted local names, for example star and arrow-up-right-01. Apps with KumikoHugeicon items list only those.</summary>
    public static ImmutableArray<string> Names
    {
        get;
    } = [.. typeof(Hugeicons).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".svg", StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..^4])
        .Where(name => !Subset || Enabled(IconSwitchPrefix + name))
        .Order(StringComparer.Ordinal)];

    static readonly ImmutableHashSet<string> KnownNames = Names.ToImmutableHashSet(StringComparer.Ordinal);

    static Hugeicons() => SvgIcons.Register("hugeicons", name => KnownNames.Contains(name) ? GetSvg(name) : throw Unknown(name));

    /// <summary>Returns a validated identifier for Context.Icon or a Kit icon parameter.</summary>
    public static string Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!KnownNames.Contains(name))
            throw Unknown(name);
        return "hugeicons:" + name;
    }

    /// <summary>Returns the same tintable SVG document used natively, suitable for inline web SVG.</summary>
    public static string GetSvg(string name)
    {
        Get(name);
        using var stream = typeof(Hugeicons).Assembly.GetManifestResourceStream($"{ResourcePrefix}{name}.svg")
            ?? throw new InvalidDataException($"Missing embedded Hugeicons asset '{name}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static bool Enabled(string feature) => AppContext.TryGetSwitch(feature, out bool on) && on;

    static KeyNotFoundException Unknown(string name) => new(Subset
        ? $"Hugeicons icon '{name}' is not in this app's KumikoHugeicon set. Add <KumikoHugeicon Include=\"{name}\" /> to the app project."
        : $"Unknown Hugeicons free icon '{name}'.");
}
