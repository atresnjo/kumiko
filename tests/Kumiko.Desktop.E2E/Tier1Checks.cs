using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.Rendering;
using Kumiko.UI;

static partial class Tier1Checks
{
    internal static bool Child(string[] arguments)
    {
        if (arguments.FirstOrDefault() != "--tier1-instance")
            return false;
        using var app = new App(new("Tier 1 child", QuitOnLastWindow: false, Hidden: true));
        bool first = app.RequestSingleInstanceLock(arguments[1]);
        Console.WriteLine(first ? "FIRST" : "SECOND");
        return true;
    }

    internal static void Run(string output, Action<bool, string> check)
    {
        var evidence = new List<string>();
        void Verify(bool condition, string name)
        {
            check(condition, "Tier 1: " + name);
            evidence.Add(name);
        }

        using var app = new App(new("Tier 1 isolated shell", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        var directory = Path.Combine(output, "tier1-files");
        Directory.CreateDirectory(directory);
        app.WindowStates = new(Path.Combine(directory, "states"));
        ClipboardFlows(app, Verify);
        WindowFlows(app, output, Verify);
        LaunchFlows(app, directory, output, Verify);
        WatchFlows(app, directory, Verify);
        FixtureFlows(app, Verify);
        if (OperatingSystem.IsMacOS())
        {
            MacDeliveryFlows(app, directory, Verify);
            MacLifetimeFlows(app, output, Verify);
            MacNotificationFlows(app, Verify);
            MacTrayFlows(app, Verify);
            MacShortcutFlows(app, Verify);
        }

        Verify(errors.Count == 0, "native callbacks contain exceptions and the hidden flows report no errors");
        using var stream = File.Create(Path.Combine(output, "tier1.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("status", "passed");
        json.WriteString("platform", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
        json.WriteString("nativeExecution", "hidden window geometry/close, native file watcher, current-user pipe forwarding" + (OperatingSystem.IsMacOS() ? ", named AppKit pasteboard, delegate selectors, NSMenu, transparent NSVisualEffectView capture" : ""));
        json.WriteString("fixtureOnly", "notification center, offscreen status item, Carbon ABI; production adapter callback/lifetime paths execute against private fixtures");
        json.WriteString("notExecuted", "visible chrome/state transitions, real notification/tray/shortcut registration, foreign application drag, Windows/Linux native paths on macOS");
        json.WriteStartArray("checks");
        foreach (var item in evidence)
            json.WriteStringValue(item);
        json.WriteEndArray();
        json.WriteEndObject();
    }

    static void Pump(App app, Func<bool> complete, string operation)
    {
        var timer = Stopwatch.StartNew();
        while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(8))
            app.RunFor(TimeSpan.FromMilliseconds(25));
        if (!complete())
            throw new TimeoutException(operation);
    }

    static void ClipboardFlows(App app, Action<bool, string> check)
    {
        int fetches = 0, releases = 0;
        const string custom = "application/vnd.kumiko.fixture+json";
        var bytes = Encoding.UTF8.GetBytes("frozen");
        var data = new TransferData(new TransferItem(new TransferRepresentation(TransferFormats.Text, bytes), new TransferRepresentation(TransferFormats.HTML, Encoding.UTF8.GetBytes("<b>frozen</b>")), new TransferRepresentation(custom, () =>
        {
            fetches++;
            return Encoding.UTF8.GetBytes("{\"value\":42}");
        })));
        bytes[0] = 0;
        Clipboard.Write(data, () => releases++);
        check(Clipboard.Formats.Count == 3 && fetches == 0, "format discovery never renders a lazy representation");
        check(Clipboard.ReadText() == "frozen", "eager bytes are frozen before native ownership");
        var first = Clipboard.ReadFormat(custom);
        first[0] = 0;
        check(Encoding.UTF8.GetString(Clipboard.ReadFormat(custom)).StartsWith('{') && fetches == 1, "read bytes are independent and providers run once per snapshot");
        using (var independentDrag = new TransferSnapshot(app, data))
            independentDrag.Read(custom);
        check(fetches == 2, "drag snapshots do not share clipboard provider caches");
        Clipboard.Clear();
        check(releases == 1 && Clipboard.Formats.Count == 0, "clearing releases the owner exactly once");
        Clipboard.WriteText("first\nsecond\n");
        check(Clipboard.ReadText() == "first\nsecond\n", "text representations preserve authored trailing newlines");
        string file = Path.Combine(Path.GetTempPath(), "kumiko fixture #1.txt");
        Clipboard.WriteFiles(file);
        check(Clipboard.ReadFiles().SequenceEqual(new[] { file }) && !File.Exists(file), "file offers round trip escaped absolute paths without creating files");
        Clipboard.Write(new(new TransferItem(new TransferRepresentation(TransferFormats.URIList, Encoding.UTF8.GetBytes("file://remote-host/share/private\r\nhttps://example.com\r\n")))));
        check(Clipboard.ReadFiles().Count == 0, "remote file authorities and web URLs never become local paths");
        Clipboard.Write(new(new TransferItem(new TransferRepresentation(custom, () => Clipboard.ReadFormat(custom)))));
        bool recursion = false;
        try
        {
            Clipboard.ReadFormat(custom);
        }
        catch (InvalidOperationException)
        {
            recursion = true;
        }

        check(recursion, "reentrant clipboard providers fail explicitly");
        Clipboard.Clear();
        if (OperatingSystem.IsMacOS())
        {
            var backend = new MacClipboardBackend(app, isolated: true);
            using var scope = Clipboard.Attach(app, backend);
            int nativeFetches = 0;
            Clipboard.Write(new(new TransferItem(new TransferRepresentation(TransferFormats.Text, () =>
            {
                nativeFetches++;
                return Encoding.UTF8.GetBytes("native provider 日本語");
            }))));
            check(Clipboard.Formats.Contains(TransferFormats.Text) && nativeFetches == 0, "named NSPasteboard discovers promised types without fetching");
            using var read = new TransferSnapshot(app, backend.ReadForeign([TransferFormats.Text]));
            check(Encoding.UTF8.GetString(read.Read(TransferFormats.Text)) == "native provider 日本語" && nativeFetches == 1, "AppKit calls the retained pasteboard provider through its native callback");
            Clipboard.Flush();
            Clipboard.Clear();
            Clipboard.WriteFiles(file);
            var nativeFile = ObjC.O(backend.NativeItems, "objectAtIndex:", (nuint)0);
            string nativeUrl = ObjC.Text(ObjC.O(nativeFile, "stringForType:", ObjC.TempString("public.file-url")));
            check(!nativeUrl.Contains('\r') && !nativeUrl.Contains('\n') && new Uri(nativeUrl).LocalPath == file,
                "AppKit file URL providers remove URI-list framing while preserving escaped filenames");
            Clipboard.Clear();
        }
    }

    static void WindowFlows(App app, string output, Action<bool, string> check)
    {
        Directory.CreateDirectory(app.WindowStates.Directory);
        File.Delete(Path.Combine(app.WindowStates.Directory, "tier1.json"));
        int created = 0, ready = 0, moved = 0, resized = 0;
        app.WindowCreated += _ => created++;
        using var window = app.NewWindow(new(Title: "Tier 1 hidden", Width: 480, Height: 300, Visible: false)
        {
            RememberAs = "tier1"
        }, c =>
        {
            using var root = c.Div(key: "shell").SizeFull().Padding(24).Gap(12);
            c.Text("Desktop shell", c.Theme.Typography.Title);
            c.Text("Hidden native geometry and isolated transfer formats");
        });
        window.ReadyToShow += () => ready++;
        window.Moved += () => moved++;
        window.Resized += () => resized++;
        app.RunFor(TimeSpan.FromMilliseconds(60));
        window.SetMinimumSize(new(240, 180));
        window.SetMaximumSize(new(900, 700));
        window.SetOpacity(.8f);
        window.SetPosition(new(100, 120));
        window.Resize(600, 360);
        app.RunFor(TimeSpan.FromMilliseconds(40));
        check(created == 1 && ready == 1 && !window.IsVisible, "created and first-paint events preserve hidden visibility");
        check(Math.Abs(window.ContentSize.Width - 600) < 2 && Math.Abs(window.ContentSize.Height - 360) < 2, "content resizing reports content dimensions independently of the frame");
        check(window.Bounds.Width >= window.ContentSize.Width && window.Opacity == .8f, "frame bounds and opacity are available at runtime");
        if (!OperatingSystem.IsLinux())
            check(moved > 0 && resized > 0, "native geometry changes deliver move and resize events");
        Action<CancelableEvent> cancel = e => e.PreventDefault();
        window.Closing += cancel;
        window.Close();
        check(app.Windows.Contains(window), "canceling Closing retains the window and context");
        bool invalid = false;
        try
        {
            window.SetMaximumSize(new(100, 100));
        }
        catch (ArgumentOutOfRangeException)
        {
            invalid = true;
        }

        check(invalid && window.ContentSize.Width > 200, "inconsistent size limits fail before changing native geometry");
        Png.Write(Path.Combine(output, "tier1-hidden.png"), window.Capture());
        File.WriteAllText(Path.Combine(output, "tier1-hidden.txt"), window.Context!.DumpTree());
        window.Closing -= cancel;
        window.Close();
        check(!app.Windows.Contains(window) && app.WindowStates.Load("tier1") is { Width: > 200 }, "successful close persists normal placement in the isolated store");
        var state = new WindowState(20, 30, 400, 300, false, true);
        app.WindowStates.Save("fullscreen", state);
        check(app.WindowStates.Load("fullscreen")?.FullScreen == true, "window state persists and restores the full-screen flag");
    }

    static void LaunchFlows(App app, string directory, string output, Action<bool, string> check)
    {
        app.ParseOpenArguments(["note #1.txt", "kumiko-test://open/42", "--ignored"], directory);
        var files = new List<string>();
        var urls = new List<string>();
        app.OpenFile += files.Add;
        app.OpenURL += urls.Add;
        check(files.Contains(Path.Combine(directory, "note #1.txt")) && urls.Contains("kumiko-test://open/42"), "launch files and deep links survive subscription after arrival");
        string identity = "kumiko-fixture-" + Guid.NewGuid().ToString("N");
        check(app.RequestSingleInstanceLock(identity), "first instance owns a current-user channel");
        SecondInstanceEvent? forwarded = null;
        app.SecondInstance += request => forwarded = request;
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.GetFullPath(Environment.GetCommandLineArgs()[0]));
        foreach (var argument in new[]
        {
            "--tier1-instance",
            identity,
            "forwarded.txt",
            "kumiko-test://forwarded"
        }

        )
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        try
        {
            Pump(app, () => forwarded is not null && child.HasExited, "second instance forwarding");
        }
        catch
        {
            if (!child.HasExited)
                child.Kill();
            File.WriteAllText(Path.Combine(output, "tier1-instance-failure.txt"), child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd() + string.Join("\n", start.ArgumentList));
            throw;
        }

        var transcript = child.StandardOutput.ReadToEnd() + child.StandardError.ReadToEnd();
        File.WriteAllText(Path.Combine(output, "tier1-instance.txt"), transcript);
        check(child.ExitCode == 0 && transcript.Contains("SECOND", StringComparison.Ordinal) && forwarded?.WorkingDirectory == directory, "second process forwards argv/cwd and reports lock contention");
        check(files.Contains(Path.Combine(directory, "forwarded.txt")) && urls.Contains("kumiko-test://forwarded"), "forwarded launch operands use the sender's working directory");
        app.ReleaseSingleInstanceLock();
    }

    static void WatchFlows(App app, string directory, Action<bool, string> check)
    {
        var root = Path.Combine(directory, "watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var batches = new List<FileWatchBatch>();
        int thread = Environment.CurrentManagedThreadId;
        using var watcher = new FileWatcher(app, root, new(Recursive: true, Debounce: TimeSpan.FromMilliseconds(40), Ignore: ["*.ignore"], EntryLimit: 16));
        watcher.Changed += batch =>
        {
            check(Environment.CurrentManagedThreadId == thread, "native file changes arrive on the App thread");
            batches.Add(batch);
        };
        File.WriteAllText(Path.Combine(root, "created.txt"), "first");
        File.WriteAllText(Path.Combine(root, "hidden.ignore"), "ignored");
        Pump(app, () => batches.Any(b => b.Changes.Any(c => c.Path.EndsWith("created.txt", StringComparison.Ordinal))), "file watcher create");
        File.Move(Path.Combine(root, "created.txt"), Path.Combine(root, "renamed.txt"));
        Pump(app, () => batches.Any(b => b.Changes.Any(c => c.Kind == FileChangeKind.Renamed)), "file watcher rename");
        check(batches.SelectMany(b => b.Changes).Any(c => c.OldPath?.EndsWith("created.txt", StringComparison.Ordinal) == true), "renames retain their old absolute path");
        check(!batches.SelectMany(b => b.Changes).Any(c => c.Path.EndsWith(".ignore", StringComparison.Ordinal)), "ignore patterns filter native batches");
        using var overflow = new FileWatcher(app, root, new(Debounce: TimeSpan.FromMilliseconds(100), EntryLimit: 1));
        bool didOverflow = false;
        overflow.Changed += b => didOverflow |= b.Overflow;
        for (int i = 0; i < 12; i++)
            File.WriteAllText(Path.Combine(root, "burst-" + i + ".txt"), "burst");
        Pump(app, () => didOverflow, "bounded watcher overflow");
        check(didOverflow, "bounded queues explicitly report overflow for a rescan");
    }

    static void MacDeliveryFlows(App app, string directory, Action<bool, string> check)
    {
        using var pool = new AutoreleasePool();
        var nativeApp = ObjC.O(ObjC.Class("NSApplication"), "sharedApplication");
        var target = ObjC.O(nativeApp, "delegate");
        var files = new List<string>();
        var urls = new List<string>();
        app.OpenFile += files.Add;
        app.OpenURL += urls.Add;
        string path = Path.Combine(directory, "apple-event.txt");
        var paths = ObjC.O(ObjC.Class("NSArray"), "arrayWithObject:", ObjC.TempString(path));
        ObjC.V(target, "application:openFiles:", nativeApp, paths);
        var url = ObjC.O(ObjC.Class("NSURL"), "URLWithString:", ObjC.TempString("kumiko-test://apple/event"));
        ObjC.V(target, "application:openURLs:", nativeApp, ObjC.O(ObjC.Class("NSArray"), "arrayWithObject:", url));
        check(files.Contains(path) && urls.Contains("kumiko-test://apple/event"), "AppKit delegate selectors deliver file and URL fixtures on the main thread");
        var appleEvent = ObjC.O(ObjC.O(ObjC.Class("NSAppleEventDescriptor"), "alloc"), "initWithEventClass:eventID:targetDescriptor:returnID:transactionID:",
            0x4755524cu, 0x4755524cu, (nint)0, (short)-1, 0);
        try
        {
            var descriptor = ObjC.O(ObjC.Class("NSAppleEventDescriptor"), "descriptorWithString:", ObjC.TempString("kumiko-test://geturl/path?q=1"));
            ObjC.V(appleEvent, "setParamDescriptor:forKeyword:", descriptor, (nuint)0x2d2d2d2d);
            ObjC.V(target, "handleGetURL:withReplyEvent:", appleEvent, (nint)0);
            check(urls.Contains("kumiko-test://geturl/path?q=1"), "the native GetURL Apple Event descriptor delivers its direct object without sending a system event");
        }
        finally
        {
            ObjC.Release(appleEvent);
        }
        bool explicitReason = false;
        try
        {
            using var backend = new MacNotificationBackend(app, _ =>
            {
            });
        }
        catch (PlatformNotSupportedException error)
        {
            explicitReason = error.Message.Contains("CFBundleIdentifier", StringComparison.Ordinal);
        }

        check(explicitReason, "unbundled macOS rejects notifications before accessing UNUserNotificationCenter");
    }
}
