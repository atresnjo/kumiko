using DesktopNotes;
using Kumiko.Runtime;
using Kumiko.UI;

static class ReviewFlows
{
    public static void Run(string output, Action<bool, string> check)
    {
        foreach (var awayAndBack in new[] { false, true })
        {
            using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new MemoryNoteStore()));
            using var tester = new Tester(program, new(Width: 560, Height: 380));
            program.Send(new NoteMessage.Edit(1, "history fixture"));
            tester.Settle();
            var current = program.Position;
            var pin = tester.DesktopMenu.Single(item => item.Id == "pin");
            var frame = tester.Context.FrameId;
            if (awayAndBack)
                program.Step(0);
            program.Step(current);
            var position = program.Position;
            pin.Click!(pin);
            program.ProcessMessages();
            check(tester.Context.FrameId == frame && program.Position == position && !program.Model.Notes[1].Pinned,
                awayAndBack ? "history away/back cannot revive a retained action before rendering" :
                    "same-position history reset retires retained actions before rendering");
            tester.Settle();
            var fresh = tester.DesktopMenu.Single(item => item.Id == "pin");
            fresh.Click!(fresh);
            program.ProcessMessages();
            check(program.Model.Notes[1].Pinned, "fresh actions remain usable after history reset");
        }
        using var palette = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new MemoryNoteStore()));
        using var window = new Tester(palette, new(Width: 560, Height: 380));
        window.Context.ReduceMotion = true;
        window.Get(Role.TextField, "Note text").Perform(new(AccessibilityAction.Focus));
        var editor = window.Context.Focus;
        palette.Send(new NoteMessage.OpenPopup(1, "palette", editor));
        window.Settle();
        window.Get(Role.TextField, "Type to search…").Fill("Pin");
        var search = window.Context.Focus;
        var actionsBefore = palette.History.Count(step => step.Message is NoteMessage.Pin);
        window.Compose("未", false);
        window.Key(Key.Escape);
        window.Settle();
        check(palette.Model.Notes[1].Popup == "palette" && window.Context.Focus == search,
            "first IME Escape cancels composition and preserves palette search focus");
        window.Screenshot(Path.Combine(output, "ime-escape.png"));
        File.WriteAllText(Path.Combine(output, "ime-escape.txt"), window.Dump());
        window.Key(Key.Escape);
        window.Settle();
        check(palette.Model.Notes[1].Popup is null && window.Context.Focus == editor &&
            palette.History.Count(step => step.Message is NoteMessage.Pin) == actionsBefore,
            "second Escape closes the palette and restores editor focus without dispatching an action");
        palette.Save(Path.Combine(output, "ime-escape-session.jsonl"));
    }
}
