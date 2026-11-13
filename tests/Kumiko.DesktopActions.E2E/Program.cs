using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopNotes;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Runtime;
using Kumiko.UI;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/desktop/e2e");
Directory.CreateDirectory(output);
var checks = new List<string>();
void Check(bool value, string name)
{
    if (!value)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
var store = new MemoryNoteStore();
using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(store));
program.DevMode = true;
using var tester = new Tester(program, new(Width: 560, Height: 380, DevMode: true));
var first = tester.In(new NoteWindow.Editor(1));
var second = tester.In(new NoteWindow.Editor(2));
first.Context.ReduceMotion = true;
second.Context.ReduceMotion = true;
MenuItem Item(Tester window, string id) => window.DesktopMenu.Single(item => item.Id == id);
void Menu(Tester window, string id)
{
    var item = Item(window, id);
    item.Click!(item);
    tester.Settle();
}
void WaitForSave(int id, int count)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
    while (program.Model.Notes[id].Saves < count && DateTime.UtcNow < deadline)
    {
        tester.Settle();
        Thread.Sleep(1);
    }
    Check(program.Model.Notes[id].Saves == count, $"note {id} completed save {count} exactly once");
}
void Edit(Tester window, string text)
{
    window.Get(Role.TextField, "Note text").Fill(text);
    tester.Settle();
}

Check(Item(first, "save").Disabled, "clean note disables Save in desktop menus");
var cleanSave = Item(first, "save");
cleanSave.Click!(cleanSave);
tester.Settle();
Check(program.Model.Notes[1].Saves == 0, "disabled menu callback cannot save");
program.Send(new NoteMessage.OpenPopup(1, "palette", 0));
tester.Settle();
first.Get(Role.TextField, "Type to search…").Fill("Save note");
Check(!first.Get(Role.MenuItem, "Save note").Enabled, "clean note disables the palette save row");
first.Key(Key.Enter);
Check(program.Model.Notes[1].Popup == "palette" && program.Model.Notes[1].Saves == 0,
    "Enter cannot execute the only disabled palette result");
first.Key(Key.Escape);
tester.Settle();
Edit(first, "toolbar text");
var staleSave = Item(first, "save");
first.Get(Role.Button, "Save note").Click();
WaitForSave(1, 1);
Check(store.Read(1) == "toolbar text" && store.Read(2) is null, "toolbar saves only the targeted note through the injected store");
staleSave.Click!(staleSave);
tester.Settle();
Check(program.Model.Notes[1].Saves == 1, "retained pre-save menu callback cannot execute after a model transition");
Edit(first, "menu text");
Menu(first, "save");
WaitForSave(1, 2);
Edit(first, "shortcut text");
first.Get(Role.TextField, "Note text").Perform(new(AccessibilityAction.Focus));
var editorFocus = first.Context.Focus;
first.Key(Key.S, Modifiers.Primary);
WaitForSave(1, 3);
Check(first.Context.Focus == editorFocus && second.Context.Focus == 0,
    "save shortcut preserves the originating editor focus");
Check(program.Model.Notes[2].Text == "", "first window actions never edit the second document");
Edit(first, "palette text");
program.Send(new NoteMessage.OpenPopup(1, "palette", editorFocus));
tester.Settle();
first.Screenshot(Path.Combine(output, "palette.png"));
File.WriteAllText(Path.Combine(output, "palette.txt"), first.Dump());
var savesBeforePalette = program.Model.Notes[1].Saves;
first.Key(Key.S, Modifiers.Primary);
tester.Settle();
Check(program.Model.Notes[1].Saves == savesBeforePalette, "palette owns shortcuts while open");
first.Get(Role.TextField, "Type to search…").Fill("Save note");
first.Context.Input.Composition = "未";
first.Context.Input.Key = Key.Enter;
first.Frame();
Check(program.Model.Notes[1].Popup == "palette" && program.Model.Notes[1].Saves == 3,
    "IME Enter in the palette does not select a command");
first.Context.Input.Composition = "";
first.Frame();
first.Get(Role.TextField, "Type to search…").Fill("Save note");
first.Key(Key.Enter);
WaitForSave(1, 4);
tester.Settle();
Check(program.Model.Notes[1].Popup is null && first.Context.Focus == editorFocus, "palette selection closes and restores editor focus");
Check(store.Read(1) == "palette text", "palette executes the same save effect");
Menu(second, "pin");
Check(program.Model.Notes[2].Pinned && !program.Model.Notes[1].Pinned && Item(second, "pin").Checked,
    "checked state targets the second document and updates its menu");
Check(second.HasText("✓ Pin note") && second.Context.Accessibility.Single(n => n.Role == Role.Button && n.Name == "Pin note").Checked == true,
    "toolbar reflects the same checked state semantically");
second.Screenshot(Path.Combine(output, "note-two.png"));
File.WriteAllText(Path.Combine(output, "note-two.txt"), second.Dump());
var paletteOpener = second.Get(Role.Button, "Commands").Id;
second.Get(Role.Button, "Commands").Click();
tester.Settle();
Check(second.Context.Accessibility.Single(n => n.Role == Role.MenuItem && n.Name == "Pin note").Checked == true,
    "palette exposes the same checked state as toolbar and desktop menu");
second.Screenshot(Path.Combine(output, "checked-palette.png"));
File.WriteAllText(Path.Combine(output, "checked-palette.txt"), second.Dump());
second.Key(Key.Escape);
tester.Settle();
Check(second.Context.Focus == paletteOpener && program.Model.Notes[2].Pinned,
    "palette Escape restores its actual trigger without toggling the selected action");
second.Key(Key.P, Modifiers.Primary | Modifiers.Shift);
tester.Settle();
Check(!program.Model.Notes[2].Pinned && !Item(second, "pin").Checked, "keyboard toggle updates toolbar and menu state once");
Edit(second, "context text");
second.Get(Role.Button, "Note actions").Perform(new(AccessibilityAction.Focus));
second.Key(Key.F10, Modifiers.Shift);
tester.Settle();
second.Screenshot(Path.Combine(output, "context-menu.png"));
File.WriteAllText(Path.Combine(output, "context-menu.txt"), second.Dump());
second.Get(Role.MenuItem, "Save note").Click();
WaitForSave(2, 1);
Check(program.Model.Notes[2].Popup is null && store.Read(2) == "context text", "context menu shares save definition and closes after selection");
Check(program.History.Count(step => step.Message is NoteMessage.Save { Id: 1 }) == 4 &&
    program.History.Count(step => step.Message is NoteMessage.Save { Id: 2 }) == 1,
    "menu, toolbar, shortcut, palette and context menu each dispatch exactly one Save message");

Edit(first, "IME draft");
first.Get(Role.TextField, "Note text").Perform(new(AccessibilityAction.Focus));
first.Context.Input.Composition = "未";
first.Context.Input.Key = Key.S;
first.Context.Input.Modifiers = Modifiers.Primary;
first.Frame();
tester.Settle();
Check(program.Model.Notes[1].Saves == 4, "active IME composition suppresses save shortcuts");
first.Context.Input.CommitComposition = true;
first.Frame();
first.Key(Key.A, Modifiers.Primary);
first.TypeText("editor replacement");
tester.Settle();
Check(program.Model.Notes[1].Text == "editor replacement", "editor select-all and text input retain precedence");
first.Key(Key.S, Modifiers.Primary | Modifiers.Alt);
tester.Settle();
Check(program.Model.Notes[1].Saves == 4, "extra modifiers do not match a shortcut");
var oldPin = Item(second, "pin");
Menu(second, "close");
oldPin.Click!(oldPin);
tester.Settle();
Check(!program.Model.Notes.ContainsKey(2) && program.Windows.Count == 1, "removed window rejects retained actions");
Check(program.History.Any(step => step.Message is NoteMessage.Save { Id: 1 } &&
    step.Source?.Key == "save" && step.Source.Window == program.Windows.Single().Key),
    "actions record their stable identity and originating window in the session");
first.Screenshot(Path.Combine(output, "note-one.png"));
File.WriteAllText(Path.Combine(output, "warnings.json"), JsonSerializer.Serialize(first.Context.Warnings.ToList(), EvidenceJson.Default.ListLayoutWarning));
Check(first.Context.Warnings.Count == 0, "example produces no layout diagnostics");
File.WriteAllText(Path.Combine(output, "note-one.txt"), first.Dump());
program.Save(Path.Combine(output, "session.jsonl"));
program.SaveModel(Path.Combine(output, "model.json"));
using var replay = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new RejectStore()));
replay.Replay(Path.Combine(output, "session.jsonl"));
using var replayTester = new Tester(replay, new(Width: 560, Height: 380));
Check(replay.Model == program.Model || replay.Model.Notes.SequenceEqual(program.Model.Notes), "session replay restores documents and actions without executing saves");
replayTester.Screenshot(Path.Combine(output, "replay.png"));
File.WriteAllText(Path.Combine(output, "replay.txt"), replayTester.Dump());
ReviewFlows.Run(output, Check);
SaveRace.Run(output, Check);
ScopeFlows.Run(output, Check);
NativeFlows.Run(output, Check);
NativeMenuFlows.RunTemplate(output, Check);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(checks, EvidenceJson.Default.ListString));
Console.WriteLine($"PASS: {checks.Count} desktop E2E checks; artifacts: {output}");

sealed class RejectStore : INoteStore
{
    public Task Save(int id, string text, CancellationToken token) => throw new InvalidOperationException("Replay ran storage");
}
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<LayoutWarning>))]
internal partial class EvidenceJson : JsonSerializerContext;
