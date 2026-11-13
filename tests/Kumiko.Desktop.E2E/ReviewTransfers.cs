using System.Text;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;

/// <summary>REVIEW-ACCEPTANCE transfer scenarios: teardown, native format mapping, foreign reads and drag writers.</summary>
static partial class ReviewTransfers
{
    internal static void Run(string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var events = new List<string>();
        var boards = new Dictionary<string, string[][]>();
        void Verify(bool condition, string name)
        {
            check(condition, "Review transfers: " + name);
            checks.Add(name);
        }

        TeardownFlows(events, Verify);
        if (OperatingSystem.IsMacOS())
        {
            MacTeardownFlows(events, Verify);
            MacFormatFlows(Verify);
            MacForeignFlows(boards, Verify);
            MacDragFlows(boards, events, Verify);
        }

        using var stream = File.Create(Path.Combine(output, "review-transfers.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("status", "passed");
        json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        WriteStrings(json, "checks", checks);
        WriteStrings(json, "events", events);
        json.WriteStartObject("boards");
        foreach (var (name, items) in boards)
        {
            json.WriteStartArray(name);
            foreach (var types in items)
            {
                json.WriteStartArray();
                foreach (var type in types)
                    json.WriteStringValue(type);
                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    static void WriteStrings(Utf8JsonWriter json, string name, IEnumerable<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
            json.WriteStringValue(value);
        json.WriteEndArray();
    }

    static void TeardownFlows(List<string> events, Action<bool, string> check)
    {
        int thread = Environment.CurrentManagedThreadId;
        var errors = new List<Exception>();
        int releases = 0;
        bool releaseOnMain = false;
        var app = new App(new("Review transfer teardown", QuitOnLastWindow: false, Hidden: true));
        app.UnhandledException += errors.Add;
        Clipboard.Write(TransferData.Text("teardown"), () =>
        {
            releases++;
            releaseOnMain = Environment.CurrentManagedThreadId == thread;
            throw new InvalidOperationException("release hook failure");
        });
        bool completed = false;
        try
        {
            app.Dispose();
            completed = true;
        }
        catch (Exception ex)
        {
            events.Add("portable teardown threw " + ex.GetType().Name);
        }

        events.Add($"portable teardown releases={releases} reports={errors.Count}");
        check(completed && releases == 1 && releaseOnMain, "a throwing clipboard release hook cannot interrupt App teardown");
        check(errors.Count == 1 && errors[0].Message == "release hook failure", "the release failure is reported once through the App");
    }

    static Func<byte[]> Failing(Action fetched, string message) => () =>
    {
        fetched();
        throw new IOException(message);
    };

    static Func<byte[]> Lazy(Action fetched, string text) => () =>
    {
        fetched();
        return Encoding.UTF8.GetBytes(text);
    };
}
