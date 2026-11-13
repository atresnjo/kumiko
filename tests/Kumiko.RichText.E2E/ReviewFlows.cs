using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Kumiko.RichText;
using Kumiko.UI;

static class ReviewFlows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<(string Name, string? Error)>();
        void Case(string name, Action body)
        {
            try
            {
                body();
                results.Add((name, null));
            }
            catch (Exception error)
            {
                results.Add((name, error.GetType().Name + ": " + error.Message));
            }
        }
        static void Require(bool value)
        {
            if (!value)
                throw new InvalidOperationException("Behavior assertion failed.");
        }
        Case("P2 typing IME and paste join graphemes with base attributes and valid history", () =>
        {
            using var program = EditorApp.CreateProgram();
            using var ui = new Tester(program, new(Width: 700, Height: 480, DevMode: true));
            ui.Get(Role.TextField, "Editor").Click();
            foreach (var input in new[] { "typing", "IME", "paste" })
            {
                program.Send(new EditorMessage.Replace(RichDocument.Create("e").Select(1, 1)));
                ui.Settle();
                ui.Key(Key.B, Modifiers.Primary);
                if (input == "IME")
                {
                    ui.Compose("\u0301", false);
                    ui.Compose("\u0301", true);
                }
                else if (input == "paste")
                {
                    ui.Context.Clipboard = "\u0301";
                    ui.Key(Key.V, Modifiers.Primary);
                }
                else
                    ui.TypeText("\u0301");
                Require(program.Model.Editor.Value.PlainText == "e\u0301" && !program.Model.Editor.Value.AttributesAt(0).Bold &&
                    program.Model.Editor.Selection.Cursor == 2);
                ui.Key(Key.Z, Modifiers.Primary);
                Require(program.Model.Editor.Value.PlainText == "e");
                ui.Key(Key.Z, Modifiers.Primary | Modifiers.Shift);
                Require(program.Model.Editor.Value.PlainText == "e\u0301");
            }
        });
        Case("P2 emoji joining and deletion keep complete grapheme selection", () =>
        {
            var emoji = RichDocument.Create("👩").Select(2, 2).Apply(new RichCommand.Toggle(RichFormat.Bold))
                .Apply(new RichCommand.Insert("\u200d💻"));
            Require(emoji.Value.PlainText == "👩‍💻" && emoji.Value.Snap(emoji.Selection.Cursor) == emoji.Value.Length && !emoji.Value.AttributesAt(0).Bold);
            var flags = RichDocument.Create("🇺X🇸").Select(2, 3).Apply(new RichCommand.Insert(""));
            Require(flags.Value.PlainText == "🇺🇸" && flags.Selection.Cursor == flags.Value.Length);
            Require(flags.Apply(new RichCommand.Undo()).Value.PlainText == "🇺X🇸");
            var prepended = RichDocument.Create("\u0301").Apply(new RichCommand.Toggle(RichFormat.Bold)).Apply(new RichCommand.Insert("e"));
            Require(prepended.Value.PlainText == "e\u0301" && prepended.Value.AttributesAt(0).Bold && prepended.Selection.Cursor == 2);
            var attachment = new RichAttachment("object", "Object", "application/octet-stream", ImmutableArray<byte>.Empty);
            var markedObject = RichDocument.Create("").Apply(new RichCommand.Attachment(attachment)).Apply(new RichCommand.Insert("\u0301"));
            Require(markedObject.Value.AttributesAt(0).Attachment == attachment && markedObject.Selection.Cursor == markedObject.Value.Length);
        });
        Case("P4 supported combined Markdown formats and escaped delimiters roundtrip", () =>
        {
            foreach (int bits in Enumerable.Range(0, 32))
            {
                var attributes = new RichAttributes(Bold: (bits & 1) != 0, Italic: (bits & 2) != 0, Strike: (bits & 4) != 0,
                    Code: (bits & 8) != 0, Link: (bits & 16) != 0 ? "https://example.com/safe" : null);
                var value = RichValue.Text("Hello * [x] ` \\ ~~ 👩", attributes);
                var roundtrip = RichExchange.ParseMarkdown(RichExchange.Markdown(value));
                Require(roundtrip.PlainText == value.PlainText && roundtrip.Paragraphs[0].Runs.All(r => r.Attributes == attributes));
            }
            var adjacent = new RichValue(ImmutableList.Create(new RichParagraph(ImmutableList.Create(
                new RichRun("one*", new(Bold: true)), new RichRun("two`", new(Bold: true, Italic: true)),
                new RichRun("three", new(Italic: true))), new())));
            Require(RichExchange.ParseMarkdown(RichExchange.Markdown(adjacent)) == adjacent);
        });
        Case("P1 wrapped navigation selection Home End and IME use the painted visual line", () =>
        {
            using var program = EditorApp.CreateProgram();
            using var ui = new Tester(program, new(Width: 700, Height: 480, DevMode: true));
            ui.Get(Role.TextField, "Editor").Click();
            program.Send(new EditorMessage.Replace(RichDocument.Create(string.Concat(Enumerable.Repeat("This paragraph wraps because its content continues for a while. ", 5)))));
            ui.Settle();
            Rect Caret() => ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
            var first = Caret();
            ui.Key(Key.Down);
            var second = Caret();
            int lineStart = program.Model.Editor.Selection.Cursor;
            File.WriteAllText(Path.Combine(output, "review-wrap-down.txt"), ui.Dump());
            ui.Screenshot(Path.Combine(output, "review-wrap-down.png"));
            Require(second.Y > first.Y && Math.Abs(second.X - first.X) < 2);
            ui.Key(Key.Down);
            Require(Caret().Y > second.Y && Math.Abs(Caret().X - first.X) < 2);
            ui.Key(Key.Up);
            Require(Math.Abs(Caret().Y - second.Y) < 1 && program.Model.Editor.Selection.Cursor == lineStart);
            ui.Key(Key.End);
            Require(Math.Abs(Caret().Y - second.Y) < 1 && Caret().X > first.X + 100);
            ui.Key(Key.Home);
            Require(program.Model.Editor.Selection.Cursor == lineStart && Math.Abs(Caret().Y - second.Y) < 1);
            ui.Key(Key.Up, Modifiers.Shift);
            Require(program.Model.Editor.Selection.Start == 0 && program.Model.Editor.Selection.End == lineStart);
            ui.Key(Key.Down);
            ui.Key(Key.Home);
            ui.Compose("日本", false);
            Require(Caret().Y >= second.Y);
            ui.Screenshot(Path.Combine(output, "review-wrap-ime.png"));
            File.WriteAllText(Path.Combine(output, "review-wrap-ime.txt"), ui.Dump());
            ui.Key(Key.Escape);
        });
        Case("P3 timed paragraph navigation retains native caret output", () =>
        {
            using var program = EditorApp.CreateProgram();
            using var ui = new Tester(program, new(Width: 700, Height: 480));
            ui.Get(Role.TextField, "Editor").Click();
            using var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-navigation-timing.json")), new()
            {
                Indented = true
            });
            json.WriteStartArray();
            foreach (int length in new[] { 100, 1000, 3000 })
            {
                program.Send(new EditorMessage.Replace(RichDocument.Create(new string('a', length))));
                ui.Settle();
                var before = ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
                json.WriteStartObject();
                json.WriteNumber("characters", length);
                json.WriteStartArray("rightMilliseconds");
                for (int i = 0; i < 3; i++)
                {
                    var timer = Stopwatch.StartNew();
                    ui.Key(Key.Right);
                    timer.Stop();
                    json.WriteNumberValue(timer.Elapsed.TotalMilliseconds);
                    Require(program.Model.Editor.Selection.Cursor == 1);
                    var after = ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
                    Require(after.X > before.X && Math.Abs(after.Y - before.Y) < 1);
                    ui.Key(Key.Left);
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
        });
        using (var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-cases.json")), new()
        {
            Indented = true
        }))
        {
            json.WriteStartArray();
            foreach (var result in results)
            {
                json.WriteStartObject();
                json.WriteString("case", result.Name);
                json.WriteString("error", result.Error);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }
        foreach (var result in results)
            check(result.Error is null, result.Name + (result.Error is null ? "" : ": " + result.Error));
    }
}
