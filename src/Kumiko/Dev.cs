using System.Globalization;
using Kumiko.UI;

namespace Kumiko;

public static partial class Dev
{
    public static string? Option(string[] args, string name) => args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
    /// <summary>
    /// Applies <c>--kumiko-culture=name</c> (for example he-IL) to the process before windows are created, so
    /// formatting, reading direction, built-in control text and the application's resources follow it.
    /// </summary>
    static void ApplyCulture(string[] args)
    {
        if (Option(args, "--kumiko-culture") is not { } name)
            return;
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    internal static void Run(string title, float width, float height, Action<Context> view, string[] args)
    {
        ApplyCulture(args);
        var theme = args.Contains("--dark", StringComparer.Ordinal) ? Theme.DarkTheme : null;
        var snapshot = Option(args, "--kumiko-snapshot");
        var tree = Option(args, "--kumiko-tree");
        var audit = Option(args, "--kumiko-audit");
        var dev = args.Contains("--kumiko-dev", StringComparer.Ordinal);
        if (snapshot is not null || tree is not null || audit is not null)
        {
            float scale = float.Parse(Option(args, "--kumiko-scale") ?? "2", CultureInfo.InvariantCulture);
            using var tester = new Tester(view, new(width, height, scale, theme, dev));
            // Offscreen artifacts show the settled view, as agent sessions do, not the first frame of an animation.
            tester.Context.ReduceMotion = true;
            tester.Settle();
            if (Option(args, "--kumiko-script") is { } script)
                tester.RunScript(script);
            if (Option(args, "--kumiko-wait") is { } wait)
                tester.Advance(float.Parse(wait, CultureInfo.InvariantCulture));
            if (snapshot is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshot))!);
                tester.Screenshot(snapshot);
                Console.WriteLine(Path.GetFullPath(snapshot));
            }

            if (tree is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tree))!);
                File.WriteAllText(tree, tester.Dump());
                Console.WriteLine(Path.GetFullPath(tree));
            }

            if (audit is not null)
                WriteAudit(audit, tester.Context.AuditAccessibility());
            if (dev && (snapshot ?? tree) is { } artifact)
                tester.WriteWarnings(Path.ChangeExtension(artifact, ".warnings.json"));
            return;
        }

        using var app = new App(new(title));
        using var window = app.NewWindow(new(Title: title, Width: width, Height: height, Theme: theme), view);
        window.Context!.DevMode = dev;
        app.Run();
    }

    /// <summary>Writes <c>--kumiko-audit</c> findings as JSON and prints the path; an empty array means none were found.</summary>
    static void WriteAudit(string path, IReadOnlyList<AccessibilityFinding> findings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var stream = File.Create(path))
        using (var json = new System.Text.Json.Utf8JsonWriter(stream, new()
        {
            Indented = true
        }))
        {
            json.WriteStartArray();
            foreach (var finding in findings)
            {
                json.WriteStartObject();
                json.WriteString("code", finding.Code);
                json.WriteString("path", finding.Path);
                json.WriteString("message", finding.Message);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        Console.WriteLine(Path.GetFullPath(path));
    }
}
