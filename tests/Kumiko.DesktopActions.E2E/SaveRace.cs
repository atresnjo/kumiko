using DesktopNotes;
using Kumiko.Runtime;
using Kumiko.UI;

static class SaveRace
{
    public static void Run(string output, Action<bool, string> check)
    {
        var store = new HeldStore();
        using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(store));
        using var tester = new Tester(program, new(Width: 560, Height: 380));
        var window = tester.In(new NoteWindow.Editor(1));
        window.Get(Role.TextField, "Note text").Fill("old text");
        window.Get(Role.Button, "Save note").Click();
        tester.Settle();
        check(program.Model.Notes[1].Saving && !window.Get(Role.Button, "Save note").Enabled,
            "in-flight save disables toolbar and menu");
        var saving = window.DesktopMenu.Single(a => a.Id == "save");
        saving.Click!(saving);
        window.Get(Role.TextField, "Note text").Fill("new text");
        store.Release.TrySetResult();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (program.Model.Notes[1].Saving && DateTime.UtcNow < deadline)
        {
            tester.Settle();
            Thread.Sleep(1);
        }
        check(program.Model.Notes[1].Dirty && program.Model.Notes[1].SavedText == "old text" &&
            program.Model.Notes[1].Text == "new text" && program.Model.Notes[1].Saves == 1 && store.Writes == 1,
            "save completion preserves newer edits and duplicate in-flight invocation produces no second write");
        window.Context.Blur();
        window.Frame(0);
        window.Screenshot(Path.Combine(output, "save-race.png"));
        File.WriteAllText(Path.Combine(output, "save-race.txt"), window.Dump());
        program.Save(Path.Combine(output, "save-race-session.jsonl"));
    }
    sealed class HeldStore : INoteStore
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes;
        public int Writes => Volatile.Read(ref writes);
        public async Task Save(int id, string text, CancellationToken token)
        {
            Interlocked.Increment(ref writes);
            await Release.Task.WaitAsync(token);
        }
    }
}
