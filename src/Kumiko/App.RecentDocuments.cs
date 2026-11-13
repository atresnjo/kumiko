using Kumiko.Desktop;
using Kumiko.Platform;

namespace Kumiko;

public sealed partial class App
{
    List<string>? recentDocuments;
    RecentDocumentHistory? recentHistory;
    internal event Action? RecentDocumentsChanged;

    /// <summary>
    /// Windows production history: JSON under the app data folder plus the shell recent list and this app's Jump
    /// List. Hidden apps have none unless a test injects a private one before the first read.
    /// </summary>
    internal RecentDocumentHistory? RecentHistory
    {
        get => recentHistory ??= !Options.Hidden && OperatingSystem.IsWindows()
            ? new(new(Path.Combine(Paths.Get(DirectoryKind.AppData), Options.Name, "recent-documents.json")), new WindowsRecentDocumentShell())
            : null;
        set => recentHistory = value;
    }

    /// <summary>Returns the ten most recently recorded documents. Hidden apps use private history.</summary>
    public IReadOnlyList<string> RecentDocuments
    {
        get
        {
            CheckThread();
            if (recentDocuments is null)
            {
                if (RecentHistory is { } history)
                    recentDocuments = history.Store.Load();
                else
                {
                    recentDocuments = new();
                    if (!Options.Hidden)
                    {
                        RequireSystemRecentDocuments();
                        using var pool = new AutoreleasePool();
                        var urls = ObjC.O(RecentDocumentController(), "recentDocumentURLs");
                        int count = Math.Min(RecentDocumentStore.Limit, (int)ObjC.N(urls, "count"));
                        for (int i = 0; i < count; i++)
                            recentDocuments.Add(ObjC.Text(ObjC.O(ObjC.O(urls, "objectAtIndex:", (nuint)i), "path")));
                    }
                }
            }
            return recentDocuments.ToArray();
        }
    }

    public void AddRecentDocument(string path)
    {
        CheckThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        _ = RecentDocuments;
        var history = RecentHistory;
        if (history is null && !Options.Hidden)
        {
            using var pool = new AutoreleasePool();
            ObjC.V(RecentDocumentController(), "noteNewRecentDocumentURL:", ObjC.O(ObjC.Class("NSURL"), "fileURLWithPath:", ObjC.TempString(fullPath)));
        }
        var comparer = RecentDocumentStore.Comparer;
        recentDocuments!.RemoveAll(entry => comparer.Equals(entry, fullPath));
        recentDocuments.Insert(0, fullPath);
        if (recentDocuments.Count > RecentDocumentStore.Limit)
            recentDocuments.RemoveRange(RecentDocumentStore.Limit, recentDocuments.Count - RecentDocumentStore.Limit);
        if (history is not null)
        {
            SaveRecentHistory(history);
            history.Shell.Added(fullPath);
            history.Shell.Publish(recentDocuments.ToArray());
        }
        RecentDocumentsChanged?.Invoke();
    }

    public void ClearRecentDocuments()
    {
        CheckThread();
        _ = RecentDocuments;
        var history = RecentHistory;
        if (history is null && !Options.Hidden)
        {
            using var pool = new AutoreleasePool();
            ObjC.V(RecentDocumentController(), "clearRecentDocuments:", (nint)0);
        }
        recentDocuments!.Clear();
        if (history is not null)
        {
            SaveRecentHistory(history);
            history.Shell.Clear();
        }
        RecentDocumentsChanged?.Invoke();
    }

    // Recording history is a side effect of a successful open or save; a storage failure is reported, not thrown.
    void SaveRecentHistory(RecentDocumentHistory history)
    {
        try
        {
            history.Store.Save(recentDocuments!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report(ex);
        }
    }

    static nint RecentDocumentController() => ObjC.O(ObjC.Class("NSDocumentController"), "sharedDocumentController");
    static void RequireSystemRecentDocuments()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("System recent-document history is currently supported on macOS and Windows only.");
    }
}
