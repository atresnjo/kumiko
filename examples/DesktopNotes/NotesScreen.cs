using Kumiko.Runtime;
using Kumiko.UI;

namespace DesktopNotes;

sealed class NotesScreen(NotesModel model, NoteWindow window) : WindowScreen<NotesModel, NoteMessage, NoteWindow>(model, window)
{
    protected override void Render()
    {
        switch (Window)
        {
            case NoteWindow.Editor editor:
                Editor(editor.Id);
                break;
        }
    }

    void Editor(int id)
    {
        var note = Model.Notes[id];
        using var page = C.Div(key: "note").SizeFull().Padding(Space.S5).Gap(Space.S3).Background(C.Theme.Background);
        C.Text($"Note {id}" + (note.Dirty ? " • Unsaved" : " • Saved"), Typography.Heading, key: "title");
        C.Text("Each window owns a different note. Saves stay in memory.", Typography.Caption, key: "intro");
        var actions = Ui.DesktopActions(NotesApp.Actions(id, note));
        using (C.Div(key: "toolbar").Row().Gap(Space.S2))
        {
            actions.Button("save", "save");
            actions.Button("pin", "pin");
            Ui.Button("Commands", new NoteMessage.OpenPopup(id, "palette", C.Focus), key: "commands");
            Ui.Button("Note menu", new NoteMessage.OpenPopup(id, "menu", C.Focus), key: "menu");
        }
        Ui.TextField("text", "Note text", note.Text, text => new NoteMessage.Edit(id, text),
            new(Width: 500, Multiline: true, Height: 160));
        using (var contextTarget = C.Div(key: "context-target").FillWidth().Height(30))
        {
            C.Text("Right-click here for note actions", Typography.Caption, key: "hint");
            var hit = contextTarget.Semantics(Role.Button, "Note actions").Control();
            Ui.OnContextMenu(hit, at => new NoteMessage.OpenPopup(id, "menu", hit.Id, at));
        }
        C.Text($"{note.Status} • Saves: {note.Saves}", Typography.Caption, key: "status");
        if (note.RestoreFocus)
        {
            C.FocusControl(note.ReturnFocus);
            Ui.Dispatch(new NoteMessage.FocusRestored(id));
        }
        var dispatchId = id;
        if (note.Popup == "palette")
            actions.Palette(note.Query, text => new NoteMessage.Query(dispatchId, text), new NoteMessage.ClosePopup(id), key: "palette");
        else if (note.Popup == "menu")
            actions.Menu(new NoteMessage.ClosePopup(id), note.Anchor is { } at ? new Rect(at.X, at.Y, 0, 0) : null, key: "note-menu");
        if (note.Popup is null)
            actions.Shortcuts();
    }
}
