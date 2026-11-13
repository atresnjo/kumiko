using Kumiko.Documents;
using Kumiko.RichText;
using Kumiko.Runtime;
using Kumiko.UI;

namespace Kumiko.Examples.DocumentEditor;

public sealed class DocumentScreen(DocumentModel model, string window) : WindowScreen<DocumentModel, DocumentMessage, string>(model, window)
{
    protected override void Render()
    {
        var id = Window;
        var document = Model.Workspace.Documents.Single(d => d.Id == id);
        var actions = Ui.DesktopActions([
            new Kumiko.Desktop.DesktopAction<DocumentMessage>("document.save", "Save", new DocumentMessage.Save(id), Key.S, Enabled: document.Pending is null && document.Dirty),
            new Kumiko.Desktop.DesktopAction<DocumentMessage>("document.duplicate", "Duplicate", new DocumentMessage.Duplicate(id)),
            new Kumiko.Desktop.DesktopAction<DocumentMessage>("document.close", "Close", new DocumentMessage.Close(id), Key.W)
        ]);
        using var page = C.Div(key: "page").SizeFull().Padding(Space.S5).Gap(Space.S3).Background(C.Theme.Background);
        using (C.Div(key: "toolbar").Row().Gap(Space.S2))
        {
            C.Text(document.Title + (document.Dirty ? " • Unsaved" : " • Saved"), Typography.Title, key: "title");
            actions.Button("document.save", key: "save");
            actions.Button("document.duplicate", key: "duplicate");
        }
        Ui.TextField("destination", "Explicit save destination", Model.Destination, static path => new DocumentMessage.Destination(path),
            new(Width: C.Size.Width - Space.S5 * 2, Placeholder: "/explicit/path/note.kumiko"));
        Ui.RichTextEditor("editor", "Document text", Model.Editors[id], command => new DocumentMessage.Edit(id, command),
            new(Width: C.Size.Width - Space.S5 * 2, Height: C.Size.Height - 230));
        C.Text(Model.Error ?? (document.Pending is null ? "Private storage: choose the destination explicitly." : "Saving captured revision…"),
            Typography.Caption with
            {
                Overflow = TextOverflow.Ellipsis
            }, key: "status");
        if (Model.Workspace.Close?.PendingId == id)
        {
            using var prompt = C.Div(key: "close").Row().Gap(Space.S2);
            C.Text("Keep your changes?", key: "question");
            Ui.Button("Keep editing", new DocumentMessage.Decision(id, CloseDecision.Cancel), key: "cancel");
            Ui.Button("Discard", new DocumentMessage.Decision(id, CloseDecision.Discard), new(Disabled: document.Pending is not null), key: "discard");
            Ui.Button("Saved; close", new DocumentMessage.Decision(id, CloseDecision.Saved), new(Disabled: !document.CanClose), key: "close-saved");
        }
        actions.Shortcuts();
    }
}
