using System.Text;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

/// <summary>
/// A reversible mapping between portable MIME names and NSPasteboard type identifiers.
/// Native(Portable(type)) == type for every identifier, which includes every pasteboard item type,
/// and Portable(Native(mime)) == mime for every valid MIME name, so foreign data can be read, held
/// and written back unchanged. Names that are not identifiers, such as the legacy pasteboard names
/// a board's types list also reports, cannot be promised by NSPasteboardItem; they are re-offered
/// under the identifier AppKit maps them to (NSStringPboardType becomes public.utf8-plain-text,
/// unregistered names become dyn.* identifiers).
/// </summary>
internal static class MacTransferFormats
{
    /// <summary>Portable name for a native type that has no MIME equivalent, such as com.apple.flat-rtfd.</summary>
    internal const string NativePrefix = "application/x-kumiko-native;type=";
    /// <summary>Native identifier for a MIME name the system cannot map back unchanged, such as mixed case names.</summary>
    const string MimePrefix = "org.kumiko.mime.";
    static readonly (string Mime, string Native)[] fixedTypes =
    [
        (TransferFormats.Text, "public.utf8-plain-text"),
        (TransferFormats.HTML, "public.html"),
        (TransferFormats.PNG, "public.png"),
        (TransferFormats.URIList, "public.url"),
        (TransferFormats.FileList, "public.file-url"),
    ];

    internal static string Native(string mime)
    {
        if (mime.StartsWith(NativePrefix, StringComparison.Ordinal))
        {
            var escaped = mime[NativePrefix.Length..];
            var type = Uri.UnescapeDataString(escaped);
            if (type.Length == 0 || Uri.EscapeDataString(type) != escaped || Preferred(type) is not null)
                return Encoded(mime);
            if (IsIdentifier(type))
                return type;
            return Legacy(type) ?? Encoded(mime);
        }

        return Direct(mime);
    }

    internal static string Portable(string type) => Preferred(type) ?? NativePrefix + Uri.EscapeDataString(type);

    /// <summary>The first MIME candidate that maps back to exactly this native type.</summary>
    static string? Preferred(string type)
    {
        foreach (var (mime, native) in fixedTypes)
            if (native == type)
                return mime;
        if (SystemMime(type) is { } system && !system.StartsWith(NativePrefix, StringComparison.Ordinal) && Accepts(system, type))
            return system;
        if (type.StartsWith(MimePrefix, StringComparison.Ordinal) && Decode(type[MimePrefix.Length..]) is { } decoded && Accepts(decoded, type))
            return decoded;
        return null;
    }

    // Decoding shortens the string on every nested call, so this mutual recursion terminates.
    static bool Accepts(string mime, string type) => Valid(mime) && Native(mime) == type;

    static string Direct(string mime)
    {
        foreach (var (portable, native) in fixedTypes)
            if (portable == mime)
                return native;
        if (SystemType(mime) is { } system && !fixedTypes.Any(t => t.Native == system) && SystemMime(system) == mime)
            return system;
        return Encoded(mime);
    }

    static string Encoded(string mime) => MimePrefix + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(mime));

    static string? Decode(string hex)
    {
        if (hex.Length == 0 || hex.Length % 2 != 0 || !hex.All(char.IsAsciiHexDigitLower))
            return null;
        var bytes = Convert.FromHexString(hex);
        var text = new UTF8Encoding(false, false).GetString(bytes);
        return Encoding.UTF8.GetBytes(text).AsSpan().SequenceEqual(bytes) ? text : null;
    }

    /// <summary>The identifier syntax NSPasteboardItem accepts: two or more ASCII alphanumeric or hyphen segments.</summary>
    internal static bool IsIdentifier(string type)
    {
        var segments = type.Split('.');
        return segments.Length >= 2 && segments.All(s => s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'));
    }

    static string? Legacy(string name)
    {
        if (UTType == 0)
            return null;
        using var pool = new AutoreleasePool();
        var type = O.O(UTType, "typeWithTag:tagClass:conformingToType:", O.TempString(name), O.TempString("com.apple.nspboard-type"), (nint)0);
        return type != 0 && O.Text(O.O(type, "identifier")) is { } identifier && IsIdentifier(identifier) ? identifier : null;
    }

    static bool Valid(string format) => !string.IsNullOrWhiteSpace(format) && format.Contains('/') && !format.Any(char.IsControl);

    static nint UTType => O.objc_getClass("UTType");

    static string? SystemType(string mime)
    {
        if (UTType == 0)
            return null;
        using var pool = new AutoreleasePool();
        var type = O.O(UTType, "typeWithMIMEType:", O.TempString(mime));
        return type == 0 ? null : O.Text(O.O(type, "identifier")) is { Length: > 0 } identifier ? identifier : null;
    }

    static string? SystemMime(string identifier)
    {
        if (UTType == 0)
            return null;
        using var pool = new AutoreleasePool();
        var type = O.O(UTType, "typeWithIdentifier:", O.TempString(identifier));
        if (type == 0)
            return null;
        var mime = O.O(type, "preferredMIMEType");
        return mime == 0 ? null : O.Text(mime);
    }
}
