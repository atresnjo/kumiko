using System.Collections.Immutable;
using Kumiko.Desktop;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DesktopNotes;

public sealed partial class NotesApp : IKumiko<NotesModel, NoteMessage>, IKumikoWindows<NotesModel, NoteMessage, NoteWindow>
{
    public static NotesModel Init => new(ImmutableSortedDictionary<int, Note>.Empty.Add(1, new()).Add(2, new()));
    public static NavigationRoot<NoteMessage> WindowRoot(NotesModel model, NoteWindow window) => NavigationRoot<NoteMessage>.Create(
        NavigationHistory<NoteWindow>.Start(window), "note", route => new NotesScreen(model, route).Map<NoteMessage>(static m => m),
        static _ => throw new InvalidOperationException("One screen per note."));

    public static IEnumerable<DesktopAction<NoteMessage>> Actions(int id, Note note)
    {
        yield return new("save", "Save note", new NoteMessage.Save(id), Key.S, Enabled: note.Dirty && !note.Saving);
        yield return new("pin", "Pin note", new NoteMessage.Pin(id), Key.P, Modifiers.Primary | Modifiers.Shift, Checked: note.Pinned);
        yield return new("close", "Close note", new NoteMessage.Close(id), Enabled: !note.Dirty && !note.Saving);
    }

    public static IEnumerable<WindowSpec<NoteWindow, NoteMessage>> Windows(NotesModel model)
    {
        foreach (var id in model.Notes.Keys)
            yield return new(new NoteWindow.Editor(id), $"Note {id}", new NoteMessage.Close(id))
            {
                Width = 560,
                Height = 380
            };
    }

    public static (NotesModel, Cmd<NoteMessage>) Update(NotesModel model, NoteMessage message)
    {
        int id = message switch
        {
            NoteMessage.Edit m => m.Id,
            NoteMessage.Save m => m.Id,
            NoteMessage.Saved m => m.Id,
            NoteMessage.Failed m => m.Id,
            NoteMessage.Pin m => m.Id,
            NoteMessage.Close m => m.Id,
            NoteMessage.OpenPopup m => m.Id,
            NoteMessage.ClosePopup m => m.Id,
            NoteMessage.Query m => m.Id,
            NoteMessage.FocusRestored m => m.Id,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        if (!model.Notes.TryGetValue(id, out var note))
            return (model, Cmd<NoteMessage>.None);
        var next = message switch
        {
            NoteMessage.Edit m => note with { Text = m.Text },
            NoteMessage.Save when note.Dirty && !note.Saving => note with { Saving = true, Status = "Saving…" },
            NoteMessage.Save => note,
            NoteMessage.Saved m => note with { SavedText = m.Text, Saving = false, Saves = note.Saves + 1, Status = "Saved in memory" },
            NoteMessage.Failed m => note with { Saving = false, Status = m.Error },
            NoteMessage.Pin => note with { Pinned = !note.Pinned },
            NoteMessage.Close => note,
            NoteMessage.OpenPopup m => note with { Popup = m.Kind, ReturnFocus = m.Focus, Query = "", Anchor = m.At },
            NoteMessage.ClosePopup => note with { Popup = null, RestoreFocus = true },
            NoteMessage.Query m => note with { Query = m.Text },
            NoteMessage.FocusRestored => note with { RestoreFocus = false },
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        var notes = message is NoteMessage.Close && !note.Dirty && !note.Saving ? model.Notes.Remove(id) : model.Notes.SetItem(id, next);
        var command = message is NoteMessage.Save && note.Dirty && !note.Saving
            ? Cmd<NoteMessage>.Run<INoteStore>("save-note-" + id, async (store, token) =>
            {
                await store.Save(id, note.Text, token);
                return new NoteMessage.Saved(id, note.Text);
            }, error => new NoteMessage.Failed(id, error.Message))
            : Cmd<NoteMessage>.None;
        return (model with
        {
            Notes = notes
        }, command);
    }
}
