using System.Runtime.InteropServices;
using System.Text;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;

static partial class ReviewTransfers
{
    const string Custom = "application/x-kumiko-review";

    static bool Released(string name)
    {
        using var pool = new AutoreleasePool();
        var board = ObjC.O(ObjC.Class("NSPasteboard"), "pasteboardWithName:", ObjC.TempString(name));
        bool empty = ObjC.N(ObjC.O(board, "types"), "count") == 0;
        ObjC.V(board, "releaseGlobally");
        return empty;
    }

    static nint UniqueBoard()
    {
        var board = ObjC.O(ObjC.O(ObjC.Class("NSPasteboard"), "pasteboardWithUniqueName"), "retain");
        ObjC.N(board, "clearContents");
        return board;
    }

    static void DropBoard(nint board)
    {
        ObjC.V(board, "releaseGlobally");
        ObjC.Release(board);
    }

    static unsafe nint Data(byte[] bytes)
    {
        fixed (byte* p = bytes)
            return ObjC.O(ObjC.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)bytes.Length);
    }

    static nint NativeItem(params (string Type, byte[] Bytes)[] values)
    {
        var item = ObjC.O(ObjC.New("NSPasteboardItem"), "autorelease");
        foreach (var (type, bytes) in values)
            ObjC.B(item, "setData:forType:", Data(bytes), ObjC.TempString(type));
        return item;
    }

    static void WriteNative(nint board, params nint[] items)
    {
        var array = ObjC.O(ObjC.Class("NSMutableArray"), "array");
        foreach (var item in items)
            ObjC.V(array, "addObject:", item);
        if (ObjC.B(board, "writeObjects:", array) == 0)
            throw new IOException("The fixture pasteboard rejected native items.");
    }

    internal static string[][] NativeTypes(nint board)
    {
        using var pool = new AutoreleasePool();
        var items = ObjC.O(board, "pasteboardItems");
        var result = new string[(int)ObjC.N(items, "count")][];
        for (int i = 0; i < result.Length; i++)
        {
            var types = ObjC.O(ObjC.O(items, "objectAtIndex:", (nuint)i), "types");
            result[i] = new string[(int)ObjC.N(types, "count")];
            for (int j = 0; j < result[i].Length; j++)
                result[i][j] = ObjC.Text(ObjC.O(types, "objectAtIndex:", (nuint)j));
        }

        return result;
    }

    static byte[]? NativeBytes(nint board, int index, string type)
    {
        using var pool = new AutoreleasePool();
        var item = ObjC.O(ObjC.O(board, "pasteboardItems"), "objectAtIndex:", (nuint)index);
        var data = ObjC.O(item, "dataForType:", ObjC.TempString(type));
        if (data == 0)
            return null;
        var bytes = new byte[(int)ObjC.N(data, "length")];
        if (bytes.Length > 0)
            Marshal.Copy(ObjC.O(data, "bytes"), bytes, 0, bytes.Length);
        return bytes;
    }

    static void MacTeardownFlows(List<string> events, Action<bool, string> check)
    {
        int thread = Environment.CurrentManagedThreadId;
        var errors = new List<Exception>();
        var app = new App(new("Review native clipboard teardown", QuitOnLastWindow: false, Hidden: true));
        app.UnhandledException += errors.Add;
        Clipboard.WriteText("outer");
        int baseline = MacPasteboardWriters.Live;
        var backend = new MacClipboardBackend(app, isolated: true);
        string name = backend.Name;
        var scope = Clipboard.Attach(app, backend);
        int fetches = 0, releases = 0;
        bool releaseOnMain = false;
        var failing = new TransferRepresentation(TransferFormats.Text, Failing(() => fetches++, "lazy provider failure"));
        var eager = new TransferRepresentation(Custom, Encoding.UTF8.GetBytes("eager"));
        Clipboard.Write(new(new TransferItem(failing, eager)), () =>
        {
            releases++;
            releaseOnMain = Environment.CurrentManagedThreadId == thread;
        });
        check(MacPasteboardWriters.Live == baseline + 1 && fetches == 0, "native clipboard ownership promises lazy data without fetching");
        bool disposed = false;
        try
        {
            scope.Dispose();
            disposed = true;
        }
        catch (Exception ex)
        {
            events.Add("native scope disposal threw " + ex.GetType().Name);
        }

        events.Add($"native scope disposal fetches={fetches} releases={releases} reports={errors.Count}");
        check(disposed && fetches == 1 && releases == 1 && releaseOnMain, "disposal persistence runs a failing lazy provider once and still releases on the App thread");
        check(errors.Count == 1 && errors[0] is IOException { Message: "lazy provider failure" }, "the persistence failure is reported once without escaping disposal");
        check(MacPasteboardWriters.Live == baseline && Released(name), "disposal releases native providers and the named pasteboard");
        check(Clipboard.ReadText() == "outer", "disposal restores the previous clipboard scope");
        scope.Dispose();
        check(releases == 1 && Clipboard.ReadText() == "outer", "repeated scope disposal is a no-op");
        var attached = new MacClipboardBackend(app, isolated: true);
        string attachedName = attached.Name;
        Clipboard.Attach(app, attached);
        int teardownFetches = 0, teardownReleases = 0;
        Clipboard.Write(new(new TransferItem(new TransferRepresentation(Custom, Failing(() => teardownFetches++, "teardown provider failure")))), () => teardownReleases++);
        bool completed = false;
        try
        {
            app.Dispose();
            completed = true;
        }
        catch (Exception ex)
        {
            events.Add("native App teardown threw " + ex.GetType().Name);
        }

        events.Add($"native App teardown fetches={teardownFetches} releases={teardownReleases} reports={errors.Count}");
        check(completed && teardownFetches == 1 && teardownReleases == 1, "App teardown completes when an attached native provider fails during persistence");
        check(errors.Count == 2 && MacPasteboardWriters.Live == baseline && Released(attachedName), "App teardown reports the provider failure and releases every native clipboard resource");
    }

    static void MacFormatFlows(Action<bool, string> check)
    {
        using var app = new App(new("Review native formats", QuitOnLastWindow: false, Hidden: true));
        string[] mimes = [TransferFormats.Text, TransferFormats.HTML, TransferFormats.PNG, TransferFormats.URIList, TransferFormats.FileList, "text/rtf", "image/tiff", "application/pdf", Custom, "application/vnd.kumiko.review+json", "Application/X-Kumiko-Case", "text/plain;charset=utf-16", "application/x-kumiko-native;type=com.apple.flat-rtfd", "application/x-kumiko-native;type=public.rtf"];
        check(mimes.All(m => MacTransferFormats.Portable(MacTransferFormats.Native(m)) == m), "every MIME name maps to a native type and back unchanged");
        check(mimes.Select(MacTransferFormats.Native).Distinct().Count() == mimes.Length, "distinct MIME names never share a native type");
        check(MacTransferFormats.Native("text/rtf") == "public.rtf" && MacTransferFormats.Native("image/tiff") == "public.tiff", "registered MIME names use their system identifiers");
        check(MacTransferFormats.Native(Custom).StartsWith("dyn.", StringComparison.Ordinal), "unregistered MIME names use dynamic identifiers other applications can map back");
        string[] natives = ["public.rtf", "public.tiff", "public.utf16-external-plain-text", "com.apple.flat-rtfd", "com.adobe.pdf", "public.url", "public.file-url", "dyn.ah62d4rv4ge80", "com.example.review"];
        bool valid = natives.All(n => new TransferRepresentation(MacTransferFormats.Portable(n), []).Format.Length > 0);
        check(valid && natives.All(n => MacTransferFormats.Native(MacTransferFormats.Portable(n)) == n), "every native type maps to a valid MIME name and back unchanged");
        string[] legacy = ["NSStringPboardType", "Apple HTML pasteboard type", "NeXT TIFF v4.0 pasteboard type", "Some Opaque Name", "com.example.under_score"];
        var reoffered = legacy.Select(n => MacTransferFormats.Native(MacTransferFormats.Portable(n))).ToArray();
        check(reoffered[..3].SequenceEqual(["public.utf8-plain-text", "public.html", "public.tiff"]), "legacy pasteboard names are re-offered as the modern types AppKit maps them to");
        check(reoffered.All(MacTransferFormats.IsIdentifier) && reoffered[3].StartsWith("dyn.", StringComparison.Ordinal), "names that are not identifiers are re-offered as valid dynamic identifiers");
    }

    static void MacForeignFlows(Dictionary<string, string[][]> boards, Action<bool, string> check)
    {
        using var app = new App(new("Review foreign pasteboard", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var pool = new AutoreleasePool();
        string flat = MacTransferFormats.Portable("com.apple.flat-rtfd");
        byte[] rtf = Encoding.ASCII.GetBytes("{\\rtf1 review}");
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00];
        var board = UniqueBoard();
        WriteNative(board, NativeItem(("public.rtf", rtf), ("public.tiff", tiff), (MacTransferFormats.Native(Custom), Encoding.UTF8.GetBytes("custom")), ("com.apple.flat-rtfd", Encoding.UTF8.GetBytes("rtfd"))), NativeItem(("public.utf8-plain-text", Encoding.UTF8.GetBytes("second"))));
        boards["foreign"] = NativeTypes(board);
        using (var foreign = new MacClipboardBackend(app, board))
        {
            var formats = foreign.Formats;
            check(formats.Distinct().Count() == formats.Count && formats.All(f => new TransferRepresentation(f, []).Format == f), "foreign discovery reports each native type once as a valid MIME name");
            check(new[] { "text/rtf", "image/tiff", Custom, flat, TransferFormats.Text }.All(formats.Contains), "foreign RTF, TIFF, custom MIME and unmapped native types are discoverable");
            var all = foreign.Read([]);
            check(all.Items.Count == 2 && all.Items[0].Formats.Contains(flat), "a default read accepts types that are not MIME names");
            using var snapshot = new TransferSnapshot(app, all);
            check(snapshot.ReadItem(0, "text/rtf").SequenceEqual(rtf) && snapshot.ReadItem(0, "image/tiff").SequenceEqual(tiff), "default reads preserve foreign RTF and TIFF bytes");
            var selected = foreign.Read([Custom]);
            check(selected.Items.Count == 1 && selected.Items[0].Formats.SequenceEqual([Custom]), "selective reads return only the requested custom MIME representation");
            using var scope = Clipboard.Attach(app, foreign);
            check(Encoding.UTF8.GetString(Clipboard.ReadFormat(Custom)) == "custom" && Encoding.UTF8.GetString(Clipboard.ReadFormat(flat)) == "rtfd", "the clipboard API reads foreign custom MIME and native types from the pasteboard itself");
            var copy = new MacClipboardBackend(app, isolated: true);
            using (Clipboard.Attach(app, copy))
            {
                Clipboard.Write(all);
                var written = NativeTypes(copy.Pasteboard);
                boards["round-trip"] = written;
                var original = boards["foreign"];
                check(original.Zip(written).All(p => p.First.All(p.Second.Contains)), "writing foreign data back restores the original native types");
                var reread = copy.ReadForeign([Custom, flat]);
                check(Encoding.UTF8.GetString(new TransferSnapshot(app, reread).ReadItem(0, Custom)) == "custom", "the rewritten custom MIME round trips through the native provider");
            }
        }

        DropBoard(board);
        var legacy = UniqueBoard();
        var legacyTypes = ObjC.O(ObjC.Class("NSMutableArray"), "array");
        ObjC.V(legacyTypes, "addObject:", ObjC.TempString("NSStringPboardType"));
        ObjC.V(legacyTypes, "addObject:", ObjC.TempString("NeXT TIFF v4.0 pasteboard type"));
        ObjC.O(legacy, "declareTypes:owner:", legacyTypes, (nint)0);
        ObjC.B(legacy, "setString:forType:", ObjC.TempString("legacy"), ObjC.TempString("NSStringPboardType"));
        ObjC.B(legacy, "setData:forType:", Data(tiff), ObjC.TempString("NeXT TIFF v4.0 pasteboard type"));
        boards["legacy"] = NativeTypes(legacy);
        using (var old = new MacClipboardBackend(app, legacy))
        {
            var formats = old.Formats;
            check(formats.Contains(TransferFormats.Text) && formats.Contains("image/tiff") && !formats.Any(f => f.Contains(' ') || f.Contains("PboardType")), "legacy pasteboard names are discovered as their modern MIME types without aliases");
            using var scope = Clipboard.Attach(app, old);
            check(Clipboard.ReadText() == "legacy" && Clipboard.ReadFormat("image/tiff").SequenceEqual(tiff), "legacy text and TIFF are readable through MIME names");
        }

        var reoffer = new MacClipboardBackend(app, isolated: true);
        using (Clipboard.Attach(app, reoffer))
        {
            byte[] html = Encoding.UTF8.GetBytes("<b>legacy html</b>");
            byte[] opaque = Encoding.UTF8.GetBytes("opaque");
            string legacyHtml = MacTransferFormats.Portable("Apple HTML pasteboard type");
            string opaqueName = MacTransferFormats.Portable("Some Opaque Name");
            Clipboard.Write(new(new TransferItem(new TransferRepresentation(legacyHtml, html), new TransferRepresentation(opaqueName, opaque))));
            var reofferedTypes = NativeTypes(reoffer.Pasteboard);
            boards["legacy-reoffer"] = reofferedTypes;
            check(reofferedTypes.Length == 1 && reofferedTypes[0].Contains("public.html") && reofferedTypes[0].All(MacTransferFormats.IsIdentifier), "legacy names written through providers are promised under valid identifiers");
            var native = new TransferSnapshot(app, reoffer.ReadForeign([TransferFormats.HTML]));
            check(native.ReadItem(0, TransferFormats.HTML).SequenceEqual(html), "a re-offered legacy HTML representation is served by the native provider");
            var dynamic = reofferedTypes[0].First(t => t.StartsWith("dyn.", StringComparison.Ordinal));
            check(NativeBytes(reoffer.Pasteboard, 0, dynamic)!.SequenceEqual(opaque), "an opaque legacy name is served under its dynamic identifier");
        }

        DropBoard(legacy);
        var missing = UniqueBoard();
        var declared = ObjC.O(ObjC.Class("NSMutableArray"), "array");
        ObjC.V(declared, "addObject:", ObjC.TempString("public.rtf"));
        ObjC.V(declared, "addObject:", ObjC.TempString("public.utf8-plain-text"));
        ObjC.O(missing, "declareTypes:owner:", declared, (nint)0);
        ObjC.B(missing, "setString:forType:", ObjC.TempString("available"), ObjC.TempString("public.utf8-plain-text"));
        using (var partial = new MacClipboardBackend(app, missing))
        {
            bool explicitFailure = false;
            try
            {
                partial.Read(["text/rtf"]);
            }
            catch (IOException)
            {
                explicitFailure = true;
            }

            var remaining = partial.Read([]);
            check(explicitFailure, "a requested native type without data fails explicitly instead of returning empty bytes");
            check(remaining.Formats.SequenceEqual([TransferFormats.Text]), "default reads skip declared native types that provide no data");
        }

        DropBoard(missing);
        check(errors.Count == 0, "foreign reads report no provider errors");
    }
}
