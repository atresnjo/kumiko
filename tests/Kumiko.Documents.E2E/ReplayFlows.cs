using System.Text.Json;
using System.Text.Json.Serialization;
using Kumiko.Documents;
using Kumiko.RichText;

static class ReplayFlows
{
    internal static void Run(Action<bool, string> check, string path)
    {
        var value = RichValue.Text("Rich draft", new(Bold: true));
        var clean = Document<RichValue>.Open("replay", new(path, value, new(true, "initial")));
        var restored = JsonSerializer.Deserialize(JsonSerializer.Serialize(clean, ReplayJson.Default.DocumentRichValue),
            ReplayJson.Default.DocumentRichValue)!;
        check(!restored.Dirty, "JSON model restoration keeps a saved rich document clean");
        var save = clean.Edit(RichValue.Text("Captured", new(Italic: true))).BeginSave();
        var ticket = JsonSerializer.Deserialize(JsonSerializer.Serialize(save.Request, ReplayJson.Default.DocumentRequestRichValue),
            ReplayJson.Default.DocumentRequestRichValue)!;
        var edited = save.Document.Edit(RichValue.Text("Typed while saving"));
        var completed = edited.Saved(new(ticket, new(true, "written")));
        check(completed.Pending is null && completed.Dirty && completed.Value.PlainText == "Typed while saving" &&
            completed.SavedValue.PlainText == "Captured", "serialized save completion preserves edits made in flight");
        check(completed.BeginSave().Document.Saved(new(ticket, new(true, "stale"))).Pending is not null,
            "replayed old ticket cannot complete a newer operation");
        check(edited.Saved(new(ticket with
        {
            Path = path + ".other"
        }, new(true, "wrong"))) == edited,
            "save completion for another target is ignored");
        var untrusted = edited.Saved(new(ticket with
        {
            Value = RichValue.Text("untrusted completion payload")
        }, new(true, "written")));
        check(untrusted.SavedValue.PlainText == "Captured", "completion adopts the model-owned captured value");
        check(save.Document.Failed(ticket, "fixture failure").Pending is null,
            "serialized failure resolves the matching operation");
        var revert = clean.BeginRevert();
        var revertTicket = JsonSerializer.Deserialize(JsonSerializer.Serialize(revert.Request, ReplayJson.Default.DocumentRequestRichValue),
            ReplayJson.Default.DocumentRequestRichValue)!;
        check(revert.Document.Reverted(revertTicket, new(path, RichValue.Text("Disk"), new(true, "disk"))).Value.PlainText == "Disk",
            "serialized revert completion resolves the matching operation");
    }
}

[JsonSerializable(typeof(Document<RichValue>))]
[JsonSerializable(typeof(DocumentRequest<RichValue>))]
internal partial class ReplayJson : JsonSerializerContext;
