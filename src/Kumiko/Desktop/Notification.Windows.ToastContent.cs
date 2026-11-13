using System.Security.Cryptography;
using System.Text;

namespace Kumiko.Desktop;
/// <summary>Portable toast payload and identity rules; nothing here touches the operating system.</summary>
internal static class WindowsToastContent
{
    /// <summary>
    /// ToastNotification.Tag and Group were limited to 16 characters before Windows 10 1703 and to 64
    /// afterwards. Both are 16 uppercase hex digits of SHA-256, so every ID and group length is valid.
    /// </summary>
    internal const int IdentityLength = 16;
    /// <summary>ToastGeneric shows the first text as the title and up to two further lines.</summary>
    internal static string Xml(NotificationOptions options)
    {
        var xml = new StringBuilder("<toast><visual><binding template=\"ToastGeneric\">");
        Text(xml, options.Title);
        if (options.Subtitle.Length > 0)
            Text(xml, options.Subtitle);
        if (options.Body.Length > 0)
            Text(xml, options.Body);
        xml.Append("</binding></visual></toast>");
        return xml.ToString();
    }

    static void Text(StringBuilder xml, string value)
    {
        xml.Append("<text>");
        xml.Append(Escape(value));
        xml.Append("</text>");
    }

    /// <summary>Escapes markup and replaces characters XML 1.0 cannot carry (controls, lone surrogates) with U+FFFD.</summary>
    internal static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            string? entity = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&apos;",
                _ => null
            };
            if (entity is not null)
                builder.Append(entity);
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c);
                builder.Append(text[++i]);
            }
            else if (c is '\t' or '\n' or '\r' || (c >= ' ' && c <= '퟿') || (c >= '' && c <= '�'))
                builder.Append(c);
            else
                builder.Append('�');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Maps a Notification ID and group onto toast Tag/Group. The length-prefixed tag input keeps
    /// ("ab", "") and ("b", "a") distinct; every notification gets a non-empty group so Remove(tag,
    /// group) and RemoveGroup address only Kumiko-owned toasts.
    /// </summary>
    internal static (string Tag, string Group) Identity(string id, string group) => (Hash("tag", group.Length + ":" + group + id), Hash("group", group));
    /// <summary>
    /// Unpackaged AppUserModelID: "Kumiko.&lt;ASCII name&gt;.&lt;hash of name and executable&gt;". It has no
    /// spaces and stays far below the 128-character limit. Paths compare case-insensitively on Windows.
    /// </summary>
    internal static string ApplicationId(string name, string? executable)
    {
        var product = new StringBuilder();
        foreach (char c in name)
            if (char.IsAsciiLetterOrDigit(c) && product.Length < 48)
                product.Append(c);
        if (product.Length == 0)
            product.Append("App");
        return "Kumiko." + product + "." + Hash("aumid", name + "\0" + (executable ?? "").ToUpperInvariant());
    }

    static string Hash(string kind, string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + "\0" + value)), 0, IdentityLength / 2);
}
