using System.Runtime.InteropServices;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

internal sealed unsafe class MacClipboardBackend : IClipboardBackend
{
    readonly App app;
    readonly nint pasteboard;
    readonly bool unique;
    MacPasteboardWriters? writers;
    TransferSnapshot? snapshot;
    nuint writtenChange;
    bool disposed;

    // An isolated backend uses a private pasteboardWithUniqueName, released globally on disposal.
    internal MacClipboardBackend(App app, bool isolated = false)
    {
        this.app = app;
        unique = isolated;
        using var pool = new AutoreleasePool();
        pasteboard = O.O(O.O(O.Class("NSPasteboard"), isolated ? "pasteboardWithUniqueName" : "generalPasteboard"), "retain");
    }

    /// <summary>Adopts an existing pasteboard, such as a dragging or test pasteboard, without owning its name.</summary>
    internal MacClipboardBackend(App app, nint pasteboard)
    {
        app.CheckThread();
        this.app = app;
        this.pasteboard = O.O(pasteboard, "retain");
    }

    internal nint Pasteboard => pasteboard;
    internal string Name => O.Text(O.O(pasteboard, "name"));
    internal nint NativeItems => O.O(pasteboard, "pasteboardItems");

    /// <summary>Distinct item types in order. Item types exclude legacy aliases, and discovery never fetches data.</summary>
    public IReadOnlyList<string> Formats
    {
        get
        {
            Check();
            using var pool = new AutoreleasePool();
            var items = O.O(pasteboard, "pasteboardItems");
            var formats = new List<string>();
            for (nuint i = 0; i < O.N(items, "count"); i++)
            {
                var types = O.O(O.O(items, "objectAtIndex:", i), "types");
                for (nuint j = 0; j < O.N(types, "count"); j++)
                {
                    var format = MacTransferFormats.Portable(O.Text(O.O(types, "objectAtIndex:", j)));
                    if (!formats.Contains(format))
                        formats.Add(format);
                }
            }

            return formats.AsReadOnly();
        }
    }

    public TransferData Read(string[] formats) => ReadCore(formats, false);
    /// <summary>Reads from the native pasteboard even while this backend owns it, bypassing the snapshot cache.</summary>
    internal TransferData ReadForeign(string[] formats) => ReadCore(formats, true);

    // A requested type whose native data is unavailable fails; a default read skips it.
    TransferData ReadCore(string[] formats, bool foreign)
    {
        Check();
        using var pool = new AutoreleasePool();
        var revision = O.N(pasteboard, "changeCount");
        if (!foreign && snapshot is not null && revision == writtenChange)
            return snapshot.Materialize(formats);
        var items = O.O(pasteboard, "pasteboardItems");
        var result = new List<TransferItem>();
        for (nuint i = 0; i < O.N(items, "count"); i++)
        {
            var item = O.O(items, "objectAtIndex:", i);
            var available = O.O(item, "types");
            var values = new List<TransferRepresentation>();
            for (nuint j = 0; j < O.N(available, "count"); j++)
            {
                var type = O.O(available, "objectAtIndex:", j);
                var native = O.Text(type);
                var format = MacTransferFormats.Portable(native);
                if (formats.Length > 0 && !formats.Contains(format) || values.Any(v => v.Format == format))
                    continue;
                var data = O.O(item, "dataForType:", type);
                if (data == 0)
                {
                    if (formats.Length > 0)
                        throw new IOException($"Pasteboard type {native} on item {i} provided no data for {format}.");
                    continue;
                }

                nuint length = O.N(data, "length");
                if (length > TransferSnapshot.MaximumForeignBytes)
                    throw new IOException("Foreign clipboard representation exceeds 64 MiB.");
                var bytes = new byte[(int)length];
                if (bytes.Length > 0)
                    Marshal.Copy(O.O(data, "bytes"), bytes, 0, bytes.Length);
                values.Add(new(format, bytes));
            }

            if (values.Count > 0)
                result.Add(new(values.ToArray()));
        }

        if (O.N(pasteboard, "changeCount") != revision)
            throw new IOException("Clipboard ownership changed during the read.");
        if (formats.Length > 0 && result.Count == 0)
            throw new KeyNotFoundException("None of the requested clipboard formats are available.");
        return new(result.ToArray());
    }

    public void Write(TransferSnapshot next)
    {
        Check();
        using var pool = new AutoreleasePool();
        Clear();
        MacPasteboardWriters? created = null;
        created = new(app, next, () => Finished(created!));
        writers = created;
        snapshot = next;
        if (next.Data.Items.Count > 0 && O.B(pasteboard, "writeObjects:", created.Items) == 0)
        {
            Release();
            throw new IOException("NSPasteboard rejected the transfer data.");
        }

        writtenChange = O.N(pasteboard, "changeCount");
    }

    // Another owner replaced the contents: release the snapshot now rather than at the next write.
    void Finished(MacPasteboardWriters finished)
    {
        if (writers != finished)
            return;
        var released = snapshot;
        Release();
        released?.Dispose();
    }

    public void Clear()
    {
        Check();
        O.N(pasteboard, "clearContents");
        Release();
    }

    void Release()
    {
        var current = writers;
        writers = null;
        snapshot = null;
        current?.Dispose();
    }

    public void Flush()
    {
        Check();
        if (writers is not { } current || O.N(pasteboard, "changeCount") != writtenChange)
            return;
        current.Persist(pasteboard);
        if (current.Error is { } failure)
            throw new IOException("A clipboard provider failed during Flush.", failure);
    }

    void Check()
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    /// <summary>Persists data this backend still owns, then always releases providers and the pasteboard.</summary>
    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        try
        {
            // Never clear another owner's pasteboard on disposal. Provider failures were already reported.
            if (writers is { } current && O.N(pasteboard, "changeCount") == writtenChange)
                current.Persist(pasteboard);
        }
        catch (Exception ex)
        {
            app.Report(ex);
        }
        finally
        {
            Release();
            if (unique)
                O.V(pasteboard, "releaseGlobally");
            O.Release(pasteboard);
        }
    }
}
