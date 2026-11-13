using DesktopNotes;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Runtime;

/// <summary>Typed actions placed into an application-authored standard menu template.</summary>
static partial class NativeMenuFlows
{
    public static void RunTemplate(string output, Action<bool, string> check)
    {
        using var app = new App(new("Template actions hidden", Hidden: true, QuitOnLastWindow: false));
        using var program = NotesApp.CreateProgram(new KumikoServices().Add<INoteStore>(new MemoryNoteStore()));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        var rejectedKey = false;
        try
        {
            app.SetMenu(new MenuItem("Pin", "Primary+P", Id: "pin", Role: MenuRole.DesktopAction));
        }
        catch (ArgumentException)
        {
            rejectedKey = true;
        }
        var rejectedClick = false;
        try
        {
            app.SetMenu(new MenuItem("Pin", Click: _ => { }, Id: "pin", Role: MenuRole.DesktopAction));
        }
        catch (ArgumentException)
        {
            rejectedClick = true;
        }
        check(rejectedKey && rejectedClick, "action placeholders reject their own key equivalents and callbacks");
        app.SetMenu(new MenuItem(Role: MenuRole.AppMenu),
            new MenuItem("Note", Submenu: [new("Save note", Id: "save", Role: MenuRole.DesktopAction),
                new("Pin note", Id: "pin", Role: MenuRole.DesktopAction), new("Missing", Id: "missing", Role: MenuRole.DesktopAction)]),
            new MenuItem(Role: MenuRole.EditMenu), new MenuItem(Role: MenuRole.WindowMenu));
        var template = app.Menu!;
        app.NewWindows(program, new(Visible: false));
        app.RunFor(TimeSpan.FromMilliseconds(80));
        var pin = template.Find("pin")!;
        check(app.Menu == template && template.Find("missing")!.Disabled && pin.Disabled,
            "the template survives frames; missing and two-window ambiguous actions are disabled");
        program.Send(new NoteMessage.Close(2));
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 1 && !pin.Disabled && pin.Label.StartsWith("Pin note", StringComparison.Ordinal) && pin.Accelerator.Length == 0,
            "a single projecting window binds the placeholder label and state without a native accelerator");
        var nativeMenu = OperatingSystem.IsMacOS() ? Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu") : 0;
        var before = program.History.Count(step => step.Message is NoteMessage.Pin);
        if (OperatingSystem.IsMacOS())
        {
            var notes = Get(Item(nativeMenu, 1), "submenu");
            SendAction(notes, Selector("performActionForItemAtIndex:"), 1);
            app.RunFor(TimeSpan.FromMilliseconds(60));
            check(program.Model.Notes[1].Pinned && program.History.Count(step => step.Message is NoteMessage.Pin) == before + 1 &&
                pin.Checked, "AppKit activation of a template placeholder dispatches the current projection once");
            check(Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu") == nativeMenu,
                "state changes update installed native items in place instead of rebuilding the menu");
        }
        program.Send(new NoteMessage.OpenPopup(1, "palette", 0));
        app.RunFor(TimeSpan.FromMilliseconds(60));
        var modal = program.History.Count(step => step.Message is NoteMessage.Pin);
        if (OperatingSystem.IsMacOS())
        {
            var notes = Get(Item(nativeMenu, 1), "submenu");
            SendAction(notes, Selector("performActionForItemAtIndex:"), 1);
            app.RunFor(TimeSpan.FromMilliseconds(60));
        }
        check(pin.Disabled && program.History.Count(step => step.Message is NoteMessage.Pin) == modal,
            "an open palette disables the placeholder and blocks its native activation");
        program.Send(new NoteMessage.ClosePopup(1));
        program.Send(new NoteMessage.Close(1));
        app.RunFor(TimeSpan.FromMilliseconds(80));
        check(app.Windows.Count == 0 && app.Menu == template && pin.Disabled && template.Find("save")!.Disabled,
            "closing the last projecting window disables placeholders and keeps the authored template");
        app.SetMenu();
        check(app.Menu != template, "replacing the template disposes it");
        File.WriteAllLines(Path.Combine(output, "template-errors.txt"), errors.Select(e => e.ToString()));
        check(errors.Count == 0, "template menu lifecycle has no unhandled callback errors");
    }
}
