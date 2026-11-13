using System.Runtime.InteropServices;
using System.Text;
using Kumiko.Platform;
using O = Kumiko.Platform.ObjC;

namespace Kumiko.Desktop;

/// <summary>
/// Fresh, unbound NSPasteboardItems promising one snapshot. Any pasteboard (clipboard, dragging
/// or private) may adopt the items exactly once. Disposal releases the native providers without
/// fetching; the caller owns the snapshot and its release hook.
/// </summary>
internal sealed unsafe class MacPasteboardWriters : IDisposable
{
    static readonly Dictionary<nint, (MacPasteboardWriters Owner, int Index)> providers = new();
    readonly App app;
    readonly TransferSnapshot snapshot;
    readonly Action? finished;
    readonly nint[] objects;
    readonly string[][] natives;
    readonly nint items;
    int remaining;
    bool disposed;

    /// <summary>Live native providers across all owners; zero once every owner is released.</summary>
    internal static int Live => providers.Count;
    /// <summary>A retained NSArray of NSPasteboardWriting items, in TransferData item order.</summary>
    internal nint Items => items;
    internal string[] NativeTypes(int index) => natives[index];
    internal Exception? Error
    {
        get;
        private set;
    }

    // finished runs once after the adopting pasteboard has finished with every provider.
    internal MacPasteboardWriters(App app, TransferSnapshot snapshot, Action? finished = null)
    {
        app.CheckThread();
        this.app = app;
        this.snapshot = snapshot;
        this.finished = finished;
        using var pool = new AutoreleasePool();
        var cls = O.Define("KumikoPasteboardProvider", "NSObject", ("pasteboard:item:provideDataForType:", (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, void>)&Provide, "v@:@@@"), ("pasteboardFinishedWithDataProvider:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&Finished, "v@:@"));
        O.class_addProtocol(cls, O.objc_getProtocol("NSPasteboardItemDataProvider"));
        var data = snapshot.Data.Items;
        objects = new nint[data.Count];
        natives = new string[data.Count][];
        items = O.O(O.O(O.Class("NSMutableArray"), "alloc"), "init");
        try
        {
            for (int i = 0; i < data.Count; i++)
                Promise(cls, i, data[i]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    void Promise(nint cls, int index, TransferItem data)
    {
        natives[index] = data.Formats.Select(MacTransferFormats.Native).ToArray();
        var item = O.New("NSPasteboardItem");
        try
        {
            var provider = O.O(cls, "new");
            objects[index] = provider;
            providers[provider] = (this, index);
            remaining++;
            var types = O.O(O.Class("NSMutableArray"), "array");
            foreach (var type in natives[index])
                O.V(types, "addObject:", O.TempString(type));
            if (O.B(item, "setDataProvider:forTypes:", provider, types) == 0)
                throw new InvalidOperationException($"NSPasteboardItem rejected the promised types {string.Join(", ", natives[index])}.");
            O.V(items, "addObject:", item);
        }
        finally
        {
            O.Release(item);
        }
    }

    [UnmanagedCallersOnly]
    static void Provide(nint self, nint cmd, nint board, nint item, nint type)
    {
        if (!providers.TryGetValue(self, out var context))
            return;
        var owner = context.Owner;
        try
        {
            var native = O.Text(type);
            int position = Array.IndexOf(owner.natives[context.Index], native);
            if (position < 0)
                throw new KeyNotFoundException($"Pasteboard type {native} was not promised.");
            var format = owner.snapshot.Data.Items[context.Index].Formats[position];
            var bytes = owner.snapshot.ReadItem(context.Index, format);
            if (native is "public.url" or "public.file-url")
                bytes = SingleUrl(bytes);
            fixed (byte* p = bytes)
                O.B(item, "setData:forType:", O.O(O.Class("NSData"), "dataWithBytes:length:", (nint)p, (nuint)bytes.Length), type);
        }
        catch (Exception ex)
        {
            owner.Error = ex;
            owner.app.Report(ex);
        }
    }

    static byte[] SingleUrl(byte[] bytes)
    {
        var urls = Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Where(line => !line.StartsWith('#')).ToArray();
        if (urls.Length != 1)
            throw new InvalidOperationException("AppKit URL representations require one URL per TransferItem. Use separate ordered items for multiple URLs.");
        return Encoding.UTF8.GetBytes(urls[0]);
    }

    [UnmanagedCallersOnly]
    static void Finished(nint self, nint cmd, nint board)
    {
        if (!providers.TryGetValue(self, out var context))
            return;
        var owner = context.Owner;
        try
        {
            if (--owner.remaining == 0)
                owner.finished?.Invoke();
        }
        catch (Exception ex)
        {
            owner.app.Report(ex);
        }
    }

    /// <summary>Requests every promised representation from an adopting pasteboard so it outlives the providers.</summary>
    internal void Persist(nint board)
    {
        app.CheckThread();
        ObjectDisposedException.ThrowIf(disposed, this);
        using var pool = new AutoreleasePool();
        var written = O.O(board, "pasteboardItems");
        var count = Math.Min((int)O.N(written, "count"), natives.Length);
        for (int i = 0; i < count; i++)
        {
            var item = O.O(written, "objectAtIndex:", (nuint)i);
            foreach (var type in natives[i])
                O.O(item, "dataForType:", O.TempString(type));
        }
    }

    public void Dispose()
    {
        app.CheckThread();
        if (disposed)
            return;
        disposed = true;
        // Items retain their providers; unmapped providers answer later AppKit callbacks with no data.
        foreach (var provider in objects.Where(p => p != 0))
        {
            providers.Remove(provider);
            O.Release(provider);
        }

        O.Release(items);
    }
}
