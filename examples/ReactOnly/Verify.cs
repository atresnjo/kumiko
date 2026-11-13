using System.Diagnostics;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;

namespace ReactOnly;

/// <summary>
/// Hidden end-to-end check: a program-free page window runs the React bundle, which reaches native
/// features only through the typed services. Hidden apps use a private in-memory clipboard.
/// </summary>
static class Verify
{
    const string Probe = """
        (() => {
          const text = (id) => document.querySelector(`[data-testid="${id}"]`)?.textContent ?? null;
          kumiko.log("probe:" + JSON.stringify({
            platform: text("platform"),
            theme: document.documentElement.dataset.theme ?? null,
            editor: document.querySelector('[data-testid="editor"]')?.value ?? null,
            status: text("status"),
            styled: getComputedStyle(document.body).margin === "0px",
          }));
        })()
        """;

    sealed record Snapshot(string? Platform, string? Theme, string? Editor, string? Status, bool Styled);

    public static int Run(string output, string? devServer)
    {
        Directory.CreateDirectory(output);
        var checks = new List<(bool Passed, string Name)>();
        void Check(bool passed, string name)
        {
            checks.Add((passed, name));
            Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
        }
        var (javaScript, typeScript) = NativeServices.Client();
        Check(WebBundle.Text("client/client.js") == javaScript && WebBundle.Text("client/client.d.ts") == typeScript,
            "checked-in web/src/kumiko client matches the C# service contract");
        if (devServer is null)
            Check(WebBundle.IsBuilt, "Vite production bundle is embedded in the executable");
        if (OperatingSystem.IsMacOS() && (devServer is not null || WebBundle.IsBuilt))
            Browser(Check, devServer);
        else
            Console.WriteLine("SKIP native browser flow: requires macOS and an embedded bundle");
        Report(output, checks, devServer);
        return checks.All(static check => check.Passed) ? 0 : 1;
    }

    static void Browser(Action<bool, string> check, string? devServer)
    {
        using var app = new App(new("React Only verification", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var window = NativeHost.Open(app, devServer, visible: false);
        var page = window.Page!;
        Snapshot? latest = null;
        page.Logged += message =>
        {
            if (message.StartsWith("probe:", StringComparison.Ordinal))
                latest = Parse(message["probe:".Length..]);
        };
        bool Until(Func<Snapshot, bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (page.IsReady)
                    page.Evaluate(Probe);
                app.RunFor(TimeSpan.FromMilliseconds(50));
                if (latest is { } snapshot && condition(snapshot))
                    return true;
            }
            return false;
        }
        check(window.Context is null && Until(static s => s is { Platform: "macos", Styled: true }),
            "React is the whole window: a program-free page mounts and reads native platform info");
        Clipboard.WriteText("from native clipboard");
        page.Evaluate("""document.querySelector('[data-testid="paste"]').click()""");
        check(Until(static s => s.Editor == "from native clipboard"), "React pastes text read from the native clipboard service");
        page.Evaluate("""document.querySelector('[data-testid="copy"]').click()""");
        check(Until(static s => s.Status == "Copied 21 characters") && Clipboard.ReadText() == "from native clipboard",
            "React writes the native clipboard through a typed service");
        NativeServices.Appearance.Emit(window, true);
        check(Until(static s => s.Theme == "dark"), "a native appearance event switches the React theme");
        NativeServices.Appearance.Emit(window, false);
        check(Until(static s => s.Theme == "light"), "React follows the appearance event back to light");
        check(!window.IsVisible && errors.Count == 0, "verification stays hidden with no callback errors");
    }

    static Snapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(root.GetProperty("platform").GetString(), root.GetProperty("theme").GetString(), root.GetProperty("editor").GetString(),
            root.GetProperty("status").GetString(), root.GetProperty("styled").GetBoolean());
    }

    static void Report(string output, List<(bool Passed, string Name)> checks, string? devServer)
    {
        using var json = new Utf8JsonWriter(File.Create(Path.Combine(output, devServer is null ? "react-only.json" : "react-only-dev.json")), new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("boundary", "hidden program-free WKWebView window running " + (devServer ?? "the embedded Vite production bundle") +
            "; private in-memory clipboard; dialogs and notifications are not exercised; no browser pixels are captured");
        json.WriteStartArray("checks");
        foreach (var (passed, name) in checks)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteBoolean("passed", passed);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }
}
