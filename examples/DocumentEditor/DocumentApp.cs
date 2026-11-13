using System.Collections.Immutable;
using Kumiko.Documents;
using Kumiko.RichText;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.DocumentEditor;

public sealed record DocumentWindow(string Id);
public sealed record DocumentModel(DocumentWorkspace<RichValue> Workspace, ImmutableDictionary<string, RichDocument> Editors, string Destination, string? Error = null);
public abstract partial record DocumentMessage
{
    public sealed record Edit(string Id, RichCommand Command) : DocumentMessage;
    public sealed record Save(string Id) : DocumentMessage;
    public sealed record Saved(DocumentSaved<RichValue> Result) : DocumentMessage;
    public sealed record Failed(DocumentRequest<RichValue> Request, string Error, bool Conflict) : DocumentMessage;
    public sealed record Quit : DocumentMessage;
    public sealed record Close(string Id) : DocumentMessage;
    public sealed record Decision(string Id, CloseDecision Value) : DocumentMessage;
    public sealed record Destination(string Path) : DocumentMessage;
    public sealed record Duplicate(string Id) : DocumentMessage;
}
public sealed partial class DocumentApp : IKumiko<DocumentModel, DocumentMessage>, IKumikoWindows<DocumentModel, DocumentMessage, DocumentWindow>
{
    public static DocumentModel Init
    {
        get
        {
            var editor = RichDocument.Create("A reusable editor inside a document application.\nEdit while saving; the new revision stays dirty.");
            var document = Document<RichValue>.Create("note", "Untitled", editor.Value);
            return new(new(ImmutableList.Create(document)), ImmutableDictionary<string, RichDocument>.Empty.Add(document.Id, editor), "");
        }
    }
    public static IEnumerable<WindowSpec<DocumentWindow, DocumentMessage>> Windows(DocumentModel model) => model.Workspace.Documents.Select(d =>
        new WindowSpec<DocumentWindow, DocumentMessage>(new(d.Id), (d.Dirty ? "* " : "") + d.Title, new DocumentMessage.Close(d.Id)) { Width = 820, Height = 640 });
    public static NavigationRoot<DocumentMessage> WindowRoot(DocumentModel model, DocumentWindow window) => NavigationRoot<DocumentMessage>.Create(
        NavigationHistory<DocumentWindow>.Start(window), "document", id => new DocumentScreen(model, id.Id).Map<DocumentMessage>(static m => m),
        static _ => throw new InvalidOperationException("One screen per document window."));
    public static (DocumentModel, Cmd<DocumentMessage>) Update(DocumentModel model, DocumentMessage message)
    {
        switch (message)
        {
            case DocumentMessage.Edit edit:
                var editor = model.Editors[edit.Id].Apply(edit.Command);
                var document = model.Workspace.Documents.Single(d => d.Id == edit.Id).Edit(editor.Value);
                return (model with
                {
                    Editors = model.Editors.SetItem(edit.Id, editor),
                    Workspace = model.Workspace.Put(document)
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Destination destination:
                return (model with
                {
                    Destination = destination.Path
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Save save:
                var current = model.Workspace.Documents.Single(d => d.Id == save.Id);
                if (current.Pending is not null || string.IsNullOrWhiteSpace(model.Destination) && current.Path is null)
                    return (model with
                    {
                        Error = "Enter an explicit destination path."
                    }, Cmd<DocumentMessage>.None);
                var start = current.BeginSave(current.Path is null ? model.Destination : null);
                return (model with
                {
                    Workspace = model.Workspace.Put(start.Document),
                    Error = null
                }, DocumentEffects.Save(start.Request, new RichValueCodec(),
                    static result => (DocumentMessage)new DocumentMessage.Saved(result),
                    error => new DocumentMessage.Failed(start.Request, error.Message, error is DocumentConflictException)));
            case DocumentMessage.Saved saved:
                var original = model.Workspace.Documents.SingleOrDefault(d => d.Id == saved.Result.Request.Id);
                return (original is null ? model : model with
                {
                    Workspace = model.Workspace.Put(original.Saved(saved.Result)).Remember(saved.Result.Request.Path)
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Failed failed:
                var before = model.Workspace.Documents.Single(d => d.Id == failed.Request.Id);
                return (model with
                {
                    Workspace = model.Workspace.Put(before.Failed(failed.Request, failed.Error, failed.Conflict)),
                    Error = failed.Error
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Quit:
                return (model with
                {
                    Workspace = model.Workspace.RequestQuit()
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Close close:
                return (model with
                {
                    Workspace = model.Workspace.RequestClose(close.Id)
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Decision decision:
                return (model with
                {
                    Workspace = model.Workspace.ResolveClose(decision.Id, decision.Value)
                }, Cmd<DocumentMessage>.None);
            case DocumentMessage.Duplicate duplicate:
                var source = model.Workspace.Documents.Single(d => d.Id == duplicate.Id);
                string id = source.Id + "-copy";
                if (model.Editors.ContainsKey(id))
                    return (model, Cmd<DocumentMessage>.None);
                return (model with
                {
                    Workspace = model.Workspace.Put(source.Duplicate(id, source.Title + " copy")),
                    Editors = model.Editors.Add(id, RichDocument.FromValue(source.Value))
                }, Cmd<DocumentMessage>.None);
            default:
                throw new ArgumentOutOfRangeException(nameof(message));
        }
    }
}
