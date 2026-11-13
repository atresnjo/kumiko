using System.Diagnostics;
using System.Text.Json;
using Kumiko;

namespace ReactVite;

/// <summary>
/// Hidden end-to-end check: a real WKWebView runs the embedded Vite bundle, React renders the native
/// count from the stream, and a click inside React updates the native model through a typed call.
/// </summary>
static class Verify
{
    const string Probe = """
        (() => {
          const text = (id) => document.querySelector(`[data-testid="${id}"]`)?.textContent ?? null;
          kumiko.log("probe:" + JSON.stringify({ count: text("count"), mode: text("mode"), styled: getComputedStyle(document.body).margin === "0px" }));
        })()
        """;

    sealed record Snapshot(string? Count, string? Mode, bool Styled);

    public static int Run(string output, string? devServer)
    {
        Directory.CreateDirectory(output);
        var checks = new List<(bool Passed, string Name)>();
        void Check(bool passed, string name)
        {
            checks.Add((passed, name));
            Console.WriteLine((passed ? "PASS " : "FAIL ") + name);
        }
        var (javaScript, typeScript) = HostServices.Client();
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
        using var app = new App(new("React + Vite verification", Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var host = Host.Start(app, devServer, visible: false);
        string mode = devServer is null ? "bundled" : "dev-server";
        Snapshot? latest = null;
        host.Page.Logged += message =>
        {
            if (message.StartsWith("probe:", StringComparison.Ordinal))
                latest = Parse(message["probe:".Length..]);
        };
        bool Until(Func<Snapshot, bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (host.Page.IsReady)
                    host.Page.Evaluate(Probe);
                app.RunFor(TimeSpan.FromMilliseconds(50));
                if (latest is { } snapshot && condition(snapshot))
                    return true;
            }
            return false;
        }
        check(Until(s => s is { Count: "0", Styled: true } && s.Mode == mode),
            devServer is null ? "React mounts from kumiko://localhost with its hashed Vite JS and CSS assets"
                : "React mounts from the trusted Vite dev server origin with the injected bridge");
        host.Program.Send(new CounterMsg.Add(1, "Kumiko"));
        check(Until(static s => s.Count == "1"), "a native Kumiko message is streamed into React state");
        host.Page.Evaluate("""document.querySelector('[data-testid="add"]').click()""");
        check(Until(s => s.Count == "2" && host.Program.Model is { Count: 2, LastSource: "React" }),
            "a React click calls the typed C# service and updates the native model");
        check(!host.Window.IsVisible && errors.Count == 0, "verification stays hidden with no callback errors");
    }

    static Snapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new(root.GetProperty("count").GetString(), root.GetProperty("mode").GetString(), root.GetProperty("styled").GetBoolean());
    }

    static void Report(string output, List<(bool Passed, string Name)> checks, string? devServer)
    {
        using var json = new Utf8JsonWriter(File.Create(Path.Combine(output, devServer is null ? "react-vite.json" : "react-vite-dev.json")), new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("boundary", "hidden WKWebView running " + (devServer ?? "the embedded Vite production bundle") + "; no browser pixels are captured");
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
