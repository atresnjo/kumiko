using System.Collections.Immutable;
using Kumiko.Runtime;
using Kumiko.UI;

namespace DesktopNotes;

public sealed record Note(string Text = "", string SavedText = "", bool Saving = false, bool Pinned = false,
    int Saves = 0, string Status = "In memory", string? Popup = null, string Query = "", ulong ReturnFocus = 0,
    bool RestoreFocus = false, Point? Anchor = null)
{
    public bool Dirty => Text != SavedText;
}
public sealed record NotesModel(ImmutableSortedDictionary<int, Note> Notes);
public abstract partial record NoteWindow
{
    public sealed record Editor(int Id) : NoteWindow;
}
public abstract partial record NoteMessage
{
    public sealed record Edit(int Id, string Text) : NoteMessage;
    public sealed record Save(int Id) : NoteMessage;
    public sealed record Saved(int Id, string Text) : NoteMessage;
    public sealed record Failed(int Id, string Error) : NoteMessage;
    public sealed record Pin(int Id) : NoteMessage;
    public sealed record Close(int Id) : NoteMessage;
    public sealed record OpenPopup(int Id, string Kind, ulong Focus, Point? At = null) : NoteMessage;
    public sealed record ClosePopup(int Id) : NoteMessage;
    public sealed record Query(int Id, string Text) : NoteMessage;
    public sealed record FocusRestored(int Id) : NoteMessage;
}
public interface INoteStore
{
    Task Save(int id, string text, CancellationToken token);
}
public sealed class MemoryNoteStore : INoteStore
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> notes = new();
    public Task Save(int id, string text, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        notes[id] = text;
        return Task.CompletedTask;
    }
    public string? Read(int id) => notes.GetValueOrDefault(id);
}
