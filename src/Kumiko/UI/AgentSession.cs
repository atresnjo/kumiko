using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Kumiko.Runtime;

namespace Kumiko.UI;

// One JSON request and one JSON response per line. This opt-in process owns an
// offscreen Tester and blocks on stdin or the Kumiko wake event, never a render poll.
internal static class AgentSession
{
    internal static void Run(Tester tester, IProgram program, TextReader input, TextWriter output, string? initialWindow = null)
    {
        while (input.ReadLine() is { } line)
        {
            using var stream = new MemoryStream();
            using var writer = new Utf8JsonWriter(stream);
            bool quit = false;
            var selected = tester;
            writer.WriteStartObject();
            try
            {
                using var document = JsonDocument.Parse(line);
                var request = document.RootElement;
                if (request.TryGetProperty("id", out var requestId))
                {
                    writer.WritePropertyName("id");
                    requestId.WriteTo(writer);
                }
                var operation = request.GetProperty("op").GetString();
                var selector = Text(request, "window") ?? initialWindow;
                if (selector is not null && operation is not ("windows" or "quit"))
                    selected = tester.In(selector);
                switch (operation)
                {
                    case "inspect":
                        writer.WriteString("status", "ok");
                        WriteNodes(writer, selected.Context.Query(Query(request, true)));
                        break;
                    case "act":
                        Act(request, selected, tester, writer);
                        break;
                    case "await":
                        Await(request, selected, tester, program, writer);
                        break;
                    case "screenshot":
                        var path = Path.GetFullPath(request.GetProperty("path").GetString()!);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        selected.Screenshot(path);
                        writer.WriteString("status", "ok");
                        writer.WriteString("path", path);
                        break;
                    case "windows":
                        writer.WriteString("status", "ok");
                        writer.WriteStartArray("windows");
                        foreach (var window in tester.Windows)
                        {
                            writer.WriteStartObject();
                            writer.WriteString("key", window.Key);
                            writer.WriteString("title", window.Title);
                            writer.WriteNumber("width", window.Width);
                            writer.WriteNumber("height", window.Height);
                            writer.WriteEndObject();
                        }
                        writer.WriteEndArray();
                        break;
                    case "quit":
                        quit = true;
                        writer.WriteString("status", "ok");
                        break;
                    default:
                        writer.WriteString("status", "invalid-request");
                        writer.WriteString("detail", "Use windows, inspect, act, await, screenshot or quit.");
                        break;
                }
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or FormatException or OverflowException)
            {
                writer.WriteString("status", "invalid-request");
                writer.WriteString("detail", error.Message);
            }
            writer.WriteNumber("revision", selected.Context.Revision);
            writer.WriteNumber("pendingCommands", program.PendingCommands);
            writer.WriteEndObject();
            writer.Flush();
            output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
            output.Flush();
            if (quit)
                return;
        }
    }

    static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) ? value.GetString() : null;
    static ulong? Id(JsonElement root, string key) => Text(root, key) is { } value ? ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : null;
    static SemanticQuery Query(JsonElement root, bool offscreen) => new(
        Text(root, "role") is { } role ? Enum.Parse<Role>(role, true) : null,
        Text(root, "name"), Text(root, "key"), Id(root, "within"),
        root.TryGetProperty("includeOffscreen", out var include) ? include.GetBoolean() : offscreen);

    static void Act(JsonElement request, Tester tester, Tester root, Utf8JsonWriter writer)
    {
        // Revisions are mandatory for mutations; a stale query never changes state.
        var revision = request.GetProperty("revision").GetInt64();
        if (revision != tester.Context.Revision)
        {
            writer.WriteString("status", "StaleRevision");
            return;
        }
        var action = Enum.Parse<AccessibilityAction>(request.GetProperty("action").GetString()!, true);
        var target = Id(request, "target");
        if (target is null)
        {
            var matches = tester.Context.Query(Query(request, action == AccessibilityAction.Reveal));
            if (matches.Count != 1)
            {
                writer.WriteString("status", matches.Count == 0 ? "NotFound" : "Ambiguous");
                WriteNodes(writer, matches);
                return;
            }
            target = matches[0].Id;
        }
        var result = tester.Context.PerformAction(target.Value, new(action, Text: Text(request, "text"),
            Number: request.TryGetProperty("number", out var number) ? number.GetDouble() : 0,
            Start: request.TryGetProperty("start", out var start) ? start.GetInt32() : 0,
            End: request.TryGetProperty("end", out var end) ? end.GetInt32() : 0), revision);
        if (result.Succeeded)
            Settle(root);
        writer.WriteString("status", result.Succeeded ? "ok" : result.Status.ToString());
        writer.WriteString("dispatch", result.Status.ToString());
        if (result.Detail is not null)
            writer.WriteString("detail", result.Detail);
    }

    static void Settle(Tester tester)
    {
        tester.Settle();
        for (var i = 0; i < 8 && tester.Context.NextFrame is { } next; i++)
            tester.Advance((float)Math.Clamp(next.TotalSeconds, 0, 1));
    }

    static void Await(JsonElement request, Tester tester, Tester root, IProgram program, Utf8JsonWriter writer)
    {
        var after = request.TryGetProperty("afterRevision", out var revision) ? revision.GetInt64() : tester.Context.Revision;
        bool idle = request.TryGetProperty("idle", out var idleValue) && idleValue.GetBoolean();
        var timeout = TimeSpan.FromMilliseconds(request.TryGetProperty("timeoutMs", out var ms) ? Math.Clamp(ms.GetInt32(), 0, 30000) : 5000);
        var elapsed = Stopwatch.StartNew();
        if (program.HasPendingWork)
            Settle(root);
        while (idle ? program.HasPendingWork : tester.Context.Revision <= after)
        {
            var remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                writer.WriteString("status", "timeout");
                return;
            }
            var scheduled = tester.Context.NextFrame;
            var wait = scheduled is { } next && next < remaining ? next : remaining;
            var work = program.WaitForWork(wait);
            if (work || scheduled is { } frame && frame <= wait)
                Settle(root);
        }
        writer.WriteString("status", "ok");
    }

    static void WriteNodes(Utf8JsonWriter writer, IReadOnlyList<AccessibleNode> nodes)
    {
        writer.WriteStartArray("nodes");
        foreach (var node in nodes)
        {
            writer.WriteStartObject();
            writer.WriteString("id", node.Id.ToString("x16"));
            writer.WriteString("parent", node.Parent.ToString("x16"));
            writer.WriteString("role", node.Role.ToString());
            writer.WriteString("name", node.Name);
            if (node.Key is not null)
                writer.WriteString("key", node.Key);
            if (node.Value is not null && node.Properties?.Password != true)
                writer.WriteString("value", node.Value);
            writer.WriteBoolean("enabled", node.Enabled);
            writer.WriteBoolean("inert", node.Inert);
            writer.WriteBoolean("offscreen", node.Offscreen);
            writer.WriteBoolean("focused", node.Focused);
            writer.WriteBoolean("checked", node.Checked);
            if (node.Properties is { } properties)
            {
                writer.WriteBoolean("selected", properties.Selected);
                if (properties.Expanded is { } expanded)
                    writer.WriteBoolean("expanded", expanded);
                if (properties.Error is { } error)
                    writer.WriteString("error", error);
                if (properties.Description is { } description)
                    writer.WriteString("description", description);
                if (properties.Number is { } number)
                    writer.WriteNumber("number", number);
                if (properties.Minimum is { } min)
                    writer.WriteNumber("minimum", min);
                if (properties.Maximum is { } max)
                    writer.WriteNumber("maximum", max);
            }
            writer.WriteStartArray("actions");
            foreach (var action in Enum.GetValues<AccessibilityAction>())
                if (node.Supports(action))
                    writer.WriteStringValue(action.ToString());
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
