using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopNotes;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Runtime;

static partial class NativeMenuFlows
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    public static void Run(App app, Program<NotesModel, NoteMessage> program, string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        var main = Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu");
        var evidence = new List<string>();
        void Inspect(nint menu)
        {
            for (nint index = 0; index < Get(menu, "numberOfItems"); index++)
            {
                var item = Item(menu, index);
                var title = String(Get(item, "title"));
                var key = String(Get(item, "keyEquivalent"));
                var mask = Get(item, "keyEquivalentModifierMask");
                evidence.Add($"{title}: key={key}, modifiers={mask}");
                check(key.Length == 0 && mask == 0, "installed native action has no key equivalent: " + title);
                var child = Get(item, "submenu");
                if (child != 0)
                    Inspect(child);
            }
        }
        Inspect(main);
        var documents = Get(Item(main, 0), "submenu");
        var note = Get(Item(documents, 0), "submenu");
        var before = program.History.Count(step => step.Message is NoteMessage.Pin);
        SendAction(note, Selector("performActionForItemAtIndex:"), 1);
        app.RunFor(TimeSpan.FromMilliseconds(60));
        check(program.Model.Notes[1].Pinned && !program.Model.Notes[2].Pinned &&
            program.History.Count(step => step.Message is NoteMessage.Pin) == before + 1,
            "AppKit native menu activation dispatches exactly once to its named document");
        check(app.Windows.All(window => !window.IsVisible), "AppKit inspection and activation keep native windows hidden");
        // Reset via the current managed projection so NativeFlows retains its original initial state.
        var pin = app.Menu!.Find("pin")!;
        pin.Click!(pin);
        app.RunFor(TimeSpan.FromMilliseconds(40));
        app.SetMenu(new MenuItem("Explicit plus", Accelerator: "+"),
            new MenuItem("Primary plus", Accelerator: "CmdOrCtrl++"));
        main = Get(Get(Class("NSApplication"), "sharedApplication"), "mainMenu");
        check(String(Get(Item(main, 0), "keyEquivalent")) == "+" &&
            String(Get(Item(main, 1), "keyEquivalent")) == "+" &&
            Get(Item(main, 1), "keyEquivalentModifierMask") == 1 << 20,
            "explicit plus and Primary-plus native accelerators remain intact");
        // Rebuild through the actual window host, replacing the temporary legacy menu.
        foreach (var window in app.Windows)
            window.Invalidate();
        app.RunFor(TimeSpan.FromMilliseconds(40));
        File.WriteAllText(Path.Combine(output, "native-menu.json"), JsonSerializer.Serialize(evidence, NativeMenuJson.Default.ListString));
    }

    static nint Get(nint receiver, string name) => Send(receiver, Selector(name));
    static nint Item(nint menu, nint index) => SendIndex(menu, Selector("itemAtIndex:"), index);
    static string String(nint value) => Marshal.PtrToStringUTF8(Get(value, "UTF8String")) ?? "";
    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Class(string name);
    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint SendIndex(nint receiver, nint selector, nint index);
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void SendAction(nint receiver, nint selector, nint index);
}

[JsonSerializable(typeof(List<string>))]
internal partial class NativeMenuJson : JsonSerializerContext;
