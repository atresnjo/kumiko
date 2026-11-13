using System.Runtime.InteropServices;
using System.Text;
using Kumiko;
using Kumiko.Desktop;
using Kumiko.Platform;
using Kumiko.UI;

static partial class ReviewTransfers
{
    static readonly nint objc = NativeLibrary.Load("/usr/lib/libobjc.A.dylib");

    /// <summary>Sends the real NSDraggingSource end message, as AppKit does when a session finishes.</summary>
    static unsafe void EndSession(nint source, nuint operation)
    {
        var send = (delegate* unmanaged<nint, nint, nint, NativePoint, nuint, void>)NativeLibrary.GetExport(objc, "objc_msgSend");
        send(source, ObjC.Sel("draggingSession:endedAtPoint:operation:"), 0, default, operation);
    }

    static void MacCacheFlows(Action<bool, string> check)
    {
        using var app = new App(new("Review clipboard cache", QuitOnLastWindow: false, Hidden: true));
        using var pool = new AutoreleasePool();
        var backend = new MacClipboardBackend(app, isolated: true);
        using var scope = Clipboard.Attach(app, backend);
        int fetches = 0;
        Clipboard.Write(new(new TransferItem(new TransferRepresentation(Custom, Lazy(() => fetches++, "owned custom")))));
        check(Clipboard.Formats.SequenceEqual([Custom]) && fetches == 0, "custom MIME discovery on a named pasteboard fetches nothing");
        var foreign = backend.ReadForeign([Custom]);
        check(fetches == 1 && Encoding.UTF8.GetString(new TransferSnapshot(app, foreign).ReadItem(0, Custom)) == "owned custom", "a foreign read of an owned custom MIME goes through the native provider");
        check(Encoding.UTF8.GetString(Clipboard.ReadFormat(Custom)) == "owned custom" && fetches == 1, "owned and native reads share one provider fetch");
        ObjC.N(backend.Pasteboard, "clearContents");
        WriteNative(backend.Pasteboard, NativeItem((MacTransferFormats.Native(Custom), Encoding.UTF8.GetBytes("replaced by another owner"))));
        check(Encoding.UTF8.GetString(Clipboard.ReadFormat(Custom)) == "replaced by another owner" && fetches == 1, "a changed pasteboard bypasses the owned snapshot cache");
    }

    static void MacDragFlows(Dictionary<string, string[][]> boards, List<string> events, Action<bool, string> check)
    {
        MacCacheFlows(check);
        int thread = Environment.CurrentManagedThreadId;
        using var app = new App(new("Review drag writers", QuitOnLastWindow: false, Hidden: true));
        var errors = new List<Exception>();
        app.UnhandledException += errors.Add;
        using var pool = new AutoreleasePool();
        int writers = MacPasteboardWriters.Live, sources = MacDragSource.Live;
        string file = Path.Combine(Path.GetTempPath(), "kumiko review drag #1.txt");
        byte[] fileBytes = Encoding.UTF8.GetBytes(new Uri(file).AbsoluteUri + "\r\n");
        int text = 0, custom = 0, releases = 0;
        bool releaseOnMain = false;
        var data = new TransferData(
            new TransferItem(new TransferRepresentation(TransferFormats.Text, Lazy(() => text++, "drag text")), new TransferRepresentation(Custom, Lazy(() => custom++, "{\"drag\":1}"))),
            new TransferItem(new TransferRepresentation(TransferFormats.FileList, fileBytes), new TransferRepresentation(TransferFormats.URIList, fileBytes)));
        var dragging = UniqueBoard();
        int offered = 0;
        bool accepted = false;
        var drag = new MacDragSource(app, data, new Rect(10, 10, 80, 24), (items, source) =>
        {
            var array = ObjC.O(ObjC.Class("NSMutableArray"), "array");
            for (nuint i = 0; i < ObjC.N(items, "count"); i++)
            {
                ObjC.V(array, "addObject:", ObjC.O(ObjC.O(items, "objectAtIndex:", i), "item"));
                offered++;
            }

            accepted = ObjC.B(dragging, "writeObjects:", array) != 0;
        }, () =>
        {
            releases++;
            releaseOnMain = Environment.CurrentManagedThreadId == thread;
        });
        var types = NativeTypes(dragging);
        boards["dragging"] = types;
        check(accepted && offered == 2, "the session endpoint receives fresh unbound writers a second private pasteboard accepts");
        string[] first = [MacTransferFormats.Native(TransferFormats.Text), MacTransferFormats.Native(Custom)];
        string[] second = [MacTransferFormats.Native(TransferFormats.FileList), MacTransferFormats.Native(TransferFormats.URIList)];
        check(types.Length == 2 && first.All(types[0].Contains) && second.All(types[1].Contains) && Array.IndexOf(types[0], first[0]) < Array.IndexOf(types[0], first[1]), "dragging pasteboard items preserve ordered representations");
        check(text == 0 && custom == 0 && MacPasteboardWriters.Live == writers + 2, "promising drag writers fetches no lazy representation");
        var drop = NativeBytes(dragging, 0, first[0]);
        var again = NativeBytes(dragging, 0, first[0]);
        check(drop is not null && Encoding.UTF8.GetString(drop) == "drag text" && again!.SequenceEqual(drop) && text == 1 && custom == 0, "a drop fetches only the requested representation, at most once");
        using (var target = new MacClipboardBackend(app, dragging))
        {
            var dropped = target.Read([Custom]);
            check(custom == 1 && Encoding.UTF8.GetString(new TransferSnapshot(app, dropped).ReadItem(0, Custom)) == "{\"drag\":1}", "a drop target reads the custom MIME through the drag provider");
        }

        var url = NativeBytes(dragging, 1, second[0]);
        check(url is not null && new Uri(Encoding.UTF8.GetString(url)).LocalPath == file, "file writers provide one AppKit file URL per item");
        EndSession(drag.Source, 1);
        events.Add($"completed drag text={text} custom={custom} releases={releases}");
        check(releases == 1 && releaseOnMain && MacPasteboardWriters.Live == writers && MacDragSource.Live == sources, "session end releases providers, source and snapshot exactly once on the App thread");
        drag.Dispose();
        check(releases == 1, "repeated drag disposal is a no-op");
        DropBoard(dragging);
        int lazy = 0, cancelledReleases = 0;
        var cancelledData = new TransferData(new TransferItem(new TransferRepresentation(TransferFormats.Text, Lazy(() => lazy++, "never")), new TransferRepresentation(Custom, Lazy(() => lazy++, "never"))));
        var cancelled = new MacDragSource(app, cancelledData, new Rect(0, 0, 40, 20), (_, _) => { }, () => cancelledReleases++);
        EndSession(cancelled.Source, 0);
        events.Add($"cancelled drag fetches={lazy} releases={cancelledReleases}");
        check(lazy == 0 && cancelledReleases == 1 && MacPasteboardWriters.Live == writers && MacDragSource.Live == sources, "a cancelled drag releases without fetching any lazy representation");
        int failedReleases = 0;
        bool startFailed = false;
        try
        {
            _ = new MacDragSource(app, cancelledData, new Rect(0, 0, 40, 20), (_, _) => throw new InvalidOperationException("no pointer event"), () => failedReleases++);
        }
        catch (InvalidOperationException)
        {
            startFailed = true;
        }

        check(startFailed && failedReleases == 1 && lazy == 0 && MacPasteboardWriters.Live == writers && MacDragSource.Live == sources, "a failed session start releases its writers and snapshot without fetching");
        int failingFetches = 0;
        var failing = UniqueBoard();
        var broken = new MacDragSource(app, new(new TransferItem(new TransferRepresentation(Custom, Failing(() => failingFetches++, "drag provider failure")))), new Rect(0, 0, 40, 20), (items, _) =>
        {
            var array = ObjC.O(ObjC.Class("NSMutableArray"), "array");
            ObjC.V(array, "addObject:", ObjC.O(ObjC.O(items, "objectAtIndex:", (nuint)0), "item"));
            ObjC.B(failing, "writeObjects:", array);
        }, () => throw new InvalidOperationException("drag release failure"));
        var missing = NativeBytes(failing, 0, MacTransferFormats.Native(Custom));
        NativeBytes(failing, 0, MacTransferFormats.Native(Custom));
        EndSession(broken.Source, 0);
        DropBoard(failing);
        events.Add($"failing drag fetches={failingFetches} reports={errors.Count}");
        check(missing is null && failingFetches == 1, "a failing drag provider runs at most once and provides no data");
        check(errors.Any(e => e.Message == "drag provider failure") && errors.Any(e => e.Message == "drag release failure"), "drag provider and release failures are reported without escaping native callbacks");
        check(MacPasteboardWriters.Live == writers && MacDragSource.Live == sources, "every drag owner is released after its session ends");
    }
}
