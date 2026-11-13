using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kumiko.Desktop;

/// <summary>App-owned recent-document history: most recent first, deduplicated and capped like AppKit's list.</summary>
internal sealed class RecentDocumentStore(string path)
{
    internal const int Limit = 10;
    internal static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal string Path { get; } = path;

    /// <summary>Missing, unreadable or malformed history is empty; entries that are not fully qualified are skipped.</summary>
    internal List<string> Load()
    {
        var documents = new List<string>();
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path));
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("documents", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return documents;
            foreach (var entry in entries.EnumerateArray())
                if (documents.Count < Limit && entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } document &&
                    System.IO.Path.IsPathFullyQualified(document) && !documents.Contains(document, Comparer))
                    documents.Add(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            documents.Clear();
        }
        return documents;
    }

    /// <summary>Writes a sibling temporary file and renames it over the history, so readers never see a partial list.</summary>
    internal void Save(IReadOnlyList<string> documents)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var json = new Utf8JsonWriter(stream, new()
            {
                Indented = true
            }))
            {
                json.WriteStartObject();
                json.WriteStartArray("documents");
                foreach (var document in documents.Take(Limit))
                    json.WriteStringValue(document);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            File.Move(temporary, Path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

/// <summary>Operating-system publication of the app-owned history. Tests inject a private fixture.</summary>
internal interface IRecentDocumentShell
{
    void Added(string path);
    void Publish(IReadOnlyList<string> documents);
    void Clear();
}

internal sealed class RecentDocumentHistory(RecentDocumentStore store, IRecentDocumentShell shell)
{
    internal RecentDocumentStore Store { get; } = store;
    internal IRecentDocumentShell Shell { get; } = shell;
}

// Interfaces, class IDs and vtable slots are checked against mingw-w64 shobjidl.h, objectarray.h, propsys.h,
// propidl.h, propkey.h and shlobj.h: ICustomDestinationList SetAppID 3, BeginList 4, AppendCategory 5,
// CommitList 8, DeleteList 10, AbortList 11; IObjectArray GetCount 3, GetAt 4; IObjectCollection AddObject 5;
// IShellLinkW SetDescription 7, GetArguments 10, SetArguments 11, SetPath 20; IPropertyStore SetValue 6,
// Commit 7. PROPVARIANT keeps vt at offset 0 and its value at offset 8 (24 bytes on 64-bit targets).
internal sealed unsafe class WindowsRecentDocumentShell : IRecentDocumentShell
{
    static readonly Guid DestinationListClass = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");
    static readonly Guid CustomDestinationList = new("6332debf-87b5-4670-90c0-5e57b408a49e");
    static readonly Guid CollectionClass = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");
    static readonly Guid ObjectCollection = new("5632b1a4-e38a-400a-928a-d4cd63230295");
    static readonly Guid ObjectArray = new("92ca9dcd-5622-4bba-a805-5e9f541bd8c9");
    static readonly Guid ShellLinkClass = new("00021401-0000-0000-c000-000000000046");
    static readonly Guid ShellLink = new("000214f9-0000-0000-c000-000000000046");
    static readonly Guid PropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
    static readonly Guid SummaryInformation = new("f29f85e0-4ff9-1068-ab91-08002b27b3d9");
    const int QueryInterface = 0, Release = 2;
    const int SetAppID = 3, BeginList = 4, AppendCategory = 5, CommitList = 8, DeleteList = 10, AbortList = 11;
    const int GetCount = 3, GetAt = 4, AddObject = 5;
    const int SetDescription = 7, GetArguments = 10, SetArguments = 11, SetPath = 20;
    const int SetValue = 6, Commit = 7;
    const uint ShardPathW = 3, InprocServer = 1, TitleProperty = 2;
    const ushort WideStringType = 31;
    const string Category = "Recent";

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;
        [FieldOffset(8)]
        public nint Pointer;
    }

    // Jump List entries relaunch this apphost; a framework-dependent `dotnet` host cannot be relaunched alone.
    readonly string? executable = Environment.ProcessPath is { } path && !System.IO.Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? path : null;

    public void Added(string path)
    {
        // SHAddToRecentDocs(…, NULL) erases the user's whole recent list; only ever pass one concrete path.
        if (string.IsNullOrEmpty(path))
            return;
        fixed (char* value = path)
            SHAddToRecentDocs(ShardPathW, value);
    }

    /// <summary>Replaces this app's custom Jump List category. Entries the user removed are not re-added in the same commit.</summary>
    public void Publish(IReadOnlyList<string> documents)
    {
        if (executable is null)
            return;
        var com = CoInitializeEx(0, 2);
        nint list = 0, removed = 0, collection = 0;
        bool begun = false;
        try
        {
            if (!CreateList(out list))
                return;
            uint slots;
            Guid removedId = ObjectArray;
            if (((delegate* unmanaged<nint, uint*, Guid*, nint*, int>)Slot(list, BeginList))(list, &slots, &removedId, &removed) < 0)
                return;
            begun = true;
            var excluded = RemovedArguments(removed);
            Guid collectionClass = CollectionClass, collectionId = ObjectCollection;
            if (CoCreateInstance(&collectionClass, 0, InprocServer, &collectionId, &collection) < 0)
                return;
            int added = 0;
            foreach (var document in documents)
            {
                var arguments = Arguments(document);
                if (excluded.Contains(arguments))
                    continue;
                var link = CreateLink(executable, document, arguments);
                if (link == 0)
                    continue;
                if (((delegate* unmanaged<nint, nint, int>)Slot(collection, AddObject))(collection, link) >= 0)
                    added++;
                ReleaseObject(link);
            }
            // AppendCategory reports E_ACCESSDENIED when the user turned off recent items; commit an empty list then.
            if (added > 0)
                fixed (char* name = Category)
                    _ = ((delegate* unmanaged<nint, char*, nint, int>)Slot(list, AppendCategory))(list, name, collection);
            if (((delegate* unmanaged<nint, int>)Slot(list, CommitList))(list) >= 0)
                begun = false;
        }
        finally
        {
            if (begun)
                _ = ((delegate* unmanaged<nint, int>)Slot(list, AbortList))(list);
            ReleaseObject(collection);
            ReleaseObject(removed);
            ReleaseObject(list);
            if (com >= 0)
                CoUninitialize();
        }
    }

    /// <summary>Deletes only this application's Jump List; the user's system-wide recent items are untouched.</summary>
    public void Clear()
    {
        var com = CoInitializeEx(0, 2);
        nint list = 0;
        try
        {
            if (!CreateList(out list))
                return;
            fixed (char* id = ExplicitApplicationId())
                _ = ((delegate* unmanaged<nint, char*, int>)Slot(list, DeleteList))(list, id);
        }
        finally
        {
            ReleaseObject(list);
            if (com >= 0)
                CoUninitialize();
        }
    }

    /// <summary>Operands follow `--`, so OpenFile receives the path even when its name starts with '-'.</summary>
    internal static string Arguments(string document)
    {
        // CommandLineToArgvW treats backslashes before a closing quote as escapes; double a trailing run.
        int trailing = document.Length - document.TrimEnd('\\').Length;
        return "-- \"" + document + new string('\\', trailing) + "\"";
    }

    /// <summary>An IShellLinkW that relaunches the app with the document, titled by file name; 0 if the shell rejects it.</summary>
    internal static nint CreateLink(string executable, string document, string arguments)
    {
        nint link = 0, store = 0;
        try
        {
            Guid linkClass = ShellLinkClass, linkId = ShellLink, storeId = PropertyStore;
            if (CoCreateInstance(&linkClass, 0, InprocServer, &linkId, &link) < 0)
                return 0;
            var name = System.IO.Path.GetFileName(document);
            var title = name.Length > 0 ? name : document;
            fixed (char* target = executable)
            fixed (char* operands = arguments)
            fixed (char* description = document)
            fixed (char* text = title)
            {
                if (((delegate* unmanaged<nint, char*, int>)Slot(link, SetPath))(link, target) < 0 ||
                    ((delegate* unmanaged<nint, char*, int>)Slot(link, SetArguments))(link, operands) < 0)
                    return 0;
                // The tooltip is informational; an over-long path must not drop the entry.
                _ = ((delegate* unmanaged<nint, char*, int>)Slot(link, SetDescription))(link, description);
                if (((delegate* unmanaged<nint, Guid*, nint*, int>)Slot(link, QueryInterface))(link, &storeId, &store) < 0)
                    return 0;
                var key = new PropertyKey
                {
                    Format = SummaryInformation,
                    Id = TitleProperty
                };
                var value = new PropVariant
                {
                    Type = WideStringType,
                    Pointer = (nint)text
                };
                // SetValue copies the string; the pinned buffer only needs to live for the call.
                if (((delegate* unmanaged<nint, PropertyKey*, PropVariant*, int>)Slot(store, SetValue))(store, &key, &value) < 0 ||
                    ((delegate* unmanaged<nint, int>)Slot(store, Commit))(store) < 0)
                    return 0;
            }
            var result = link;
            link = 0;
            return result;
        }
        finally
        {
            ReleaseObject(store);
            ReleaseObject(link);
        }
    }

    internal static string LinkArguments(nint link)
    {
        var buffer = new char[32768];
        fixed (char* arguments = buffer)
        {
            if (((delegate* unmanaged<nint, char*, int, int>)Slot(link, GetArguments))(link, arguments, buffer.Length) < 0)
                return "";
            return new string(arguments);
        }
    }

    static HashSet<string> RemovedArguments(nint removed)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        uint count;
        if (removed == 0 || ((delegate* unmanaged<nint, uint*, int>)Slot(removed, GetCount))(removed, &count) < 0)
            return result;
        Guid linkId = ShellLink;
        for (uint i = 0; i < count; i++)
        {
            nint link = 0;
            if (((delegate* unmanaged<nint, uint, Guid*, nint*, int>)Slot(removed, GetAt))(removed, i, &linkId, &link) < 0)
                continue;
            result.Add(LinkArguments(link));
            ReleaseObject(link);
        }
        return result;
    }

    static bool CreateList(out nint list)
    {
        Guid listClass = DestinationListClass, listId = CustomDestinationList;
        nint created = 0;
        list = 0;
        if (CoCreateInstance(&listClass, 0, InprocServer, &listId, &created) < 0)
            return false;
        list = created;
        // Without an explicit process AppUserModelID the shell uses the one it derived for this executable.
        if (ExplicitApplicationId() is { } id)
            fixed (char* value = id)
                _ = ((delegate* unmanaged<nint, char*, int>)Slot(list, SetAppID))(list, value);
        return true;
    }

    static string? ExplicitApplicationId()
    {
        if (GetCurrentProcessExplicitAppUserModelID(out var id) < 0 || id == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(id);
        }
        finally
        {
            Marshal.FreeCoTaskMem(id);
        }
    }

    static nint Slot(nint value, int index) => (*(nint**)value)[index];
    static void ReleaseObject(nint value)
    {
        if (value != 0)
            ((delegate* unmanaged<nint, uint>)Slot(value, Release))(value);
    }

    [DllImport("shell32.dll")]
    static extern void SHAddToRecentDocs(uint flags, void* value);
    [DllImport("shell32.dll")]
    static extern int GetCurrentProcessExplicitAppUserModelID(out nint id);
    [DllImport("ole32.dll")]
    static extern int CoInitializeEx(nint reserved, uint model);
    [DllImport("ole32.dll")]
    static extern void CoUninitialize();
    [DllImport("ole32.dll")]
    static extern int CoCreateInstance(Guid* cls, nint outer, uint context, Guid* iid, nint* instance);
}
