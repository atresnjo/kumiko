using System.Net;

static class ArtifactGallery
{
    public static void Write(string output, int count)
    {
        var pictures = new (string File, string Label)[]
        {
            ("workbench-light.png", "Neutral light"),
            ("workbench-dropdown-dark.png", "Dark searchable combobox"),
            ("workbench-menu-dark.png", "Keyboard context menu"),
            ("workbench-violet-compact.png", "Brand color and compact density"),
            ("workbench-validation.png", "Invalid edit retained"),
            ("workbench-load-error.png", "Recoverable background failure"),
            ("workbench-empty-narrow.png", "Narrow empty state"),
            ("text-composition-multiline.png", "Composition and multiline editing"),
            ("gpu.png", "GPU rendering"),
            ("gpu-cpu-reference.png", "CPU rendering reference"),
            ("gpu-2x.png", "GPU at 2× display scale"),
            ("render-glass-1.25.png", "GPU overlapping glass at fractional scale"),
            ("render-glass-reference-1.25.png", "Serial CPU glass reference"),
            ("render-cpu-interactions.png", "Incremental software drawing after interaction and scale changes"),
            ("native-hidden.png", "Hidden native window after accessibility actions")
        };
        using var file = File.CreateText(Path.Combine(output, "index.html"));
        file.WriteLine("""
            <!doctype html><html lang="en"><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Kumiko desktop verification</title>
            <style>
            :root{color-scheme:light dark;font:15px/1.5 system-ui,sans-serif;background:#fafafa;color:#18181b}
            body{max-width:1440px;margin:auto;padding:32px}h1{font-size:28px;letter-spacing:-.02em;margin:0 0 8px}
            p{color:#52525b}a{color:inherit;text-underline-offset:3px}.evidence{display:flex;gap:24px;flex-wrap:wrap;margin:20px 0 32px}
            main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,540px),1fr));gap:28px}
            figure{margin:0}figcaption{font-weight:600;margin-bottom:10px}img{display:block;width:100%;height:auto;border:1px solid #e4e4e7;border-radius:8px}
            @media(prefers-color-scheme:dark){:root{background:#09090b;color:#fafafa}p{color:#a1a1aa}img{border-color:#3f3f46}}
            </style><h1>Kumiko desktop verification</h1>
            """);
        file.WriteLine($"<p>{count} behavior checks passed on this run. Screenshots show illustrative in-memory inventory.</p>");
        file.WriteLine("<nav class=\"evidence\"><a href=\"report.json\">Checks and SHA-256 hashes</a><a href=\"gpu.json\">GPU driver and comparison</a><a href=\"native-renderer.txt\">Native renderer</a></nav><main>");
        foreach (var picture in pictures)
        {
            if (!File.Exists(Path.Combine(output, picture.File)))
                continue;
            string label = WebUtility.HtmlEncode(picture.Label);
            file.WriteLine($"<figure><figcaption>{label}</figcaption><a href=\"{picture.File}\"><img src=\"{picture.File}\" alt=\"{label}\" loading=\"lazy\"></a></figure>");
        }
        file.WriteLine("</main></html>");
    }
}
