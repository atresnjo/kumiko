using System.Runtime.InteropServices;
using Kumiko;
using Kumiko.Desktop;

/// <summary>
/// Windows recent documents, Jump List links, login provenance and the pre-2004 network fallback. Hidden apps,
/// a temporary history file and an injected shell fixture keep the user's recent items, Jump List and Run key
/// untouched; links are created in memory and never committed.
/// </summary>
static class WindowsShellChecks
{
    sealed class ShellFixture : IRecentDocumentShell
    {
        public readonly List<string> AddedPaths = new();
        public IReadOnlyList<string> Published = [];
        public int Cleared;
        public void Added(string path) => AddedPaths.Add(path);
        public void Publish(IReadOnlyList<string> documents) => Published = documents.ToArray();
        public void Clear() => Cleared++;
    }

    public static void Run(string output, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "kumiko-windows-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var history = Path.Combine(root, "app-data", "recent-documents.json");
            var expected = RecentHistory(root, history, check);
            using (var reopened = new App(new("Windows shell checks", Hidden: true)))
            {
                reopened.RecentHistory = new(new(history), new ShellFixture());
                check(reopened.RecentDocuments.SequenceEqual(expected), "Windows recent history survives an app restart in its JSON store");
            }
            StoreTolerance(root, check);
            using var app = new App(new("Windows shell checks", Hidden: true));
            RecentMenu(app, root, check);
            JumpListLinks(app, root, check);
            LoginProvenance(app, root, check);
            LegacyNetwork(app, check);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static string[] RecentHistory(string root, string history, Action<bool, string> check)
    {
        var shell = new ShellFixture();
        using var app = new App(new("Windows shell checks", Hidden: true));
        app.RecentHistory = new(new(history), shell);
        check(app.RecentDocuments.Count == 0 && !File.Exists(history), "a missing Windows history file reads as empty without creating it");
        var documents = Enumerable.Range(0, 12).Select(i => Path.Combine(root, $"document-{i}.txt")).ToArray();
        foreach (var document in documents)
            app.AddRecentDocument(document);
        var reopened = documents[5].ToUpperInvariant();
        app.AddRecentDocument(reopened);
        string[] expected = [reopened, .. Enumerable.Reverse(documents).Take(10).Where(path => path != documents[5])];
        check(app.RecentDocuments.SequenceEqual(expected) && expected.Length == 10,
            "Windows history keeps ten entries, most recent first, deduplicating paths case-insensitively");
        check(shell.Published.SequenceEqual(expected) && shell.AddedPaths.Count == 13 && shell.AddedPaths[^1] == reopened,
            "each addition records its one path with the shell and republishes the Jump List");
        check(new RecentDocumentStore(history).Load().SequenceEqual(expected) &&
            Directory.GetFiles(Path.GetDirectoryName(history)!).Length == 1,
            "history is persisted atomically without leaving temporary files");
        return expected;
    }

    static void StoreTolerance(string root, Action<bool, string> check)
    {
        var path = Path.Combine(root, "malformed", "recent-documents.json");
        var store = new RecentDocumentStore(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        check(store.Load().Count == 0, "a malformed Windows history reads as empty");
        var kept = Path.Combine(root, "kept.txt");
        File.WriteAllText(path, "{\"documents\":[\"relative.txt\",5,\"" + kept.Replace("\\", "\\\\") + "\",\"" + kept.ToUpperInvariant().Replace("\\", "\\\\") + "\"]}");
        check(store.Load().SequenceEqual([kept]), "history loading skips relative, non-string and duplicate entries");
        var shell = new ShellFixture();
        using var app = new App(new("Windows shell checks", Hidden: true));
        app.RecentHistory = new(store, shell);
        app.ClearRecentDocuments();
        check(app.RecentDocuments.Count == 0 && store.Load().Count == 0 && shell.Cleared == 1 && shell.AddedPaths.Count == 0,
            "Clear empties the app history and only this app's Jump List, never the shell's recent items");
    }

    static void RecentMenu(App app, string root, Action<bool, string> check)
    {
        using var menu = new Menu(app, new MenuItem(Role: MenuRole.FileMenu, Id: "file"));
        var recent = menu.Find("file")?.Submenu?.FirstOrDefault(item => item.Role == MenuRole.OpenRecent);
        check(recent?.Submenu is { Length: 1 } empty && empty[0].Disabled, "the Windows File menu includes an empty, disabled Open Recent menu");
        var document = Path.Combine(root, "menu-document.txt");
        app.AddRecentDocument(document);
        var entry = recent?.Submenu?.FirstOrDefault();
        check(entry?.Label == "menu-document.txt", "Open Recent regenerates with the recorded document");
        string? received = null;
        void Open(string path) => received = path;
        app.OpenFile += Open;
        entry?.Click?.Invoke(entry);
        app.OpenFile -= Open;
        check(received == document, "choosing a Windows recent document delivers OpenFile");
        var clear = recent?.Submenu?.LastOrDefault();
        clear?.Click?.Invoke(clear);
        check(app.RecentDocuments.Count == 0 && recent?.Submenu is { Length: 1 } cleared && cleared[0].Disabled, "Clear Menu empties the private hidden-app history");
    }

    static void JumpListLinks(App app, string root, Action<bool, string> check)
    {
        var executable = Environment.ProcessPath!;
        string[] documents = [Path.Combine(root, "-dash first.txt"), Path.Combine(root, "folder with spaces") + "\\"];
        foreach (var document in documents)
        {
            var arguments = WindowsRecentDocumentShell.Arguments(document);
            var link = WindowsRecentDocumentShell.CreateLink(executable, document, arguments);
            check(link != 0, "the shell accepts a titled Jump List link for " + Path.GetFileName(document.TrimEnd('\\')));
            if (link == 0)
                continue;
            var stored = WindowsRecentDocumentShell.LinkArguments(link);
            Marshal.Release(link);
            var received = new List<string>();
            void Open(string path) => received.Add(path);
            app.OpenFile += Open;
            app.ParseOpenArguments(Split("\"" + executable + "\" " + stored).Skip(1), root);
            app.OpenFile -= Open;
            check(stored == arguments && received.SequenceEqual([Path.GetFullPath(document)]),
                "a Jump List launch delivers exactly its document through OpenFile, even with a leading dash or trailing separator");
        }
    }

    static void LoginProvenance(App app, string root, Action<bool, string> check)
    {
        // Construction and command formatting never read or write the Run key.
        var startup = new NativeStartupItem(app, "dev.kumiko.windows-shell-checks", "Windows shell checks");
        var arguments = Split(startup.WindowsCommand).Skip(1).ToArray();
        check(startup.LaunchedWith(arguments), "the owned Run command marks a login launch");
        check(!startup.LaunchedWith(["--kumiko-login=dev.kumiko.other"]) && !startup.LaunchedWith(["--", arguments[0]]) && !startup.LaunchedWith([]),
            "another identity's marker, an operand after --, or no marker is not a login launch");
        int opened = 0;
        void Open(string path) => opened++;
        app.OpenFile += Open;
        app.ParseOpenArguments(arguments, root);
        app.OpenFile -= Open;
        check(opened == 0, "the login marker never becomes an OpenFile operand");
    }

    static void LegacyNetwork(App app, Action<bool, string> check)
    {
        check(WindowsLegacyNetworkBackend.Status(0x20, 0) is { Connectivity: Connectivity.Local, Expensive: null, Constrained: null },
            "Network List Manager cost that is unknown stays null");
        int owner = Environment.CurrentManagedThreadId;
        int changes = 0;
        bool ownerOnly = true;
        using var monitor = new NetworkMonitor(app, new WindowsLegacyNetworkBackend());
        monitor.Changed += _ =>
        {
            changes++;
            ownerOnly &= Environment.CurrentManagedThreadId == owner;
        };
        // One bounded event-loop wait for the initial interface notification, not an application polling loop.
        app.RunFor(TimeSpan.FromMilliseconds(500));
        check(monitor.Current.Connectivity != Connectivity.Unknown && changes >= 1 && ownerOnly,
            "the pre-2004 fallback publishes its initial Network List Manager snapshot on the App thread");
    }

    static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == 0)
            return [];
        try
        {
            return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")]
    static extern nint LocalFree(nint memory);
}
