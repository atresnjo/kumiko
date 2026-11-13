namespace DiffViewer.Highlight;

/// <summary>Languages the highlighter understands. Anything else renders as plain text.</summary>
public enum Language : byte
{
    Plain,
    TypeScript,
    Tsx,
    JavaScript,
    Jsx,
    Json,
    CSharp
}

public static class Languages
{
    /// <summary>Picks a language from the file extension, ignoring case. Declaration files (.d.ts) are TypeScript.</summary>
    public static Language FromPath(string path)
    {
        var name = path.AsSpan();
        var slash = name.LastIndexOfAny('/', '\\');
        if (slash >= 0)
            name = name[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        if (dot <= 0)
            return Language.Plain;
        var extension = name[(dot + 1)..];
        Span<char> lower = stackalloc char[5];
        if (extension.Length > lower.Length)
            return Language.Plain;
        var length = extension.ToLowerInvariant(lower);
        return lower[..length] switch
        {
            "ts" or "mts" or "cts" => Language.TypeScript,
            "tsx" => Language.Tsx,
            "js" or "mjs" or "cjs" => Language.JavaScript,
            "jsx" => Language.Jsx,
            "json" => Language.Json,
            "cs" or "csx" => Language.CSharp,
            _ => Language.Plain
        };
    }
}
