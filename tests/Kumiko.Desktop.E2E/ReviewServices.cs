using System.Diagnostics;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.UI;

static class ReviewServices
{
    internal static void Run(string output, Action<bool, string> check)
    {
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, "review-services.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        json.WriteString("scope", "private native menu/status button/notification center; isolated filesystem; no shell publication");
        json.WriteStartArray("checks");
        void Verify(bool condition, string name)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteBoolean("passed", condition);
            json.WriteEndObject();
            json.Flush();
            check(condition, "Review services: " + name);
        }

        using var app = new App(new("Review services", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        Menus(app, Verify);
        if (OperatingSystem.IsMacOS())
            Tier1Checks.ReviewNativeServices(app, Verify);
        Watcher(app, output, Verify);
        Verify(errors.Count == 0, "native callbacks report no escaped errors");
        json.WriteEndArray();
        json.WriteString("status", "passed");
        json.WriteEndObject();
    }

    static void Menus(App app, Action<bool, string> check)
    {
        var custom = new MenuItem("Caller command", Id: "caller");
        var edit = new MenuItem(Id: "edit", Role: MenuRole.EditMenu, Submenu: [custom]);
        var standard = new MenuItem(Id: "standard", Role: MenuRole.EditMenu);
        using var menu = new Menu(app, edit, standard);
        check(ReferenceEquals(menu.Find("edit"), edit) && ReferenceEquals(menu.Find("caller"), custom),
            "standard roles preserve caller item and submenu identities on initial expansion");
        var retained = menu.Find("standard")!;
        var retainedChild = retained.Submenu![0];
        retainedChild.SetLabel("Retained undo");
        menu.Append(new("Appended", Id: "appended"));
        menu.Insert(0, new("Inserted"));
        retained.SetLabel("Retained edit");
        custom.SetLabel("Updated caller command");
        check(ReferenceEquals(menu.Find("standard"), retained) && ReferenceEquals(retained.Submenu![0], retainedChild) &&
            ReferenceEquals(menu.Find("caller"), custom), "structural mutations preserve retained standard roles and children");
        if (OperatingSystem.IsMacOS())
        {
            using var pool = new AutoreleasePool();
            var authoredRecent = new MenuItem(Role: MenuRole.OpenRecent, Submenu: [new("Pinned", Id: "pinned")]);
            using var recent = new Menu(app, authoredRecent);
            var pinned = recent.Find("pinned");
            app.AddRecentDocument(Path.Combine(Path.GetTempPath(), "private-menu-document.txt"));
            check(pinned is not null && ReferenceEquals(recent.Find("pinned"), pinned), "recent-document updates preserve caller-authored submenus");
            var nativeEdit = ObjC.O(menu.Native, "itemAtIndex:", (nuint)2);
            var nativeChild = ObjC.O(ObjC.O(nativeEdit, "submenu"), "itemAtIndex:", (nuint)0);
            check(ObjC.Text(ObjC.O(nativeEdit, "title")) == "Retained edit" &&
                ObjC.Text(ObjC.O(nativeChild, "title")) == "Retained undo",
                "retained setters update the rebuilt native menu");
            Menu? mutating = null;
            int actions = 0;
            bool intactDuringAction = false;
            using (mutating = new Menu(app, new MenuItem("Mutate", Click: _ =>
            {
                actions++;
                var original = mutating!.Native;
                mutating.Append(new("After action"));
                intactDuringAction = mutating.Native == original && ObjC.N(original, "numberOfItems") == 1;
            })))
            {
                ObjC.V(mutating.Native, "performActionForItemAtIndex:", (nuint)0);
                app.RunFor(TimeSpan.FromMilliseconds(40));
                check(actions == 1 && intactDuringAction && ObjC.N(mutating.Native, "numberOfItems") == 2,
                    "native menu action defers destructive rebuild until its sender unwinds");
            }
            Menu? disposing = null;
            bool disposedInAction = false;
            using (disposing = new Menu(app, new MenuItem("Dispose", Click: _ =>
            {
                disposing!.Dispose();
                disposedInAction = true;
            })))
                ObjC.V(disposing.Native, "performActionForItemAtIndex:", (nuint)0);
            app.RunFor(TimeSpan.FromMilliseconds(25));
            check(disposedInAction, "native menu action can dispose its own owner before returning to AppKit");
        }

        int invoked = 0;
        using var shortcuts = new Menu(app, new MenuItem("Primary", "Primary+K", _ => invoked++));
        var input = new Input { Key = Key.K };
        check(!shortcuts.HandleInput(null!, input) && invoked == 0, "Primary accelerator never matches an unmodified letter");
        input.Modifiers = Modifiers.Primary;
        check(shortcuts.HandleInput(null!, input) && invoked == 1, "Primary accelerator dispatches exactly once with its modifier");
        input.Modifiers |= Modifiers.Alt;
        check(!shortcuts.HandleInput(null!, input) && invoked == 1, "extra accelerator modifiers do not match a shorter chord");
    }

    static void Pump(App app, Func<bool> ready, string name)
    {
        var clock = Stopwatch.StartNew();
        while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(8))
            app.RunFor(TimeSpan.FromMilliseconds(25));
        if (!ready())
            throw new TimeoutException(name);
    }

    static void Watcher(App app, string output, Action<bool, string> check)
    {
        string root = Path.Combine(output, "review-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "nested", "node_modules", "package"));
        Directory.CreateDirectory(Path.Combine(root, ".git", "objects"));
        var changes = new List<FileChange>();
        var transcript = new List<FileChange>();
        int thread = Environment.CurrentManagedThreadId;
        bool mainThread = true;
        using var watcher = new FileWatcher(app, root, new(Recursive: true, Debounce: TimeSpan.FromMilliseconds(400), Ignore: ["*.ignore", "node_modules", ".git"]));
        watcher.Changed += batch =>
        {
            mainThread &= Environment.CurrentManagedThreadId == thread;
            changes.AddRange(batch.Changes);
            transcript.AddRange(batch.Changes);
        };
        string first = Path.Combine(root, "first.txt"), ignored = Path.Combine(root, "hidden.ignore"), restored = Path.Combine(root, "restored.txt");
        File.WriteAllText(first, "watch fixture");
        Pump(app, () => changes.Any(c => c.Path == first), "initial watcher create");
        changes.Clear();
        File.Move(first, ignored);
        Pump(app, () => changes.Any(c => c.Path == first && c.Kind == FileChangeKind.Deleted), "rename into ignored set");
        check(changes.All(c => c.Path != ignored), "included-to-ignored rename reports deletion of the included endpoint");
        changes.Clear();
        File.Move(ignored, restored);
        Pump(app, () => changes.Any(c => c.Path == restored && c.Kind == FileChangeKind.Created), "rename out of ignored set");
        check(changes.All(c => c.OldPath != ignored), "ignored-to-included rename reports creation without exposing ignored endpoint");
        changes.Clear();
        File.Move(restored, first);
        Pump(app, () => changes.Any(c => c.Kind == FileChangeKind.Renamed && c.Path == first && c.OldPath == restored), "included rename");
        check(changes.Any(c => c.Kind == FileChangeKind.Renamed && c.Path == first && c.OldPath == restored),
            "included-to-included rename preserves both endpoints");
        changes.Clear();
        File.WriteAllText(Path.Combine(root, "nested", "node_modules", "package", "ignored.js"), "ignored");
        File.WriteAllText(Path.Combine(root, ".git", "objects", "ignored"), "ignored");
        File.WriteAllText(Path.Combine(root, "barrier.txt"), "barrier");
        Pump(app, () => changes.Any(c => c.Path.EndsWith("barrier.txt", StringComparison.Ordinal)), "subtree ignore barrier");
        check(changes.All(c => !c.Path.Contains("node_modules", StringComparison.Ordinal) && !c.Path.Contains(".git", StringComparison.Ordinal)),
            "ignored directory names exclude descendants at every depth");
        changes.Clear();
        var clock = Stopwatch.StartNew();
        long firstBatch = -1;
        watcher.Changed += _ =>
        {
            if (firstBatch < 0)
                firstBatch = clock.ElapsedMilliseconds;
        };
        var writer = Task.Run(async () =>
        {
            for (int i = 0; i < 150; i++)
            {
                File.WriteAllText(Path.Combine(root, "traffic.txt"), i.ToString());
                await Task.Delay(20);
            }
            File.WriteAllText(Path.Combine(root, "final.txt"), "last event");
        });
        Pump(app, () => writer.IsCompleted && changes.Any(c => c.Path.EndsWith("final.txt", StringComparison.Ordinal)), "sustained watcher traffic");
        writer.GetAwaiter().GetResult();
        check(firstBatch is >= 0 and < 2000 && mainThread, "sustained traffic has bounded delivery on the App thread and preserves its final batch");
        int beforeDispose = changes.Count;
        File.WriteAllText(Path.Combine(root, "pending.txt"), "pending");
        watcher.Dispose();
        app.RunFor(TimeSpan.FromMilliseconds(450));
        check(changes.Count == beforeDispose, "watcher disposal suppresses pending deliveries");
        using var stream = File.Create(Path.Combine(output, "review-watch-transcript.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteNumber("sustainedFirstBatchMs", firstBatch);
        json.WriteStartArray("changes");
        foreach (var change in transcript)
        {
            json.WriteStartObject();
            json.WriteString("kind", change.Kind.ToString());
            json.WriteString("path", Path.GetRelativePath(root, change.Path));
            if (change.OldPath is { } old)
                json.WriteString("oldPath", Path.GetRelativePath(root, old));
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
        Directory.Delete(root, recursive: true);
    }
}
