using Kumiko.Web;

namespace ReactOnly;

/// <summary>Reads the Vite dist/ output that the project embeds as resources named web/&lt;path&gt;.</summary>
static class WebBundle
{
    const string Prefix = "web/";

    public static bool IsBuilt => typeof(WebBundle).Assembly.GetManifestResourceInfo(Prefix + "index.html") is not null;

    public static Asset[] Load()
    {
        if (!IsBuilt)
            return [Missing];
        var assembly = typeof(WebBundle).Assembly;
        return [.. assembly.GetManifestResourceNames().Where(static name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(name => new Asset("/" + name[Prefix.Length..], Read(name)))];
    }

    public static string? Text(string name)
    {
        using var stream = typeof(WebBundle).Assembly.GetManifestResourceStream(name);
        return stream is null ? null : new StreamReader(stream).ReadToEnd();
    }

    static byte[] Read(string name)
    {
        using var stream = typeof(WebBundle).Assembly.GetManifestResourceStream(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    static Asset Missing => Asset.Text("/index.html", """
        <!doctype html><html><body style="font:15px system-ui;padding:24px;color:#333">
        <h2>The React bundle has not been built</h2>
        <p>Run <code>npm install</code> in <code>examples/ReactOnly/web</code>, then rebuild this app.
        Or start <code>npm run dev</code> and pass <code>--dev-server=http://localhost:5173</code>.</p>
        </body></html>
        """);
}
