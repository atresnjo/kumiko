using System.Security.Cryptography;
using System.Text.Json;
using Kumiko.RichText;
using Kumiko.Runtime;
using Kumiko.UI;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/rich-text");
Directory.CreateDirectory(output);
File.Delete(Path.Combine(output, "report.json"));
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
ReviewR3Flows.Run(output, Check);
ReviewR2Flows.Run(output, Check);
ReviewFlows.Run(output, Check);
ValueFlows.Run(Check);
NativeAccessibilityFlow.Run(Check);
using var program = EditorApp.CreateProgram();
using var ui = new Tester(program, new(Width: 700, Height: 480, DevMode: true));
ui.Get(Role.TextField, "Editor").Click();
ui.Key(Key.A, Modifiers.Primary);
ui.TypeText("Hello world");
ui.Key(Key.A, Modifiers.Primary);
ui.Key(Key.B, Modifiers.Primary);
Check(program.Model.Editor.Value.Paragraphs[0].Runs[0].Attributes.Bold, "editor shortcut dispatches rich formatting command");
ui.Key(Key.End);
ui.Compose("日本", false);
Check(program.Model.Editor.Value.PlainText == "Hello world", "IME preview stays outside committed model");
ui.Compose("日本", true);
Check(program.Model.Editor.Value.PlainText == "Hello world日本", "IME commit inserts once in typing attributes");
ui.Key(Key.Z, Modifiers.Primary);
Check(program.Model.Editor.Value.PlainText == "Hello world", "one undo removes complete IME commit");
ui.Key(Key.A, Modifiers.Primary);
ui.Key(Key.C, Modifiers.Primary);
ui.Key(Key.End);
ui.Key(Key.V, Modifiers.Primary);
Check(program.Model.Editor.Value.PlainText == "Hello worldHello world" && program.Model.Editor.Value.AttributesAt(12).Bold,
    "private rich clipboard paste preserves formatting");
var node = ui.Context.Accessibility.Single(n => n.Role == Role.TextField && n.Name == "Editor");
Check(node.Properties is { Multiline: true } && node.Value == program.Model.Editor.Value.PlainText, "accessibility exposes committed content and selection");
Check(ui.Context.PerformAccessibilityAction(node.Id, new(AccessibilityAction.SetSelection, Start: 0, End: 5)), "accessibility can select editor text");
ui.Settle();
Check(program.Model.Editor.Selection.End == 5, "accessibility selection uses model-owned editor selection");
ui.Screenshot(Path.Combine(output, "editor.png"));
File.WriteAllText(Path.Combine(output, "editor.txt"), ui.Dump());
ui.WriteWarnings(Path.Combine(output, "editor.warnings.json"));
program.Send(new EditorMessage.Replace(RichDocument.Create("אבג العربية 👩‍💻 e\u0301").Select(0, 0)));
ui.Settle();
ui.Key(Key.Left);
Check(program.Model.Editor.Selection.Cursor == 1, "visual Left from Hebrew paragraph start moves to the next logical grapheme");
ui.Key(Key.Left, Modifiers.Shift);
Check(program.Model.Editor.Selection is { Start: 1, End: 2 }, "bidi shift navigation preserves directed selection");
var highlight = ui.Context.Tree.Take(ui.Context.Count).Single(n => n.Key?.ToString() == "selection:0").Rect;
var shaped = ui.Context.Tree.Take(ui.Context.Count).Single(n => n.Text == program.Model.Editor.Value.PlainText).Rect;
Check(highlight.X >= shaped.X && highlight.Right <= shaped.Right + 1,
    "bidi selection highlight overlaps its native-shaped text");
ui.Screenshot(Path.Combine(output, "bidi-selection.png"));
File.WriteAllText(Path.Combine(output, "bidi-selection.txt"), ui.Dump());
ui.Compose("日本", false);
var marked = ui.Context.Tree.Take(ui.Context.Count).Where(n => n.Key?.StartsWith("composition:", StringComparison.Ordinal) == true).ToArray();
Check(marked.Length > 0 && marked.All(n => n.Rect.X >= 28 && n.Rect.Right <= 665),
    "bidi IME preview paints bounded marked text while committed value stays unchanged");
Check(marked.Length == 2 && Math.Abs(marked[0].Rect.X - marked[1].Rect.X) >= Math.Min(marked[0].Rect.Width, marked[1].Rect.Width) - 1,
    "opposite-direction IME graphemes have distinct nonoverlapping native selection ranges");
ui.Screenshot(Path.Combine(output, "ime-preview.png"));
File.WriteAllText(Path.Combine(output, "ime-preview.txt"), ui.Dump());
ui.Key(Key.Escape);
Check(!program.Model.Editor.Value.PlainText.Contains("日本", StringComparison.Ordinal), "Escape cancels composition without changing committed bidi text");
ui.Compose("stale preedit", false);
program.Send(new EditorMessage.Replace(RichDocument.Create("Restored model")));
ui.Settle();
Check(ui.Context.Edits.Values.All(e => e.Composition.Length == 0), "external model restoration cancels node-owned preedit");
ui.TypeText("!");
Check(program.Model.Editor.Value.PlainText == "!Restored model", "typing after restoration cannot commit stale composition");
program.Send(new EditorMessage.Replace(RichDocument.Create("Inline ").Select(7, 7).Apply(new RichCommand.Attachment(
    new("fixture", "Attachment box", "application/octet-stream", System.Collections.Immutable.ImmutableArray<byte>.Empty, 120, 60)))));
ui.Settle();
ui.Screenshot(Path.Combine(output, "attachment.png"));
File.WriteAllText(Path.Combine(output, "attachment.txt"), ui.Dump());
ui.Key(Key.Backspace);
Check(program.Model.Editor.Value.PlainText == "Inline ", "native editor deletes an attachment as a single grapheme");
program.Send(new EditorMessage.Replace(RichDocument.Create(string.Join('\n', Enumerable.Range(0, 20000).Select(i => "Paragraph " + i)))));
ui.Settle();
Check(ui.Context.Stats.Nodes < 300, "20,000 paragraphs build only visible editor paragraphs");
ui.Key(Key.End, Modifiers.Primary);
Check(program.Model.Editor.Selection.End == program.Model.Editor.Value.Length, "keyboard can navigate to the end of a large document");
ui.Screenshot(Path.Combine(output, "large-end.png"));
File.WriteAllText(Path.Combine(output, "large-end.txt"), ui.Dump());
using var report = new Utf8JsonWriter(File.Create(Path.Combine(output, "report.json")), new() { Indented = true });
report.WriteStartObject();
report.WriteString("status", "passed");
report.WriteString("boundary", "Tester private input/clipboard; native " + ui.Context.TextSystem.Backend + " offscreen shaping; no real OS IME/AT execution");
report.WriteStartArray("checks");
foreach (var check in checks)
    report.WriteStringValue(check);
report.WriteEndArray();
report.WriteStartObject("sha256");
foreach (var path in Directory.GetFiles(output).Where(p => p.EndsWith(".png", StringComparison.Ordinal)))
    report.WriteString(Path.GetFileName(path), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
report.WriteEndObject();
report.WriteEndObject();
Console.WriteLine($"Passed {checks.Count} rich text checks: {output}");

public sealed record EditorModel(RichDocument Editor, NavigationHistory<string> Navigation);
public abstract partial record EditorMessage
{
    public sealed record Edit(RichCommand Command) : EditorMessage;
    public sealed record Replace(RichDocument Document) : EditorMessage;
    public sealed record Navigate(HistoryRequest Request) : EditorMessage;
}
public sealed partial class EditorApp : IKumiko<EditorModel, EditorMessage>
{
    public static EditorModel Init => new(RichDocument.Create("Start writing"), NavigationHistory<string>.Start("editor"));
    public static NavigationRoot<EditorMessage> Root(EditorModel model) => NavigationRoot<EditorMessage>.Create(model.Navigation, "app",
        _ => new EditorScreen(model.Editor).Map<EditorMessage>(static m => m), static r => new EditorMessage.Navigate(r));
    public static (EditorModel, Cmd<EditorMessage>) Update(EditorModel model, EditorMessage message) => (message switch
    {
        EditorMessage.Edit edit => model with { Editor = model.Editor.Apply(edit.Command) },
        EditorMessage.Replace replace => model with { Editor = replace.Document },
        EditorMessage.Navigate navigate => model with { Navigation = model.Navigation.Move(navigate.Request, "app") },
        _ => throw new ArgumentOutOfRangeException(nameof(message))
    }, Cmd<EditorMessage>.None);
}
public sealed class EditorScreen(RichDocument document) : Screen<RichDocument, EditorMessage>(document)
{
    protected override void Render()
    {
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S4).Background(C.Theme.Background);
        Ui.RichTextEditor("editor", "Editor", Model, static command => new EditorMessage.Edit(command), new(Width: 660, Height: 420));
    }
}
