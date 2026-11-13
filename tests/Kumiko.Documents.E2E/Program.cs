using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Kumiko.Documents;

var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/documents");
Directory.CreateDirectory(output);
File.Delete(Path.Combine(output, "report.json"));
var root = Path.Combine(output, "files");
if (Directory.Exists(root))
    Directory.Delete(root, true);
Directory.CreateDirectory(root);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException(name);
    checks.Add(name);
}
async Task Conflict(Func<Task> operation, string name)
{
    try
    {
        await operation();
        Check(false, name);
    }
    catch (DocumentConflictException)
    {
        Check(true, name);
    }
}
using var store = new FileDocumentStore();
var codec = new Utf8DocumentCodec();
var path = Path.Combine(root, "note.txt");
ReplayFlows.Run(Check, path);
await ConcurrentFlows.Run(root, Check);
var doc = Document<string>.Create("one", "Untitled", "initial");
Check(doc.Dirty && doc.Path is null, "new document needs saving");
var pending = doc.BeginSave(path, FileVersion.Missing);
doc = pending.Document;
doc = doc.Edit("edited during save");
var saved = await store.SaveAsync(pending.Request, codec, default);
doc = doc.Saved(saved);
Check(doc.Dirty && doc.Value == "edited during save" && doc.SavedValue == "initial", "late save preserves newer edit and records captured value");
Check(doc.Saved(saved) == doc, "duplicate completion is ignored");
var first = doc.BeginSave();
var second = doc.BeginSave();
var winner = await store.SaveAsync(first.Request, codec, default);
await Conflict(async () => await store.SaveAsync(second.Request, codec, default), "concurrent writers from one fingerprint conflict");
doc = first.Document.Saved(winner);
Check(!doc.Dirty, "matching save completion clears dirty state");
await File.WriteAllTextAsync(path, "external");
var changed = await store.ReadAsync(path, codec, default);
Check(doc.Observe(changed.Version).Conflict, "external change is model-owned conflict");
await Conflict(async () => await store.SaveAsync(doc.BeginSave().Request, codec, default), "external content cannot be silently overwritten");
var dirty = doc.Edit("local");
Check(dirty.Reload(changed).Value == "local" && dirty.Reload(changed).Conflict, "external reload preserves dirty content");
Check(doc.Reload(changed).Value == "external" && !doc.Reload(changed).Dirty, "clean external reload adopts disk content");
var revert = dirty.BeginRevert();
Check(revert.Document.Edit("later").Reverted(revert.Request, changed).Value == "later", "held revert cannot discard newer typing");
Check(revert.Document.Reverted(revert.Request, changed).Value == "external", "explicit revert replaces unchanged dirty draft");
var target = Path.Combine(root, "occupied.txt");
await File.WriteAllTextAsync(target, "keep");
await Conflict(async () => await store.SaveAsync(dirty.BeginSave(target, FileVersion.Missing).Request, codec, default), "Save As requires explicit destination fingerprint");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await store.SaveAsync(dirty.BeginSave(Path.Combine(root, "cancelled.txt"), FileVersion.Missing).Request, codec, cancelled.Token);
    Check(false, "cancelled save throws");
}
catch (OperationCanceledException)
{
    Check(!File.Exists(Path.Combine(root, "cancelled.txt")), "cancelled save never creates destination");
}
using var recovery = new DocumentRecovery(root, "test-session");
await recovery.WriteAsync(dirty, codec, default);
var recovered = await recovery.ReadAsync("one", codec, default);
Check(recovered is { Value: "local", Path: not null } && recovered.Dirty, "recovery restores unsaved draft and original disk identity");
Check((await recovery.ReadAllAsync(codec)).Count == 1, "recovery enumerates drafts after session restart without a separate identity index");
try
{
    using var competing = new DocumentRecovery(root, "test-session");
    Check(false, "recovery session has exclusive ownership");
}
catch (IOException)
{
    Check(true, "another instance cannot recover a live session");
}
var copy = dirty.Duplicate("two", "Copy");
Check(copy.Path is null && copy.Dirty && copy.Value == dirty.Value, "duplicate has independent identity and no original path");
var workspace = new DocumentWorkspace<string>(ImmutableList.Create(dirty, copy));
workspace = workspace.RequestQuit();
Check(workspace.Close is { PendingId: "one" } && workspace.Documents.Count == 2, "quit waits for first dirty decision");
workspace = workspace.ResolveClose("one", CloseDecision.Discard);
Check(workspace.Close is { PendingId: "two" } && workspace.Documents.Count == 2, "quit decisions are staged without closing earlier windows");
workspace = workspace.ResolveClose("two", CloseDecision.Cancel);
Check(workspace.Close is null && workspace.Documents.Count == 2, "cancel quit keeps every document open");
workspace = workspace.RequestQuit().ResolveClose("one", CloseDecision.Discard).ResolveClose("two", CloseDecision.Discard);
Check(workspace.Documents.Count == 0, "quit commits only after every dirty decision");
var moved = Path.Combine(root, "renamed.txt");
await Conflict(async () => await store.MoveAsync(path, moved, doc.Version, default), "move rejects externally modified source");
await store.MoveAsync(path, moved, changed.Version, default);
Check(File.Exists(moved) && !File.Exists(path), "verified move preserves complete content");
Check(!Directory.GetFiles(root).Any(p => p.EndsWith(".tmp", StringComparison.Ordinal)), "no abandoned atomic-save temporary files");
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(moved, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    string link = Path.Combine(root, "linked.txt");
    File.CreateSymbolicLink(link, moved);
    var linked = Document<string>.Open("linked", await store.ReadAsync(link, codec, default)).Edit("saved through link").BeginSave();
    await store.SaveAsync(linked.Request, codec, default);
    Check(new FileInfo(link).LinkTarget is not null && await File.ReadAllTextAsync(moved) == "saved through link", "save follows leaf symlink and preserves the link itself");
    Check(File.GetUnixFileMode(moved) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "atomic replacement retains private Unix permissions");
}
await RecoveryLifetimeFlows.Run(root, Check);
ConsumerFlows.Run(output, Check);
using var report = new Utf8JsonWriter(File.Create(Path.Combine(output, "report.json")), new() { Indented = true });
report.WriteStartObject();
report.WriteString("status", "passed");
report.WriteString("boundary", "managed filesystem on " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
report.WriteStartArray("checks");
foreach (var check in checks)
    report.WriteStringValue(check);
report.WriteEndArray();
report.WriteEndObject();
Console.WriteLine($"Passed {checks.Count} document checks: {output}");
