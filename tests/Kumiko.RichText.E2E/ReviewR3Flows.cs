using System.Collections.Immutable;
using System.Text.Json;
using Kumiko.RichText;
using Kumiko.UI;

static class ReviewR3Flows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<(string Name, string? Error)>();
        var observations = new List<(string Label, int Cursor, RichAffinity Affinity, float X, float Y)>();
        void Case(string name, Action body)
        {
            string? error = null;
            try
            {
                body();
            }
            catch (Exception failure)
            {
                error = failure.GetType().Name + ": " + failure.Message;
            }
            results.Add((name, error));
        }
        static void Require(bool value)
        {
            if (!value)
                throw new InvalidOperationException("R3 rich behavior assertion failed.");
        }
        foreach (string text in new[] { "שלום abc עולם", "abc שלום xyz" })
            foreach (bool reverse in new[] { false, true })
                foreach (var key in new[] { Key.Left, Key.Right })
                    Case($"R3-P1 {key} collapses native visual island endpoints reverse={reverse} text={text}", () =>
                    {
                        using var program = EditorApp.CreateProgram();
                        using var ui = new Tester(program, new(Width: 700, Height: 480, DevMode: true));
                        ui.Get(Role.TextField, "Editor").Click();
                        Rect Endpoint(int position)
                        {
                            program.Send(new EditorMessage.Replace(RichDocument.Create(text).Select(position, position)));
                            ui.Settle();
                            return ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
                        }
                        var a = Endpoint(6);
                        var b = Endpoint(7);
                        int expected = key == Key.Left ? a.X < b.X ? 6 : 7 : a.X > b.X ? 6 : 7;
                        program.Send(new EditorMessage.Replace(RichDocument.Create(text).Select(reverse ? 7 : 6, reverse ? 6 : 7)));
                        ui.Settle();
                        ui.Key(key);
                        var caret = ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
                        observations.Add((text + ":" + key + ":" + reverse, program.Model.Editor.Selection.Cursor,
                            program.Model.Editor.Selection.Affinity, caret.X, caret.Y));
                        string prefix = "review-r3-island-" + (text.StartsWith("שלום", StringComparison.Ordinal) ? "rtl" : "ltr") + "-" + key + "-" + reverse;
                        ui.Screenshot(Path.Combine(output, prefix + ".png"));
                        File.WriteAllText(Path.Combine(output, prefix + ".txt"), ui.Dump());
                        Require(program.Model.Editor.Selection.Empty && program.Model.Editor.Selection.Cursor == expected &&
                            Math.Abs(caret.X - (key == Key.Left ? Math.Min(a.X, b.X) : Math.Max(a.X, b.X))) < 1);
                    });
        Case("R3-P2 code paragraphs preserve noncolliding fences and adjacent paragraph content", () =>
        {
            foreach (string text in new[] { "```", "````", new string('`', 127), "a```b", "", "  ```  ", "normal", "```\n\n````\ntext" })
            {
                var value = RichValue.Text(text, new(Code: true), new(Code: true));
                Require(RichExchange.ParseMarkdown(RichExchange.Markdown(value)) == value);
            }
            var code = RichValue.Text("````", new(Code: true), new(Code: true)).Paragraphs[0];
            var plain = RichValue.Text("plain ``` content").Paragraphs[0];
            var mixed = new RichValue(ImmutableList.Create(plain, code, plain, code));
            Require(RichExchange.ParseMarkdown(RichExchange.Markdown(mixed)) == mixed);
            var imported = RichExchange.ParseMarkdown("`````\n```\n````\n`````\nafter");
            Require(imported.PlainText == "```\n````\nafter" && imported.Paragraphs[0].Style.Code && !imported.Paragraphs[^1].Style.Code);
            var unclosed = RichExchange.ParseMarkdown("````\n<script>literal</script>\n```");
            Require(unclosed.PlainText == "<script>literal</script>\n```" && unclosed.Paragraphs.All(p => p.Style.Code));
        });
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r3-rich.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartObject();
            json.WriteString("boundary", "Tester/private input and native offscreen endpoint geometry; bounded managed Markdown, no OS input or clipboard");
            json.WriteStartArray("cases");
            foreach (var result in results)
            {
                json.WriteStartObject();
                json.WriteString("name", result.Name);
                json.WriteString("error", result.Error);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("carets");
            foreach (var item in observations)
            {
                json.WriteStartObject();
                json.WriteString("label", item.Label);
                json.WriteNumber("cursor", item.Cursor);
                json.WriteString("affinity", item.Affinity.ToString());
                json.WriteNumber("x", item.X);
                json.WriteNumber("y", item.Y);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        foreach (var result in results)
            check(result.Error is null, result.Name + (result.Error is null ? "" : ": " + result.Error));
    }
}
