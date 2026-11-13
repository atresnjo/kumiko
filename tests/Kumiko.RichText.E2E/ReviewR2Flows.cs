using System.Collections.Immutable;
using System.Text.Json;
using Kumiko.RichText;
using Kumiko.UI;

static class ReviewR2Flows
{
    internal static void Run(string output, Action<bool, string> check)
    {
        var results = new List<(string Name, string? Error)>();
        var carets = new List<(string Label, RichSelection Selection, Rect Rect)>();
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
                throw new InvalidOperationException("R2 rich behavior assertion failed.");
        }
        Case("R2-P1 uniform RTL wrap navigation selection and IME follow visual direction", () =>
        {
            using var program = EditorApp.CreateProgram();
            using var ui = new Tester(program, new(Width: 700, Height: 480, DevMode: true));
            ui.Get(Role.TextField, "Editor").Click();
            program.Send(new EditorMessage.Replace(RichDocument.Create(string.Concat(Enumerable.Repeat("שלום עולם טקסט עברי וארוך ", 8)))));
            ui.Settle();
            Rect Record(string label)
            {
                var rect = ui.Context.Nodes.ToArray().Single(n => n.Key == "caret").Rect;
                carets.Add((label, program.Model.Editor.Selection, rect));
                return rect;
            }
            ui.Key(Key.End);
            int boundary = program.Model.Editor.Selection.Cursor;
            var end = Record("RTL first End");
            ui.Key(Key.Left);
            var next = Record("RTL End Left");
            ui.Screenshot(Path.Combine(output, "review-r2-rtl-left.png"));
            File.WriteAllText(Path.Combine(output, "review-r2-rtl-left.txt"), ui.Dump());
            Require(program.Model.Editor.Selection.Cursor == boundary && next.Y > end.Y && next.X > end.X);
            ui.Key(Key.Left);
            var advanced = Record("RTL End Left Left");
            Require(program.Model.Editor.Selection.Cursor > boundary && advanced.Y == next.Y && advanced.X < next.X);
            ui.Key(Key.Right);
            Require(program.Model.Editor.Selection.Cursor == boundary);
            ui.Key(Key.Home);
            ui.Key(Key.Right);
            var previous = Record("RTL second Home Right");
            ui.Screenshot(Path.Combine(output, "review-r2-rtl-right.png"));
            File.WriteAllText(Path.Combine(output, "review-r2-rtl-right.txt"), ui.Dump());
            Require(program.Model.Editor.Selection.Cursor == boundary && previous.Y == end.Y && Math.Abs(previous.X - end.X) < 1);
            ui.Key(Key.Left, Modifiers.Shift);
            ui.Key(Key.Left, Modifiers.Shift);
            Record("RTL Shift Left across wrap");
            Require(program.Model.Editor.Selection.Anchor == boundary && program.Model.Editor.Selection.Cursor > boundary);
            ui.Key(Key.Left);
            Require(program.Model.Editor.Selection.Empty && program.Model.Editor.Selection.Cursor > boundary);
            ui.Key(Key.Home);
            ui.Compose("ע", false);
            var marked = Record("RTL wrapped composition");
            Require(marked.Y == next.Y);
            ui.Screenshot(Path.Combine(output, "review-r2-rtl-ime.png"));
            File.WriteAllText(Path.Combine(output, "review-r2-rtl-ime.txt"), ui.Dump());
            ui.Key(Key.Escape);
        });
        Case("R2-P1 visual arrows traverse RTL and opposite-direction paragraph boundaries", () =>
        {
            using var program = EditorApp.CreateProgram();
            using var ui = new Tester(program, new(Width: 700, Height: 480));
            ui.Get(Role.TextField, "Editor").Click();
            program.Send(new EditorMessage.Replace(RichDocument.Create("שלום\nעולם\nplain")));
            ui.Settle();
            ui.Key(Key.Right);
            Require(program.Model.Editor.Selection.Cursor == 0);
            ui.Key(Key.End);
            ui.Key(Key.Left);
            Require(program.Model.Editor.Selection.Cursor == 5);
            ui.Key(Key.Right);
            Require(program.Model.Editor.Selection.Cursor == 4);
            ui.Key(Key.Left);
            ui.Key(Key.End);
            ui.Key(Key.Left);
            Require(program.Model.Editor.Selection.Cursor == 10);
            ui.Key(Key.Left);
            Require(program.Model.Editor.Selection.Cursor == 9);
            program.Send(new EditorMessage.Replace(RichDocument.Create("שלום").Select(4, 4)));
            ui.Settle();
            ui.Key(Key.Left);
            Require(program.Model.Editor.Selection.Cursor == 4);
        });
        Case("R2-P2 Markdown preserves literal edge whitespace attributes and supported paragraph styles", () =>
        {
            var styles = new[] { new RichParagraphStyle(), new(Heading: 2), new(Indent: 2), new(Quote: true, Indent: 1),
                new(List: RichList.Bullet, Indent: 2), new(List: RichList.Number), new(List: RichList.Task, Checked: true, Indent: 1) };
            foreach (string literal in new[] { " a", "  a  ", "\ta\t", " ", "\t", "&#32; ", "&amp;", "* a*", "\\ ", "[]()" })
                foreach (var style in styles)
                    foreach (int bits in Enumerable.Range(0, 32))
                    {
                        var attributes = new RichAttributes(Bold: (bits & 1) != 0, Italic: (bits & 2) != 0, Strike: (bits & 4) != 0,
                            Code: (bits & 8) != 0, Link: (bits & 16) != 0 ? "https://example.com/safe" : null);
                        var value = RichValue.Text(literal, attributes, style);
                        var restored = RichExchange.ParseMarkdown(RichExchange.Markdown(value));
                        Require(restored == value);
                    }
            var adjacent = new RichValue(ImmutableList.Create(new RichParagraph(ImmutableList.Create(
                new RichRun(" a", new(Italic: true)), new RichRun(" b ", new(Bold: true)), new RichRun("&#9;", new())), new())));
            Require(RichExchange.ParseMarkdown(RichExchange.Markdown(adjacent)) == adjacent);
        });
        using var json = new Utf8JsonWriter(File.Create(Path.Combine(output, "review-r2-rich.json")), new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("boundary", "Tester/private input and native offscreen CoreText; simulated IME, managed bounded Markdown subset");
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
        foreach (var caret in carets)
        {
            json.WriteStartObject();
            json.WriteString("label", caret.Label);
            json.WriteNumber("cursor", caret.Selection.Cursor);
            json.WriteString("affinity", caret.Selection.Affinity.ToString());
            json.WriteNumber("x", caret.Rect.X);
            json.WriteNumber("y", caret.Rect.Y);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
        json.Flush();
        foreach (var result in results)
            check(result.Error is null, result.Name + (result.Error is null ? "" : ": " + result.Error));
    }
}
